"""Unity asset, metadata, and required-file checks."""

from __future__ import annotations

from .foundation import *
def check_required_files(results: list[CheckResult]) -> None:
    """Validate release-critical files that must be present in the package."""
    required = [
        PACKAGE / "README.md",
        PACKAGE / "LICENSE",
        PACKAGE / "Runtime" / "Unity.FoxgloveSDK.asmdef",
        PACKAGE / "Editor" / "Unity.FoxgloveSDK.Editor.asmdef",
        PACKAGE / "Editor" / "SourceGenerators" / "src" / "Unity.FoxgloveSDK.SourceGenerators.asmdef",
        PACKAGE / "Runtime" / "Schemas" / "Proto" / "Unity.FoxgloveSDK.Proto.asmdef",
        PACKAGE / "Runtime" / "link.xml",
        PACKAGE / "Plugins" / "Google.Protobuf" / "Google.Protobuf.dll",
    ]
    for path in required:
        add(results, f"required file: {path.name}", path.exists(), rel(path))
        meta = Path(str(path) + ".meta")
        add(
            results,
            f"required meta: {meta.name}",
            meta.exists(),
            rel(meta),
        )


def check_sample_meta(
    results: list[CheckResult],
    samples_files: list[Path] | None = None,
    additional_files: list[Path] | None = None,
) -> None:
    """Ensure ordinary Unity assets have valid, unique .meta identities."""
    samples_files = samples_files if samples_files is not None else list(iter_files(SAMPLES))
    files = list(samples_files) + list(additional_files or [])
    missing: list[str] = []
    malformed: list[str] = []
    duplicate: list[str] = []
    guids: dict[str, str] = {}
    for path in files:
        if path.suffix == ".meta" or path.name == "README.md":
            continue
        if path.suffix.lower() not in UNITY_META_EXTENSIONS:
            continue
        meta = Path(str(path) + ".meta")
        if not meta.is_file():
            missing.append(rel(path))
            continue
        try:
            text = meta.read_text(encoding="utf-8")
        except OSError:
            malformed.append(rel(meta))
            continue
        match = META_GUID_RE.search(text)
        if match is None:
            malformed.append(rel(meta))
            continue
        guid = match.group(1).lower()
        previous = guids.get(guid)
        if previous is not None:
            duplicate.append(f"{rel(meta)} duplicates {previous}")
        else:
            guids[guid] = rel(meta)
    add(
        results,
        "sample Unity asset .meta files",
        not missing and not malformed and not duplicate,
        "; ".join(
            missing[:MAX_REPORTED_MISSING_META]
            + malformed[:MAX_REPORTED_MISSING_META]
            + duplicate[:MAX_REPORTED_MISSING_META]
        )
        if missing or malformed or duplicate
        else f"{len(guids)} ordinary asset metas have unique valid GUIDs",
    )


def check_package_asset_meta(
    results: list[CheckResult], package: Path, label: str
) -> None:
    """Ensure every Unity asset and folder in a first-party package has valid metadata."""
    if not package.is_dir():
        add(results, f"{label} Unity asset and folder .meta files", False, rel(package))
        return
    files, folders = _iter_package_assets(package)
    missing: list[str] = []
    malformed: list[str] = []
    orphan = [
        rel(meta)
        for meta in _iter_scoped_meta_files(package)
        if not Path(str(meta)[:-len(".meta")]).exists()
    ]
    for asset in files:
        meta = Path(str(asset) + ".meta")
        if not meta.is_file():
            missing.append(rel(meta))
            continue
        try:
            text = meta.read_text(encoding="utf-8", errors="replace")
        except (OSError, UnicodeError):
            malformed.append(rel(meta))
            continue
        if META_GUID_RE.search(text) is None or (
            asset.suffix.casefold() == ".cs" and not _csharp_meta_importer_is_valid(text)
        ):
            malformed.append(rel(meta))
    for folder in folders:
        meta = Path(str(folder) + ".meta")
        if not meta.is_file():
            missing.append(rel(meta))
            continue
        try:
            text = meta.read_text(encoding="utf-8", errors="replace")
        except (OSError, UnicodeError):
            malformed.append(rel(meta))
            continue
        if META_GUID_RE.search(text) is None or META_FOLDER_ASSET_RE.search(text) is None:
            malformed.append(rel(meta))
    offenders = (
        missing[:MAX_REPORTED_MISSING_META]
        + malformed[:MAX_REPORTED_MISSING_META]
        + orphan[:MAX_REPORTED_MISSING_META]
    )
    add(
        results,
        f"{label} Unity asset and folder .meta files",
        not offenders,
        "; ".join(offenders) if offenders else f"{len(files)} assets and {len(folders)} folders have metadata",
    )


def check_packages_meta_guid_uniqueness(
    results: list[CheckResult], packages_root: Path | None = None
) -> None:
    """Ensure first-party package Unity GUIDs are valid and unique across packages."""
    roots = (
        tuple(sorted((p for p in (packages_root or ROOT / "Packages").iterdir() if p.is_dir()), key=rel))
        if packages_root is not None
        else UNITY_SOURCE_PACKAGES
    )
    guids: dict[str, str] = {}
    offenders: list[str] = []
    for package in roots:
        optional_missing = (
            SDK_OPTIONAL_MISSING_META_PATHS
            if package.resolve() == PACKAGE.resolve()
            else frozenset()
        )
        for meta in _iter_scoped_meta_files(package):
            asset = Path(str(meta)[:-len(".meta")])
            relative_meta = meta.relative_to(package).as_posix()
            if not asset.exists():
                if relative_meta not in optional_missing:
                    offenders.append(f"{rel(meta)} is an orphan metadata sidecar")
                    continue
            text = meta.read_text(encoding="utf-8", errors="replace")
            match = META_GUID_RE.search(text)
            if match is None:
                offenders.append(f"{rel(meta)} has no valid GUID")
                continue
            if (
                not asset.exists()
                and relative_meta in optional_missing
                and META_FOLDER_ASSET_RE.search(text) is None
            ):
                offenders.append(f"{rel(meta)} is missing folderAsset: yes")
                continue
            guid = match.group(1).lower()
            previous = guids.get(guid)
            if previous is not None:
                offenders.append(f"{rel(meta)} duplicates {previous}")
            else:
                guids[guid] = rel(meta)
    add(
        results,
        "First-party package Unity .meta GUIDs",
        not offenders,
        "; ".join(offenders[:MAX_REPORTED_MISSING_META])
        if offenders
        else f"{len(guids)} Unity-tracked package metas have unique valid GUIDs",
    )


def check_sdk_script_meta(
    results: list[CheckResult],
    sdk_files: list[Path] | None = None,
    known_guids: dict[str, str] | None = None,
) -> None:
    """Ensure every C# source shipped in the SDK package has a Unity meta sidecar."""
    files = sdk_files if sdk_files is not None else list(PACKAGE.rglob("*.cs"))
    missing: list[str] = []
    malformed: list[str] = []
    local_guids: dict[str, str] = {}
    guids = known_guids if known_guids is not None else local_guids
    for path in files:
        if not path.is_file() or path.suffix.casefold() != ".cs":
            continue
        meta = Path(str(path) + ".meta")
        if not meta.is_file():
            missing.append(rel(path))
            continue
        try:
            text = meta.read_text(encoding="utf-8")
        except OSError:
            malformed.append(rel(meta))
            continue
        match = META_GUID_RE.search(text)
        if match is None or not _csharp_meta_importer_is_valid(text):
            malformed.append(rel(meta))
            continue
        guid = match.group(1).lower()
        if guid in guids:
            malformed.append(f"{rel(meta)} duplicates {guids[guid]}")
        else:
            guids[guid] = rel(meta)
        local_guids.setdefault(guid, rel(meta))

    offenders = missing[:MAX_REPORTED_MISSING_META] + malformed[:MAX_REPORTED_MISSING_META]
    add(
        results,
        "SDK C# source .meta files",
        not missing and not malformed,
        "; ".join(offenders) if offenders else f"{len(local_guids)} SDK C# sources have valid unique metas",
    )


def check_sdk_non_csharp_asset_meta(
    results: list[CheckResult],
    sdk_files: list[Path] | None = None,
) -> None:
    """Ensure non-C# Unity-tracked SDK assets have valid metadata sidecars."""
    files = sdk_files
    if files is None:
        files = [
            path
            for path in _iter_package_assets(PACKAGE)[0]
            if path.suffix.casefold() != ".cs"
            and not path_is_relative_to(path, SAMPLES)
        ]
    missing: list[str] = []
    malformed: list[str] = []
    for asset in files:
        if not asset.is_file() or asset.suffix.casefold() == ".cs":
            continue
        meta = Path(str(asset) + ".meta")
        if not meta.is_file():
            missing.append(rel(meta))
            continue
        try:
            text = meta.read_text(encoding="utf-8")
        except (OSError, UnicodeError):
            malformed.append(rel(meta))
            continue
        if META_GUID_RE.search(text) is None:
            malformed.append(rel(meta))
    offenders = missing[:MAX_REPORTED_MISSING_META] + malformed[:MAX_REPORTED_MISSING_META]
    add(
        results,
        "SDK non-C# Unity asset .meta files",
        not offenders,
        "; ".join(offenders)
        if offenders
        else f"{len(files)} non-C# SDK assets have valid metas",
    )


def check_sdk_decomposed_folder_meta(
    results: list[CheckResult],
    root: Path | None = None,
    known_guids: dict[str, str] | None = None,
) -> None:
    """Ensure decomposed SDK test folders have stable Unity folder identities."""
    if root is not None:
        decomposed_roots = [root]
    else:
        decomposed_root = SDK_DECOMPOSED_TESTS
        if not decomposed_root.is_dir():
            add(
                results,
                "SDK decomposed test folder .meta files",
                False,
                f"Required directory missing: {rel(decomposed_root)}",
            )
            return
        unit_root = PACKAGE / "Tests" / "Unit"
        extra_roots = (
            PACKAGE / "Runtime" / "Schemas" / "Proto" / "Video" / "Decomposed",
            PACKAGE / "Tests" / "Performance" / "Decomposed",
        )
        decomposed_roots = [decomposed_root]
        decomposed_roots.extend(
            sorted(
                {
                    path
                    for path in unit_root.rglob("Decomposed")
                    if path.is_dir() and path != decomposed_root
                } | {path for path in extra_roots if path.is_dir()},
                key=rel,
            )
        )

    if any(not decomposed_root.is_dir() for decomposed_root in decomposed_roots):
        missing_root = next(
            decomposed_root
            for decomposed_root in decomposed_roots
            if not decomposed_root.is_dir()
        )
        add(
            results,
            "SDK decomposed test folder .meta files",
            False,
            f"Required directory missing: {rel(missing_root)}",
        )
        return

    folders: list[Path] = []
    seen_folders: set[Path] = set()
    for decomposed_root in decomposed_roots:
        candidates = [decomposed_root]
        candidates.extend(
            sorted(
                (path for path in decomposed_root.rglob("*") if path.is_dir()),
                key=rel,
            )
        )
        for folder in candidates:
            if folder not in seen_folders:
                seen_folders.add(folder)
                folders.append(folder)
    folders.sort(key=rel)

    missing: list[str] = []
    malformed: list[str] = []
    duplicate: list[str] = []
    local_guids: dict[str, str] = {}
    guids = known_guids if known_guids is not None else local_guids
    for folder in folders:
        meta = Path(str(folder) + ".meta")
        if not meta.is_file():
            missing.append(rel(meta))
            continue
        try:
            meta_text = meta.read_text(encoding="utf-8")
        except OSError:
            malformed.append(rel(meta))
            continue
        guid_match = META_GUID_RE.search(meta_text)
        if guid_match is None or META_FOLDER_ASSET_RE.search(meta_text) is None:
            malformed.append(rel(meta))
            continue
        guid = guid_match.group(1).lower()
        previous = guids.get(guid)
        if previous is not None:
            duplicate.append(f"{rel(meta)} duplicates {previous}")
        else:
            guids[guid] = rel(meta)
        local_guids.setdefault(guid, rel(meta))

    offenders = (
        missing[:MAX_REPORTED_MISSING_META]
        + malformed[:MAX_REPORTED_MISSING_META]
        + duplicate[:MAX_REPORTED_MISSING_META]
    )
    add(
        results,
        "SDK decomposed test folder .meta files",
        not offenders,
        "; ".join(offenders)
        if offenders
        else f"{len(local_guids)} decomposed test folders have valid unique folder metas",
    )


def _collect_meta_guid_offenders(
    package: Path,
    optional_missing_meta_paths: frozenset[str] = frozenset(),
) -> tuple[dict[str, str], list[str], list[str], list[str]]:
    """Collect malformed and duplicate GUID references under a package."""
    guids: dict[str, str] = {}
    malformed: list[str] = []
    duplicate: list[str] = []
    orphan: list[str] = []
    for meta in sorted(package.rglob("*.meta"), key=rel):
        if not meta.is_file():
            continue
        asset = Path(str(meta)[:-len(".meta")])
        relative_meta = meta.relative_to(package).as_posix()
        if not asset.exists():
            if relative_meta not in optional_missing_meta_paths:
                orphan.append(rel(meta))
                continue
        if (
            asset.is_file()
            and asset.suffix.casefold() in NON_UNITY_BUILD_META_SUFFIXES
        ):
            continue
        try:
            text = meta.read_text(encoding="utf-8")
        except (OSError, UnicodeError):
            malformed.append(rel(meta))
            continue
        match = META_GUID_RE.search(text)
        if match is None:
            malformed.append(rel(meta))
            continue
        if (
            not asset.exists()
            and relative_meta in optional_missing_meta_paths
            and META_FOLDER_ASSET_RE.search(text) is None
        ):
            malformed.append(rel(meta))
            continue
        guid = match.group(1).lower()
        previous = guids.get(guid)
        if previous is not None:
            duplicate.append(f"{rel(meta)} duplicates {previous}")
        else:
            guids[guid] = rel(meta)
    return guids, malformed, duplicate, orphan


def check_package_meta_guid_uniqueness(
    results: list[CheckResult],
    root: Path,
    check_name: str,
    asset_label: str,
    optional_missing_meta_paths: frozenset[str] = frozenset(),
) -> None:
    """Ensure package Unity asset and folder metas have unique valid GUIDs."""
    package = root
    if not package.is_dir():
        add(
            results,
            check_name,
            False,
            f"Required directory missing: {rel(package)}",
        )
        return

    guids, malformed, duplicate, orphan = _collect_meta_guid_offenders(
        package, optional_missing_meta_paths
    )

    offenders = (
        orphan[:MAX_REPORTED_MISSING_META]
        + malformed[:MAX_REPORTED_MISSING_META]
        + duplicate[:MAX_REPORTED_MISSING_META]
    )
    add(
        results,
        check_name,
        not offenders,
        "; ".join(offenders)
        if offenders
        else f"{len(guids)} {asset_label} metas have unique valid GUIDs",
    )


def check_sdk_meta_guid_uniqueness(
    results: list[CheckResult],
    root: Path | None = None,
) -> None:
    """Ensure Unity-tracked SDK assets and folders have unique GUIDs."""
    check_package_meta_guid_uniqueness(
        results,
        root if root is not None else PACKAGE,
        "SDK Unity asset and folder .meta GUIDs",
        "Unity-tracked SDK",
        SDK_OPTIONAL_MISSING_META_PATHS,
    )


def check_ros2_bridge_meta(results: list[CheckResult]) -> None:
    """Ensure Bridge Unity assets and tracked folders have complete metadata."""
    package = ROS2_BRIDGE_PACKAGE
    if not package.is_dir():
        add(
            results,
            "ROS2 Bridge Unity asset and folder .meta files",
            False,
            f"Required directory missing: {rel(package)}",
        )
        check_package_meta_guid_uniqueness(
            results,
            package,
            "ROS2 Bridge Unity asset and folder .meta GUIDs",
            "Unity-tracked ROS2 Bridge",
        )
        return

    missing: list[str] = []
    malformed_folders: list[str] = []
    malformed_scripts: list[str] = []
    for asset in iter_bridge_asset_files(package):
        meta = Path(str(asset) + ".meta")
        if not meta.is_file():
            missing.append(rel(meta))
            continue
        if asset.suffix.casefold() == ".cs":
            try:
                text = meta.read_text(encoding="utf-8")
            except (OSError, UnicodeError):
                malformed_scripts.append(rel(meta))
            else:
                if not _csharp_meta_importer_is_valid(text):
                    malformed_scripts.append(rel(meta))

    for folder in iter_bridge_asset_directories(package):
        meta = Path(str(folder) + ".meta")
        if not meta.is_file():
            missing.append(rel(meta))
            continue
        try:
            text = meta.read_text(encoding="utf-8")
        except (OSError, UnicodeError):
            malformed_folders.append(rel(meta))
            continue
        if META_FOLDER_ASSET_RE.search(text) is None:
            malformed_folders.append(rel(meta))

    offenders = (
        missing[:MAX_REPORTED_MISSING_META]
        + malformed_folders[:MAX_REPORTED_MISSING_META]
        + malformed_scripts[:MAX_REPORTED_MISSING_META]
    )
    add(
        results,
        "ROS2 Bridge Unity asset and folder .meta files",
        not offenders,
        "; ".join(offenders)
        if offenders
        else f"{len(iter_bridge_asset_files(package))} assets and "
        f"{len(iter_bridge_asset_directories(package))} folders have metadata",
    )
    check_package_meta_guid_uniqueness(
        results,
        package,
        "ROS2 Bridge Unity asset and folder .meta GUIDs",
        "Unity-tracked ROS2 Bridge",
    )


def check_package_meta_yaml(
    results: list[CheckResult],
    package_roots: Iterable[Path] | None = None,
) -> None:
    """Ensure tracked Unity metadata keeps the basic YAML identity shape."""
    roots = tuple(
        package_roots
        or (PACKAGE, ROS2_FOR_UNITY_PACKAGE, ROS2_BRIDGE_PACKAGE, REMOTE_GATEWAY_PACKAGE)
    )
    malformed: list[str] = []
    for root in roots:
        if not root.is_dir():
            continue
        for meta in root.rglob("*.meta"):
            try:
                text = meta.read_text(encoding="utf-8")
            except OSError:
                malformed.append(rel(meta))
                continue
            if "`" in text or not re.search(r"(?m)^fileFormatVersion:\s*2\s*$", text):
                malformed.append(rel(meta))
                continue
            if META_GUID_RE.search(text) is None:
                malformed.append(rel(meta))
    add(
        results,
        "Unity package .meta YAML shape",
        not malformed,
        "; ".join(malformed[:MAX_REPORTED_MISSING_META])
        if malformed
        else f"{sum(1 for root in roots if root.is_dir() for _ in root.rglob('*.meta'))} metadata files have valid YAML identity headers",
    )


def check_sample_boundaries(results: list[CheckResult]) -> None:
    """Verify Basic and FullDemo sample boundaries remain intentional."""
    basic = SAMPLES / "BasicVisualization"
    full = SAMPLES / "FullDemoVisualization"

    forbidden_basic = [
        basic / "Scripts",
        basic / "Settings",
        basic / "InputSystem_Actions.inputactions",
        basic / "FoxgloveFullLayout.json",
        basic / "Scenes" / "FullDemoVisualization.unity",
    ]
    leaks = [rel(p) for p in forbidden_basic if p.exists()]
    add(results, "Basic sample remains minimal", not leaks, "; ".join(leaks) if leaks else "no FullDemo-only files")

    required_full = [
        full / "FoxgloveFullLayout.json",
        full / "FoxgloveFullLayout.json.meta",
        full / "Scenes" / "FullDemoVisualization.unity",
        full / "Scripts" / "FoxgloveDemoSetup.cs",
        full / "Scripts" / "MouseDragCube.cs",
        full / "Scripts" / "TestLog.cs",
        full / "Settings" / "DefaultVolumeProfile.asset",
        full / "Settings" / "UniversalRenderPipelineGlobalSettings.asset",
    ]
    missing = [rel(p) for p in required_full if not p.exists()]
    add(results, "FullDemo required files", not missing, "; ".join(missing) if missing else "all required files present")

    forbidden_full = [
        full / "InputSystem_Actions.inputactions",
        full / "InputSystem_Actions.inputactions.meta",
    ]
    conflicts = [rel(p) for p in forbidden_full if p.exists()]
    add(results, "FullDemo avoids project-level input action assets", not conflicts, "; ".join(conflicts) if conflicts else "no InputSystem_Actions asset")



__all__ = [
    'check_required_files',
    'check_sample_boundaries',
    'check_sample_meta',
    'check_sdk_script_meta',
    'check_sdk_non_csharp_asset_meta',
    'check_package_meta_yaml',
    'check_package_asset_meta',
    'check_packages_meta_guid_uniqueness',
    'check_sdk_decomposed_folder_meta',
    'check_package_meta_guid_uniqueness',
    'check_sdk_meta_guid_uniqueness',
    'check_ros2_bridge_meta',
]
