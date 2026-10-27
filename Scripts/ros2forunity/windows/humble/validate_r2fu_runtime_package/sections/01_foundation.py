@dataclass
class CheckResult:
    """Structured result for one runtime package validation check."""

    name: str
    ok: bool
    detail: str


def rel(path: Path) -> str:
    """Format a path relative to the repository root when possible."""
    try:
        return path.resolve().relative_to(ROOT.resolve()).as_posix()
    except ValueError:
        return str(path)


def add(results: list[CheckResult], name: str, ok: bool, detail: str = "") -> None:
    """Append one check result to the accumulated report."""
    results.append(CheckResult(name, ok, detail))


def iter_files(root: Path) -> Iterable[Path]:
    """Yield files below a root, returning an empty iterable when absent."""
    if not root.exists():
        return ()
    return (path for path in root.rglob("*") if path.is_file())


def load_json(path: Path, results: list[CheckResult], name: str) -> dict:
    """Load JSON and record whether parsing succeeded."""
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except Exception as exc:
        add(results, name, False, f"{rel(path)}: {exc}")
        return {}
    add(results, name, True, rel(path))
    return data


def read_optional_text(path: Path) -> str:
    """Read UTF-8 text when present, returning an empty string for absent files."""
    return path.read_text(encoding="utf-8", errors="replace") if path.exists() else ""


def file_sha256(path: Path) -> str:
    """Return the SHA-256 digest for a package payload file."""
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def critical_runtime_file_path(name: str) -> Path:
    """Resolve critical runtime files across managed and native plugin roots."""
    if name.endswith("_assembly.dll"):
        return RUNTIME_ROOT / "Plugins" / name
    return PLUGIN_ROOT / name


def is_sha256(value: object) -> bool:
    """Return true for a complete lowercase SHA-256 hex string."""
    return isinstance(value, str) and re.fullmatch(r"[0-9a-f]{64}", value) is not None


def unity_editor_using_is_guarded(text: str) -> bool:
    """Return true when every UnityEditor using is inside a UNITY_EDITOR block."""
    in_unity_editor = False
    found = False
    for line in text.splitlines():
        stripped = line.strip()
        if stripped.startswith("#if") and "UNITY_EDITOR" in stripped:
            in_unity_editor = True
            continue
        if stripped.startswith("#endif"):
            in_unity_editor = False
            continue
        if "using UnityEditor;" in stripped:
            found = True
            if not in_unity_editor:
                return False
    return found


def check_package_metadata(results: list[CheckResult]) -> None:
    """Validate Unity package metadata."""
    add(results, "runtime package folder exists", PACKAGE.is_dir(), rel(PACKAGE))
    data = load_json(PACKAGE / "package.json", results, "package.json parses")
    if not data:
        return

    expected = {
        "name": PACKAGE_NAME,
        "version": "0.1.0-preview.1",
        "displayName": "Unity2Foxglove ROS2 For Unity Runtime - Humble Win64",
        "license": "Apache-2.0",
        "unity": "6000.0",
        "description": "Optional Humble Windows x64 runtime package for Unity2Foxglove ROS2 For Unity integration.",
    }
    for key, value in expected.items():
        add(results, f"package {key}", data.get(key) == value, f"expected {value!r}, got {data.get(key)!r}")

    add(results, "package has no dependencies", "dependencies" not in data, f"dependencies={data.get('dependencies')!r}")
    keywords = data.get("keywords", [])
    add(
        results,
        "package keywords",
        isinstance(keywords, list) and {"ros2", "ros2-for-unity", "humble", "win64"}.issubset(set(keywords)),
        f"keywords={keywords!r}",
    )


def check_required_files(results: list[CheckResult]) -> None:
    """Validate files required by the runtime package contract."""
    required = [
        PACKAGE / "README.md",
        PACKAGE / "LICENSE",
        PACKAGE / "THIRD_PARTY_NOTICES.md",
        MANIFEST,
        INVENTORY,
        RUNTIME_ROOT / "metadata_ros2_for_unity.xml",
        RUNTIME_ROOT / "metadata_ros2cs.xml",
        RUNTIME_ROOT / "Plugins" / "metadata_ros2cs.xml",
        PLUGIN_ROOT / "metadata_ros2cs.xml",
        RUNTIME_ROOT / "StreamingAssets" / "Ros2ForUnity" / "share" / "ament_index" / "resource_index" / "packages" / "rmw",
        RUNTIME_ROOT / "Scripts" / "ROS2ForUnity.cs",
        RUNTIME_ROOT / "Scripts" / "ROS2UnityComponent.cs",
        RUNTIME_ROOT / "Scripts" / "ROS2UnityCore.cs",
        RUNTIME_ROOT / "Scripts" / "Unity2Foxglove.Ros2ForUnity.Runtime.HumbleWin64.asmdef",
        RUNTIME_ROOT / "Plugins" / "ros2cs_core.dll",
        RUNTIME_ROOT / "Plugins" / "ros2cs_common.dll",
        RUNTIME_ROOT / "Plugins" / "std_msgs_assembly.dll",
        RUNTIME_ROOT / "Plugins" / "rosgraph_msgs_assembly.dll",
    ]
    for path in required:
        add(results, f"required file: {path.name}", path.exists(), rel(path))


def check_ros2cs_metadata_portability(results: list[CheckResult]) -> None:
    """Require package-relative roots in both shipped plugin inventories."""
    for path in (
        RUNTIME_ROOT / "Plugins" / "metadata_ros2cs.xml",
        PLUGIN_ROOT / "metadata_ros2cs.xml",
    ):
        text = read_optional_text(path)
        try:
            root = ElementTree.fromstring(text)
            plugins = root.find("plugins") if root.tag == "ros2cs" else None
            plugin_root = plugins.get("root") if plugins is not None else None
        except ElementTree.ParseError:
            plugin_root = None
        add(
            results,
            f"{rel(path)} uses portable plugin root",
            plugin_root == ".",
            f"root={plugin_root!r}",
        )


def check_message_package_floor(results: list[CheckResult]) -> None:
    """Validate generated message packages required by packaged R2FU scripts."""
    native_suffixes = (
        "__rosidl_generator_c.dll",
        "__rosidl_typesupport_c.dll",
        "__rosidl_typesupport_cpp.dll",
        "__rosidl_typesupport_fastrtps_c.dll",
        "__rosidl_typesupport_fastrtps_cpp.dll",
        "__rosidl_typesupport_introspection_c.dll",
        "__rosidl_typesupport_introspection_cpp.dll",
    )
    for package in BASELINE_MESSAGE_PACKAGES:
        managed = RUNTIME_ROOT / "Plugins" / f"{package}_assembly.dll"
        add(results, f"baseline message assembly present: {package}", managed.exists(), rel(managed))
        missing_native = [suffix for suffix in native_suffixes if not (PLUGIN_ROOT / f"{package}{suffix}").exists()]
        add(
            results,
            f"baseline native message support present: {package}",
            not missing_native,
            f"missing={missing_native!r}",
        )

    for family in HUMBLE_HANDOFF_FAMILIES:
        managed = RUNTIME_ROOT / "Plugins" / f"{family}_assembly.dll"
        add(results, f"Humble handoff message assembly present: {family}", managed.exists(), rel(managed))

    for dll in HUMBLE_HANDOFF_DLLS:
        path = PLUGIN_ROOT / dll
        add(results, f"Humble handoff DLL present: {dll}", path.exists(), rel(path))
