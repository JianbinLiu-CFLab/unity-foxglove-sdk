from __future__ import annotations
from .preflight_process_execution import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase184_profile_acceptance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
def _current_unity_runtime_selection_evidence(
    repository: pathlib.Path,
    distro: str,
    rmw: str,
) -> dict[str, str] | None:
    """Prove an exact default-RMW selection before skipping Package Manager."""

    runtime_package = (
        f"dev.unity2foxglove.ros2forunity.runtime.{distro}.win64"
    )
    typesupport_package = (
        "dev.unity2foxglove.foxrun.ros2.interfaces.typesupport."
        f"{distro}.win64"
    )
    runtime_reference = f"file:../../Packages/{runtime_package}"
    typesupport_reference = f"file:../../Packages/{typesupport_package}"
    root = pathlib.Path(repository)
    project = root / "Unity2Foxglove"
    runtime_root = root / "Packages" / runtime_package
    typesupport_root = root / "Packages" / typesupport_package

    def read_mapping(path: pathlib.Path) -> Mapping[str, Any] | None:
        """Read mapping."""

        document = json.loads(path.read_text(encoding="utf-8"))
        return document if isinstance(document, Mapping) else None

    def selected_package_ids(
        dependencies: Mapping[str, Any],
        prefix: str,
    ) -> tuple[str, ...]:
        """Handle the selected package ids step."""

        return tuple(
            sorted(
                key
                for key in dependencies
                if isinstance(key, str) and key.startswith(prefix)
            )
        )

    try:
        manifest = read_mapping(project / "Packages" / "manifest.json")
        lock = read_mapping(project / "Packages" / "packages-lock.json")
        runtime_manifest = read_mapping(
            runtime_root / "RuntimeSupport" / "runtime-manifest.json"
        )
        runtime_identity = read_mapping(runtime_root / "package.json")
        typesupport_identity = read_mapping(typesupport_root / "package.json")
        project_settings = (
            project / "ProjectSettings" / "ProjectSettings.asset"
        ).read_text(encoding="utf-8")
        if any(
            document is None
            for document in (
                manifest,
                lock,
                runtime_manifest,
                runtime_identity,
                typesupport_identity,
            )
        ):
            return None

        manifest_dependencies = manifest.get("dependencies")
        lock_dependencies = lock.get("dependencies")
        if not isinstance(manifest_dependencies, Mapping) or not isinstance(
            lock_dependencies,
            Mapping,
        ):
            return None
        if selected_package_ids(
            manifest_dependencies,
            _UNITY_RUNTIME_PACKAGE_PREFIX,
        ) != (runtime_package,):
            return None
        if selected_package_ids(
            manifest_dependencies,
            _UNITY_TYPESUPPORT_PACKAGE_PREFIX,
        ) != (typesupport_package,):
            return None
        if selected_package_ids(
            lock_dependencies,
            _UNITY_RUNTIME_PACKAGE_PREFIX,
        ) != (runtime_package,):
            return None
        if selected_package_ids(
            lock_dependencies,
            _UNITY_TYPESUPPORT_PACKAGE_PREFIX,
        ) != (typesupport_package,):
            return None
        if manifest_dependencies.get(runtime_package) != runtime_reference:
            return None
        if (
            manifest_dependencies.get(typesupport_package)
            != typesupport_reference
        ):
            return None

        runtime_lock = lock_dependencies.get(runtime_package)
        typesupport_lock = lock_dependencies.get(typesupport_package)
        if not isinstance(runtime_lock, Mapping) or not isinstance(
            typesupport_lock,
            Mapping,
        ):
            return None
        for entry, reference in (
            (runtime_lock, runtime_reference),
            (typesupport_lock, typesupport_reference),
        ):
            if (
                entry.get("version") != reference
                or entry.get("source") != "local"
                or type(entry.get("depth")) is not int
                or entry.get("depth") != 0
            ):
                return None

        runtime_version = runtime_identity.get("version")
        typesupport_dependencies = typesupport_identity.get("dependencies")
        lock_typesupport_dependencies = typesupport_lock.get("dependencies")
        if (
            runtime_identity.get("name") != runtime_package
            or not isinstance(runtime_version, str)
            or not runtime_version
            or typesupport_identity.get("name") != typesupport_package
            or typesupport_identity.get(
                "unity2foxgloveFoxRunCustomTypesupportAddOn"
            )
            is not True
            or not isinstance(typesupport_dependencies, Mapping)
            or typesupport_dependencies.get(runtime_package) != runtime_version
            or not isinstance(lock_typesupport_dependencies, Mapping)
            or lock_typesupport_dependencies.get(runtime_package)
            != runtime_version
        ):
            return None

        default_rmw = runtime_manifest.get(
            "defaultRmwImplementation",
            runtime_manifest.get("rmwImplementation"),
        )
        if (
            runtime_manifest.get("packageName") != runtime_package
            or runtime_manifest.get("rosDistro") != distro
            or runtime_manifest.get("platform") != "win64"
            or runtime_manifest.get("architecture") != "x86_64"
            or default_rmw != rmw
        ):
            return None

        defines_header = re.search(
            r"(?m)^(?P<indent>[ \t]*)scriptingDefineSymbols:[ \t]*$",
            project_settings,
        )
        if defines_header is None:
            return None
        defines_indent = len(defines_header.group("indent"))
        standalone_value: str | None = None
        for line in project_settings[defines_header.end() :].splitlines():
            if not line.strip():
                continue
            line_indent = len(line) - len(line.lstrip(" \t"))
            if line_indent <= defines_indent:
                break
            standalone_match = re.match(
                r"^[ \t]*Standalone:[ \t]*(.*)$",
                line,
            )
            if standalone_match is not None:
                standalone_value = standalone_match.group(1)
                break
        if standalone_value is None:
            return None
        standalone_defines = {
            value.strip()
            for value in standalone_value.split(";")
            if value.strip()
        }
        if not {
            _UNITY_RUNTIME_DEFINE,
            _UNITY_TYPESUPPORT_DEFINE,
        }.issubset(standalone_defines):
            return None
    except (OSError, UnicodeError, json.JSONDecodeError, TypeError, ValueError):
        return None

    return {
        "mode": "reused",
        "runtimePackage": runtime_package,
        "typesupportPackage": typesupport_package,
        "rosDistro": distro,
        "rmwImplementation": rmw,
    }


def _bridge_source_root(repository: pathlib.Path) -> pathlib.Path:
    """Handle the bridge source root step."""

    return (
        pathlib.Path(repository)
        / "Tools"
        / "ros2_bridge"
        / "unity2foxglove_ros2_bridge"
    )


def _sha256_file(path: pathlib.Path) -> str:
    """Handle the SHA-256 file step."""

    digest = hashlib.sha256()
    with pathlib.Path(path).open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def _bridge_source_digest(repository: pathlib.Path) -> str:
    """Hash every staged Bridge source path and byte without build noise."""

    source = _bridge_source_root(repository)
    if not (source / "package.xml").is_file():
        raise AcceptanceFailure("FAIL_BUILD", "The maintained Bridge source is absent.")
    digest = hashlib.sha256()
    files: list[pathlib.Path] = []
    for path in source.rglob("*"):
        relative = path.relative_to(source)
        if any(part in _BRIDGE_SOURCE_IGNORES for part in relative.parts):
            continue
        if path.is_symlink():
            raise AcceptanceFailure(
                "FAIL_BUILD",
                "The maintained Bridge source cannot contain symbolic links.",
            )
        if path.is_file():
            files.append(path)
    for path in sorted(files, key=lambda item: item.relative_to(source).as_posix()):
        relative = path.relative_to(source).as_posix().encode("utf-8")
        digest.update(len(relative).to_bytes(4, "little"))
        digest.update(relative)
        digest.update(path.stat().st_size.to_bytes(8, "little"))
        with path.open("rb") as stream:
            for block in iter(lambda: stream.read(1024 * 1024), b""):
                digest.update(block)
    return digest.hexdigest()


def _bridge_build_path_identity(path: pathlib.Path) -> dict[str, object]:
    """Handle the bridge build path identity step."""

    candidate = pathlib.Path(path).resolve(strict=False)
    try:
        stat = candidate.stat()
    except OSError:
        return {"path": str(candidate), "available": False}
    return {
        "path": str(candidate),
        "available": True,
        "size": int(stat.st_size),
        "modifiedNs": int(stat.st_mtime_ns),
    }


def bridge_build_cache_key(
    repository: pathlib.Path,
    profile: str,
    distro: str,
    rmw: str,
    toolchain,
    build_command: Sequence[str],
    build_environment: Mapping[str, str],
) -> str:
    """Bind a reusable Windows Bridge build to all material local inputs."""

    if (
        profile not in protocol.PROFILE_CONTRACTS
        or not distro
        or not rmw
        or not build_command
    ):
        raise AcceptanceFailure("FAIL_BUILD", "Bridge cache identity is incomplete.")
    environment_keys = (
        "VCToolsVersion",
        "VisualStudioVersion",
        "WindowsSDKVersion",
        "VSCMD_ARG_TGT_ARCH",
        "OPENSSL_ROOT_DIR",
        "nlohmann_json_DIR",
        "tinyxml2_DIR",
    )
    payload = {
        "format": _BRIDGE_CACHE_FORMAT,
        "sourceSha256": _bridge_source_digest(repository),
        "profile": profile,
        "distro": distro,
        "rmw": rmw,
        "buildCommand": list(build_command),
        "environment": {
            key: str(build_environment.get(key, ""))
            for key in environment_keys
        },
        "toolchain": {
            "ros2Root": _bridge_build_path_identity(toolchain.ros2_root),
            "ros2LocalSetup": _bridge_build_path_identity(
                pathlib.Path(toolchain.ros2_root) / "local_setup.bat"
            ),
            "python": _bridge_build_path_identity(toolchain.python_executable),
            "colcon": _bridge_build_path_identity(toolchain.colcon_executable),
        },
    }
    encoded = json.dumps(
        payload,
        sort_keys=True,
        separators=(",", ":"),
        ensure_ascii=True,
    ).encode("utf-8")
    return hashlib.sha256(encoded).hexdigest()


def _bridge_cache_owner(profile: str) -> dict[str, object]:
    """Handle the bridge cache owner step."""

    return {
        "schemaVersion": _BRIDGE_CACHE_FORMAT,
        "owner": _BRIDGE_CACHE_OWNER,
        "profile": profile,
    }


def _read_bridge_cache_json(path: pathlib.Path) -> Mapping[str, object] | None:
    """Read bridge cache JSON."""

    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError):
        return None
    return value if isinstance(value, Mapping) else None


def _bridge_cache_is_owned(overlay: pathlib.Path, profile: str) -> bool:
    """Handle the bridge cache is owned step."""

    marker = _read_bridge_cache_json(overlay / _BRIDGE_CACHE_OWNERSHIP_NAME)
    return marker == _bridge_cache_owner(profile)


def _bridge_cache_executable(overlay: pathlib.Path) -> pathlib.Path:
    """Handle the bridge cache executable step."""

    return (
        overlay
        / "install"
        / "lib"
        / _BRIDGE_PACKAGE_NAME
        / f"{_BRIDGE_PACKAGE_NAME}.exe"
    )


def _bridge_cache_has_outputs(overlay: pathlib.Path) -> bool:
    """Handle the bridge cache has outputs step."""

    install = overlay / "install"
    return (
        _bridge_cache_executable(overlay).is_file()
        and (install / "local_setup.bat").is_file()
        and (install / "share" / _BRIDGE_PACKAGE_NAME / "package.xml").is_file()
    )


def _bridge_cache_matches(
    overlay: pathlib.Path,
    profile: str,
    cache_key: str,
) -> bool:
    """Handle the bridge cache matches step."""

    if not _bridge_cache_is_owned(overlay, profile) or not _bridge_cache_has_outputs(
        overlay
    ):
        return False
    manifest = _read_bridge_cache_json(overlay / _BRIDGE_CACHE_MANIFEST_NAME)
    if (
        manifest is None
        or set(manifest)
        != {
            "schemaVersion",
            "cacheKey",
            "profile",
            "executableSha256",
        }
        or manifest.get("schemaVersion") != _BRIDGE_CACHE_FORMAT
        or manifest.get("cacheKey") != cache_key
        or manifest.get("profile") != profile
    ):
        return False
    try:
        return manifest.get("executableSha256") == _sha256_file(
            _bridge_cache_executable(overlay)
        )
    except OSError:
        return False


def prepare_bridge_build_workspace(
    cache_root: pathlib.Path,
    profile: str,
    cache_key: str,
) -> tuple[pathlib.Path, bool]:
    """Reuse or replace only an exactly owned profile-stable Bridge cache."""

    if profile not in protocol.PROFILE_CONTRACTS or not re.fullmatch(
        r"[0-9a-f]{64}",
        cache_key,
    ):
        raise AcceptanceFailure("FAIL_BUILD", "Bridge cache key or profile is invalid.")
    root = pathlib.Path(cache_root).resolve(strict=False)
    raw_overlay = pathlib.Path(
        os.path.abspath(os.fspath(root / profile / "bridge-overlay"))
    )
    overlay = raw_overlay.resolve(strict=False)
    if (
        os.path.normcase(os.fspath(raw_overlay))
        != os.path.normcase(os.fspath(overlay))
        or overlay == root
    ):
        raise AcceptanceFailure("FAIL_BUILD", "Bridge cache path is redirected.")
    try:
        overlay.relative_to(root)
    except ValueError as exc:
        raise AcceptanceFailure("FAIL_BUILD", "Bridge cache path escaped its root.") from exc

    if overlay.exists():
        if _bridge_cache_matches(overlay, profile, cache_key):
            return overlay, True
        if not overlay.is_dir() or not _bridge_cache_is_owned(overlay, profile):
            raise AcceptanceFailure(
                "FAIL_BUILD",
                "Refusing to replace an unowned Bridge cache workspace.",
            )
        try:
            shutil.rmtree(overlay)
        except OSError as exc:
            raise AcceptanceFailure(
                "FAIL_BUILD",
                "The stale owned Bridge cache could not be replaced.",
            ) from exc

    try:
        overlay.mkdir(parents=True, exist_ok=False)
        write_private_json_atomic(
            overlay / _BRIDGE_CACHE_OWNERSHIP_NAME,
            _bridge_cache_owner(profile),
        )
    except OSError as exc:
        raise AcceptanceFailure(
            "FAIL_BUILD",
            "The owned Bridge cache workspace could not be created.",
        ) from exc
    return overlay, False


def seal_bridge_build_workspace(
    overlay: pathlib.Path,
    profile: str,
    cache_key: str,
) -> None:
    """Seal a tested Bridge cache with its exact executable digest."""

    candidate = pathlib.Path(overlay)
    if (
        profile not in protocol.PROFILE_CONTRACTS
        or not re.fullmatch(r"[0-9a-f]{64}", cache_key)
        or not _bridge_cache_is_owned(candidate, profile)
        or not _bridge_cache_has_outputs(candidate)
    ):
        raise AcceptanceFailure(
            "FAIL_BUILD",
            "The Bridge cache cannot be sealed without owned tested outputs.",
        )
    write_private_json_atomic(
        candidate / _BRIDGE_CACHE_MANIFEST_NAME,
        {
            "schemaVersion": _BRIDGE_CACHE_FORMAT,
            "cacheKey": cache_key,
            "profile": profile,
            "executableSha256": _sha256_file(
                _bridge_cache_executable(candidate)
            ),
        },
    )




__all__ = [name for name in globals() if not name.startswith("__")]
