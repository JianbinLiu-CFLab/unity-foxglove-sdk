from __future__ import annotations
from .foundation import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase181_custom_ros2_peer.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
def require_matching_unity_readiness(
    ready: protocol.UnityMarker,
    interface_ready: protocol.UnityMarker,
    lock: StaticInterfaceLock,
    distro: str,
    rmw: str,
    expected_token: str | None = None,
) -> str:
    """Return Unity's correlation token only for the selected runtime and static source."""

    token = ready.fields.get("token")
    if not _safe_marker_token(token) or interface_ready.fields.get("token") != token:
        raise PeerFailure("FAIL_READY_TOKEN", "Unity custom-interface readiness did not provide one usable correlation token.")
    if expected_token is not None and token != expected_token:
        raise PeerFailure("FAIL_READY_TOKEN", "Unity Player readiness did not report the helper-owned correlation token.")
    if ready.fields.get("runtime") != distro or ready.fields.get("rmw") != rmw:
        raise PeerFailure("FAIL_RUNTIME_IDENTITY", "Unity readiness did not report the selected ROS2 runtime and RMW.")
    if interface_ready.fields.get("digest") != protocol.digest_prefix(lock.interface_digest):
        raise PeerFailure("FAIL_INTERFACE_DIGEST", "Unity readiness did not report the locked custom interface digest prefix.")
    return token
def worker_phase_deadline(
    run_token: str | None,
    ready_deadline: float,
    apply_deadline: float | None,
) -> float:
    """Use the full readiness window first, then a separate full apply window."""

    if run_token is None:
        return ready_deadline
    if apply_deadline is None:
        raise PeerFailure("FAIL_STATE_TRANSITION", "The custom probe window was not armed after Unity correlation.")
    return apply_deadline
def load_static_interface_lock(static_interface_package: pathlib.Path) -> StaticInterfaceLock:
    """Load the single locked custom envelope identity and fail closed on drift."""

    lock_path = pathlib.Path(static_interface_package) / LOCK_RELATIVE_PATH
    try:
        raw = json.loads(lock_path.read_text(encoding="utf-8"))
    except (OSError, UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise PeerFailure("FAIL_INTERFACE_DIGEST", "The static interface lock is unavailable or malformed.") from exc
    if not isinstance(raw, Mapping):
        raise PeerFailure("FAIL_INTERFACE_DIGEST", "The static interface lock is not an object.")
    digest = raw.get("interfaceDigest")
    package_name = raw.get("rosPackageName")
    revision = raw.get("interfaceRevision")
    contracts = raw.get("contracts")
    if (
        raw.get("unityPackageId") != STATIC_INTERFACE_PACKAGE_ID
        or package_name != ROS_PACKAGE_NAME
        or not isinstance(revision, int)
        or revision <= 0
        or not isinstance(digest, str)
        or _SHA256.fullmatch(digest) is None
        or not isinstance(contracts, list)
        or len(contracts) != 1
        or not isinstance(contracts[0], Mapping)
    ):
        raise PeerFailure("FAIL_INTERFACE_DIGEST", "The static interface identity is incomplete.")
    contract = contracts[0]
    payload_name = contract.get("payloadMessageName")
    envelope_name = contract.get("envelopeMessageName")
    if not isinstance(payload_name, str) or not payload_name or not isinstance(envelope_name, str) or not envelope_name:
        raise PeerFailure("FAIL_INTERFACE_DIGEST", "The static interface message identity is incomplete.")
    if compute_static_source_digest(static_interface_package) != digest:
        raise PeerFailure("FAIL_INTERFACE_DIGEST", "The static interface lock does not match its source package.")
    return StaticInterfaceLock(
        ros_package_name=package_name,
        interface_revision=revision,
        interface_digest=digest,
        payload_message_name=payload_name,
        envelope_message_name=envelope_name,
    )
def _append_digest_frame(digest: "hashlib._Hash", content: bytes) -> None:
    """Append the same public length framing used by the static interface package lock."""

    digest.update(len(content).to_bytes(8, byteorder="big", signed=False))
    digest.update(content)
def compute_static_source_digest(static_interface_package: pathlib.Path) -> str:
    """Compute the exact public static-source digest without loading ROS or Unity."""

    root = pathlib.Path(static_interface_package)
    lock_relative = LOCK_RELATIVE_PATH.as_posix()
    files: list[tuple[str, bytes]] = []
    try:
        candidates = list(root.rglob("*"))
    except OSError as exc:
        raise PeerFailure("FAIL_INTERFACE_DIGEST", "The static interface source cannot be enumerated.") from exc
    for candidate in candidates:
        if not candidate.is_file():
            continue
        relative = candidate.relative_to(root).as_posix()
        if relative == lock_relative or relative.endswith(".meta"):
            continue
        try:
            text = candidate.read_text(encoding="utf-8")
        except (OSError, UnicodeDecodeError) as exc:
            raise PeerFailure("FAIL_INTERFACE_DIGEST", "The static interface source is not valid UTF-8 text.") from exc
        if text.startswith("\ufeff"):
            raise PeerFailure("FAIL_INTERFACE_DIGEST", "The static interface source contains an unsupported byte-order mark.")
        files.append((relative, text.replace("\r\n", "\n").replace("\r", "\n").encode("utf-8")))
    if not files or len({relative.lower() for relative, _ in files}) != len(files):
        raise PeerFailure("FAIL_INTERFACE_DIGEST", "The static interface source is empty or has ambiguous paths.")
    digest = hashlib.sha256()
    _append_digest_frame(digest, b"unity2foxglove:foxrun-ros2-interface-digest:v1")
    _append_digest_frame(digest, b"1")
    for relative, content in sorted(files, key=lambda item: item[0]):
        _append_digest_frame(digest, relative.encode("utf-8"))
        _append_digest_frame(digest, content)
    return digest.hexdigest()
def _require_owned_workspace_path(path: pathlib.Path, build_root: pathlib.Path) -> pathlib.Path:
    """Accept exactly one named peer-workspace child below the caller's build root."""

    candidate = pathlib.Path(path).resolve()
    root = pathlib.Path(build_root).resolve()
    try:
        relative = candidate.relative_to(root)
    except ValueError as exc:
        raise PeerFailure("FAIL_PEER_WORKSPACE", "The peer workspace must remain below the Phase181 build root.") from exc
    if len(relative.parts) != 2 or relative.parts[1] != "peer-workspace" or _PROFILE_ID.fullmatch(relative.parts[0]) is None:
        raise PeerFailure("FAIL_PEER_WORKSPACE", "The peer workspace path is not a named owned profile workspace.")
    return candidate
def prepare_owned_workspace(build_root: pathlib.Path, profile_id: str) -> pathlib.Path:
    """Create a fresh, marked workspace and remove only a previously marked equivalent one."""

    if _PROFILE_ID.fullmatch(profile_id) is None:
        raise PeerFailure("FAIL_PEER_WORKSPACE", "The profile identifier is not safe for an owned workspace path.")
    root = pathlib.Path(build_root).resolve()
    workspace = _require_owned_workspace_path(root / profile_id / "peer-workspace", root)
    if workspace.exists():
        cleanup_owned_workspace(workspace, root)
    try:
        workspace.mkdir(parents=True, exist_ok=False)
        (workspace / OWNERSHIP_MARKER_NAME).write_text(_OWNERSHIP_MARKER_CONTENT, encoding="utf-8")
    except OSError as exc:
        raise PeerFailure("FAIL_PEER_WORKSPACE", "The owned peer workspace could not be prepared.") from exc
    return workspace
def cleanup_owned_workspace(workspace: pathlib.Path, build_root: pathlib.Path) -> None:
    """Delete only a workspace created by :func:`prepare_owned_workspace`."""

    candidate = _require_owned_workspace_path(workspace, build_root)
    marker = candidate / OWNERSHIP_MARKER_NAME
    try:
        owned = marker.read_text(encoding="utf-8") == _OWNERSHIP_MARKER_CONTENT
    except OSError:
        owned = False
    if not owned:
        raise PeerFailure("FAIL_PEER_WORKSPACE", "Refusing to delete a workspace without the Phase181 ownership marker.")
    try:
        shutil.rmtree(candidate)
    except OSError as exc:
        raise PeerFailure("FAIL_PEER_WORKSPACE", "The owned peer workspace could not be cleaned up.") from exc
def _peer_build_tool_identity(path: pathlib.Path) -> dict[str, object]:
    """Return a private hash input for one pinned build tool without persisting its path."""

    candidate = pathlib.Path(path).resolve()
    try:
        stat = candidate.stat()
    except OSError:
        return {"path": str(candidate), "available": False}
    return {
        "path": str(candidate),
        "available": True,
        "size": stat.st_size,
        "modifiedNs": stat.st_mtime_ns,
    }
def peer_build_cache_key(
    lock: StaticInterfaceLock,
    profile_id: str,
    distro: str,
    rmw: str,
    toolchain: WindowsPeerToolchain,
    colcon_command: Sequence[str],
) -> str:
    """Hash every locked input that makes one local generated-interface build reusable."""

    if _PROFILE_ID.fullmatch(profile_id) is None or not distro or not rmw or not colcon_command:
        raise PeerFailure("FAIL_PEER_WORKSPACE", "The peer build cache requires a complete bounded profile identity.")
    payload = {
        "format": _PEER_BUILD_CACHE_FORMAT,
        "profileId": profile_id,
        "distro": distro,
        "rmw": rmw,
        "rosPackageName": lock.ros_package_name,
        "interfaceRevision": lock.interface_revision,
        "interfaceDigest": lock.interface_digest,
        "colconCommand": list(colcon_command),
        "toolchain": {
            "ros2Root": _peer_build_tool_identity(toolchain.ros2_root),
            "ros2LocalSetup": _peer_build_tool_identity(toolchain.ros2_root / "local_setup.bat"),
            "python": _peer_build_tool_identity(toolchain.python_executable),
            "colcon": _peer_build_tool_identity(toolchain.colcon_executable),
        },
    }
    encoded = json.dumps(payload, sort_keys=True, separators=(",", ":"), ensure_ascii=True).encode("utf-8")
    return hashlib.sha256(encoded).hexdigest()
def _has_reusable_peer_build_outputs(workspace: pathlib.Path, ros_package_name: str) -> bool:
    """Require the minimal generated install surface before accepting a cached peer build."""

    install = pathlib.Path(workspace) / "install"
    return (install / "local_setup.bat").is_file() and (install / "share" / ros_package_name / "package.xml").is_file()
def _peer_build_cache_matches(workspace: pathlib.Path, cache_key: str, ros_package_name: str) -> bool:
    """Accept only a sealed owned workspace with the exact locked build fingerprint."""

    candidate = pathlib.Path(workspace)
    try:
        marker_matches = (candidate / OWNERSHIP_MARKER_NAME).read_text(encoding="utf-8") == _OWNERSHIP_MARKER_CONTENT
        manifest = json.loads((candidate / PEER_BUILD_CACHE_NAME).read_text(encoding="utf-8"))
    except (OSError, UnicodeDecodeError, json.JSONDecodeError):
        return False
    return (
        marker_matches
        and isinstance(manifest, Mapping)
        and manifest.get("format") == _PEER_BUILD_CACHE_FORMAT
        and manifest.get("cacheKey") == cache_key
        and manifest.get("rosPackageName") == ros_package_name
        and _has_reusable_peer_build_outputs(candidate, ros_package_name)
    )
def prepare_peer_build_workspace(
    build_root: pathlib.Path,
    profile_id: str,
    cache_key: str,
    ros_package_name: str,
) -> tuple[pathlib.Path, bool]:
    """Reuse one sealed matching peer build or safely replace only an owned stale workspace."""

    if _SHA256.fullmatch(cache_key) is None or not ros_package_name:
        raise PeerFailure("FAIL_PEER_WORKSPACE", "The peer build cache key or ROS package identity is invalid.")
    root = pathlib.Path(build_root).resolve()
    workspace = _require_owned_workspace_path(root / profile_id / "peer-workspace", root)
    if workspace.exists() and _peer_build_cache_matches(workspace, cache_key, ros_package_name):
        return workspace, True
    return prepare_owned_workspace(root, profile_id), False
def seal_peer_build_workspace(workspace: pathlib.Path, cache_key: str, ros_package_name: str) -> None:
    """Seal a successful owned generated-interface build for exact future reuse."""

    candidate = pathlib.Path(workspace)
    try:
        marker_matches = (candidate / OWNERSHIP_MARKER_NAME).read_text(encoding="utf-8") == _OWNERSHIP_MARKER_CONTENT
    except (OSError, UnicodeDecodeError) as exc:
        raise PeerFailure("FAIL_PEER_WORKSPACE", "The peer build workspace is not owned by this helper.") from exc
    if not marker_matches or _SHA256.fullmatch(cache_key) is None or not ros_package_name:
        raise PeerFailure("FAIL_PEER_WORKSPACE", "The peer build workspace cannot be sealed with an invalid identity.")
    if not _has_reusable_peer_build_outputs(candidate, ros_package_name):
        raise PeerFailure("FAIL_PEER_BUILD", "The peer build did not create its required generated install outputs.")
    protocol.write_summary_atomic(
        candidate / PEER_BUILD_CACHE_NAME,
        {
            "format": _PEER_BUILD_CACHE_FORMAT,
            "cacheKey": cache_key,
            "rosPackageName": ros_package_name,
        },
    )
def preserve_failure_log(
    workspace: pathlib.Path,
    output_directory: pathlib.Path,
    source_name: str,
    destination_name: str,
) -> None:
    """Retain one owned diagnostic log below the profile build root before cleanup."""

    source = pathlib.Path(workspace) / source_name
    destination = pathlib.Path(output_directory) / destination_name
    if not source.is_file():
        return
    try:
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source, destination)
    except OSError:
        # The stable summary remains authoritative; diagnostics are best effort.
        return
def requires_short_windows_peer_workspace_alias(workspace: pathlib.Path, platform_name: str | None = None) -> bool:
    """Return whether the projected ROSIDL native object would exceed CMake's Windows path margin."""

    if (platform_name or os.name) != "nt":
        return False
    projected = (
        pathlib.Path(workspace)
        / "build"
        / ROS_PACKAGE_NAME
        / "CMakeFiles"
        / (ROS_PACKAGE_NAME + "_s__rosidl_typesupport_introspection_c.dir")
        / _LONGEST_WINDOWS_ROSIDL_OBJECT
    )
    return len(str(projected)) > 250
@contextlib.contextmanager
def temporary_short_windows_peer_workspace(workspace: pathlib.Path) -> Iterator[tuple[pathlib.Path, pathlib.Path]]:
    """Map only a verified owned ``build`` workspace when native ROSIDL path length requires it."""

    physical_workspace = pathlib.Path(workspace).resolve()
    if not requires_short_windows_peer_workspace_alias(physical_workspace):
        yield physical_workspace, physical_workspace
        return
    subst = pathlib.Path(os.environ.get("SystemRoot", r"C:\Windows")) / "System32" / "subst.exe"
    if not subst.is_file():
        raise PeerFailure("FAIL_PEER_WORKSPACE", "The Windows peer build requires a short workspace alias but subst is unavailable.")
    mapped_drive: str | None = None
    for letter in "ZYXWVUTSRQPONMLKJIHGFED":
        candidate = pathlib.Path(letter + ":\\")
        if candidate.exists():
            continue
        result = subprocess.run(
            (str(subst), letter + ":", str(physical_workspace)),
            shell=False,
            capture_output=True,
            text=True,
            errors="replace",
            check=False,
        )
        if result.returncode == 0:
            if candidate.exists():
                mapped_drive = letter
                break
            # ``subst`` can report success before the drive becomes visible
            # to this process.  Release that reservation before trying the
            # next letter; otherwise a failed probe leaks a drive mapping.

            _release_subst_mapping(subst, letter, "FAIL_PEER_WORKSPACE")

    if mapped_drive is None:
        raise PeerFailure("FAIL_PEER_WORKSPACE", "The Windows peer build could not reserve a short workspace alias.")
    mapped_workspace = pathlib.Path(mapped_drive + ":\\")
    try:
        yield physical_workspace, mapped_workspace
    finally:

        _release_subst_mapping(subst, mapped_drive, "FAIL_PEER_WORKSPACE")
@contextlib.contextmanager
def temporary_short_windows_plugin_alias(plugin_directory: pathlib.Path) -> Iterator[pathlib.Path]:
    """Map one verified native plugin directory for the lifetime of an owned Unity child process."""

    directory = pathlib.Path(plugin_directory).resolve()
    if not directory.is_dir():
        raise PeerFailure("FAIL_EDITOR_BATCH", "The selected native custom-interface plugin directory is unavailable.")
    if os.name != "nt":
        yield directory
        return
    subst = pathlib.Path(os.environ.get("SystemRoot", r"C:\\Windows")) / "System32" / "subst.exe"
    if not subst.is_file():
        raise PeerFailure("FAIL_EDITOR_BATCH", "The Unity Editor Batch native loader requires subst.exe for its short plugin path.")
    mapped_drive: str | None = None
    for letter in "ZYXWVUTSRQPONMLKJIHGFED":
        candidate = pathlib.Path(letter + ":\\")
        if candidate.exists():
            continue
        result = subprocess.run(
            (str(subst), letter + ":", str(directory)),
            shell=False,
            capture_output=True,
            text=True,
            errors="replace",
            check=False,
        )
        if result.returncode == 0:
            if candidate.exists():
                mapped_drive = letter
                break
            # ``subst`` can report success before the drive becomes visible
            # to this process.  Release that reservation before trying the
            # next letter; otherwise a failed probe leaks a drive mapping.

            _release_subst_mapping(subst, letter, "FAIL_EDITOR_BATCH")

    if mapped_drive is None:
        raise PeerFailure("FAIL_EDITOR_BATCH", "The Unity Editor Batch could not reserve a short native plugin path.")
    try:
        yield pathlib.Path(mapped_drive + ":\\")
    finally:

        _release_subst_mapping(subst, mapped_drive, "FAIL_EDITOR_BATCH")
def build_colcon_command(
    colcon: pathlib.Path,
    ros_package_name: str,
) -> list[str]:
    """Build only the locked interface package with an explicit colcon executable."""

    if not ros_package_name or "/" in ros_package_name or "\\" in ros_package_name:
        raise PeerFailure("FAIL_PEER_SOURCE", "The ROS package name is not safe for an explicit colcon selection.")
    return [str(pathlib.Path(colcon)), "build", "--merge-install", "--packages-select", ros_package_name]
def build_windows_colcon_command(
    colcon: pathlib.Path,
    ros_package_name: str,
    python_executable: pathlib.Path,
) -> list[str]:
    """Build the locked interface package with the Windows ROS2 native toolchain contract."""

    return [
        *build_colcon_command(colcon, ros_package_name),
        "--cmake-args",
        "-G",
        "Ninja",
        "-DCMAKE_BUILD_TYPE=Release",
        "-DPython3_EXECUTABLE=" + pathlib.Path(python_executable).as_posix(),
        "-DPYTHON_EXECUTABLE=" + pathlib.Path(python_executable).as_posix(),
    ]
def stage_locked_ros_source(
    static_interface_package: pathlib.Path,
    workspace: pathlib.Path,
    ros_package_name: str,
) -> pathlib.Path:
    """Copy the locked ``Ros2Package~`` source into a fresh caller-owned workspace."""

    source = pathlib.Path(static_interface_package) / "Ros2Package~"
    destination = pathlib.Path(workspace) / "src" / ros_package_name
    if not source.is_dir() or not (source / "package.xml").is_file() or not (source / "CMakeLists.txt").is_file():
        raise PeerFailure("FAIL_PEER_SOURCE", "The locked ROS source package is missing required build inputs.")
    if destination.exists():
        raise PeerFailure("FAIL_PEER_SOURCE", "The owned peer workspace already contains a source package.")
    try:
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copytree(source, destination, ignore=shutil.ignore_patterns("*.meta", "bin", "obj", "build", "install", "log"))
    except OSError as exc:
        raise PeerFailure("FAIL_PEER_SOURCE", "The locked ROS source package could not be staged.") from exc
    return destination


__all__ = [name for name in globals() if not name.startswith("__")]
