from __future__ import annotations
from .configuration_and_types import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase184_profile_acceptance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE


def _without_desktop_client_barrier(
    source: Mapping[str, str],
) -> dict[str, str]:
    """Copy an environment without the parent-owned Desktop barrier seam."""

    environment = dict(source)
    environment.pop(DESKTOP_CLIENT_BARRIER_ENV, None)
    return environment


def _clean_environment(source: Mapping[str, str]) -> dict[str, str]:
    """Remove ambient ROS/topology selection while preserving required host basics."""

    environment = _without_desktop_client_barrier(source)
    for key in (
        "AMENT_PREFIX_PATH",
        "CMAKE_PREFIX_PATH",
        "COLCON_PREFIX_PATH",
        "PYTHONPATH",
        "ROS_DISTRO",
        "ROS_VERSION",
        "ROS_PYTHON_VERSION",
        "RMW_IMPLEMENTATION",
        "ROS_DOMAIN_ID",
        "ROS_LOCALHOST_ONLY",
        "ROS_DISCOVERY_SERVER",
        "ROS_AUTOMATIC_DISCOVERY_RANGE",
        "ZENOH_ROUTER_CONFIG_URI",
        "ZENOH_SESSION_CONFIG_URI",
        "ZENOH_CONFIG_OVERRIDE",
        "UNITY2FOXGLOVE_ZENOH_TOPOLOGY_ID",
    ):
        environment.pop(key, None)
    return environment


def build_ros_actor_environment(
    source: Mapping[str, str],
    *,
    bridge_install: pathlib.Path | None,
    peer_install: pathlib.Path,
    ros2_root: pathlib.Path,
    distro: str,
    rmw: str,
    domain_id: int,
    discovery_range: str,
    topology_id: str,
    zenoh_session_config: pathlib.Path | None,
) -> dict[str, str]:
    """Compose one explicit Bridge -> peer -> ROS prefix environment."""

    if distro not in {"humble", "jazzy", "lyrical"}:
        raise AcceptanceFailure("FAIL_RUNTIME_SELECTION", "Unsupported ROS distribution.")
    if rmw not in {"rmw_fastrtps_cpp", "rmw_zenoh_cpp"}:
        raise AcceptanceFailure("FAIL_RUNTIME_SELECTION", "Unsupported RMW implementation.")
    expected_discovery_range = (
        "SUBNET" if rmw == "rmw_fastrtps_cpp" else "LOCALHOST"
    )
    if (
        not 0 <= int(domain_id) <= 232
        or discovery_range != expected_discovery_range
    ):
        raise AcceptanceFailure("FAIL_PREFLIGHT", "ROS domain/discovery selection is invalid.")
    if rmw == "rmw_zenoh_cpp":
        if not topology_id or zenoh_session_config is None:
            raise AcceptanceFailure(
                "FAIL_RUNTIME_SELECTION",
                "Zenoh requires an owned topology identity and session configuration.",
            )
    elif topology_id or zenoh_session_config is not None:
        raise AcceptanceFailure(
            "FAIL_RUNTIME_SELECTION",
            "FastDDS cannot inherit Zenoh topology configuration.",
        )

    environment = _clean_environment(source)
    prefixes = [
        pathlib.Path(item)
        for item in (bridge_install, peer_install, ros2_root)
        if item is not None
    ]
    prefix_text = os.pathsep.join(str(item) for item in prefixes)
    environment["AMENT_PREFIX_PATH"] = prefix_text
    environment["CMAKE_PREFIX_PATH"] = prefix_text
    environment["COLCON_PREFIX_PATH"] = prefix_text
    python_entries = [
        str(item / "Lib" / "site-packages")
        for item in prefixes
    ]
    environment["PYTHONPATH"] = os.pathsep.join(python_entries)
    path_entries: list[str] = []
    for item in prefixes:
        path_entries.extend((str(item / "bin"), str(item / "Lib")))
    if environment.get("PATH"):
        path_entries.append(environment["PATH"])
    environment["PATH"] = os.pathsep.join(path_entries)
    environment["ROS_VERSION"] = "2"
    environment["ROS_PYTHON_VERSION"] = "3"
    environment["ROS_DISTRO"] = distro
    environment["RMW_IMPLEMENTATION"] = rmw
    environment["ROS_DOMAIN_ID"] = str(domain_id)
    environment["ROS_AUTOMATIC_DISCOVERY_RANGE"] = discovery_range
    if topology_id:
        environment["UNITY2FOXGLOVE_ZENOH_TOPOLOGY_ID"] = topology_id
    if zenoh_session_config is not None:
        environment["ZENOH_SESSION_CONFIG_URI"] = str(
            pathlib.Path(zenoh_session_config).resolve()
        )
    return environment


def load_static_interface_identity(repository: pathlib.Path) -> StaticInterfaceIdentity:
    """Read the tracked Phase181 lock that both Unity and ROS workers consume."""

    lock_path = (
        pathlib.Path(repository)
        / "Packages"
        / INTERFACE_PACKAGE_ID
        / LOCK_RELATIVE_PATH
    )
    try:
        lock = json.loads(lock_path.read_text(encoding="utf-8"))
        contract = lock["contracts"][0]
        package = str(lock["rosPackageName"])
        payload = str(contract["payloadMessageName"])
        envelope = str(contract["envelopeMessageName"])
        digest = str(lock["interfaceDigest"])
        revision = int(lock["interfaceRevision"])
    except (OSError, UnicodeError, json.JSONDecodeError, KeyError, IndexError, TypeError, ValueError) as exc:
        raise AcceptanceFailure(
            "FAIL_PREFLIGHT",
            "The tracked Phase181 interface lock is unavailable or malformed.",
        ) from exc
    if not re.fullmatch(r"[0-9a-f]{64}", digest):
        raise AcceptanceFailure("FAIL_PREFLIGHT", "The Phase181 interface digest is invalid.")
    return StaticInterfaceIdentity(
        package=package,
        envelope_type=f"{package}/msg/{envelope}",
        payload_type=f"{package}/msg/{payload}",
        digest=digest,
        revision=revision,
    )


def make_run_config(
    *,
    repository: pathlib.Path,
    run_id: str,
    token: str,
    case: str,
    profile: str,
    output_root: pathlib.Path,
    domain_id: int,
    foxglove_port: int,
    bridge_port: int,
    phase181_workspace: pathlib.Path,
    interface_package: str,
    interface_type: str,
    interface_digest: str,
    execution_mode: str = "batch",
) -> dict[str, object]:
    """Create the exact immutable coordination authority for one case."""

    contract = protocol.validate_case_profile(case, profile)
    selected = protocol.PROFILE_CONTRACTS[contract.profile]
    output = pathlib.Path(output_root).resolve()
    workspace = pathlib.Path(phase181_workspace).resolve()
    bridge_install = (
        _bridge_cache_install_path(repository, contract.profile)
        if "bridge" in contract.required_actors
        else (output / "bridge-overlay" / "install").resolve()
    )
    actors = sorted(
        contract.required_actors | frozenset(contract.deliberately_absent_actors)
    )
    config: dict[str, object] = {
        "schemaVersion": protocol.RUN_CONFIG_SCHEMA_VERSION,
        "executionMode": execution_mode,
        "runId": run_id,
        "token": token,
        "case": case,
        "profile": contract.profile,
        "projectPath": str((pathlib.Path(repository) / "Unity2Foxglove").resolve()),
        "outputRoot": str(output),
        "rosDistro": selected.runtime,
        "rmw": selected.rmw,
        "domainId": int(domain_id),
        "discoveryRange": selected.discovery_range,
        "zenohTopologyId": (
            f"phase184g-{run_id[-12:]}" if selected.rmw == "rmw_zenoh_cpp" else ""
        ),
        "phase181Workspace": str(workspace),
        "phase181Install": str((workspace / "install").resolve()),
        "bridgeOverlayInstall": str(bridge_install),
        "foxgloveHost": "127.0.0.1",
        "foxglovePort": int(foxglove_port),
        "bridgeHost": "127.0.0.1",
        "bridgePort": int(bridge_port),
        "interfacePackage": interface_package,
        "interfaceType": interface_type,
        "interfaceDigest": interface_digest,
        "topics": list(contract.topics),
        "observationWindows": {
            "positiveSeconds": 3,
            "negativeSeconds": 3,
            "streamProductionSeconds": 2,
            "terminalSeconds": 30,
            "teardownSeconds": 30,
        },
        "readyFiles": {
            actor: str((output / "ready" / f"{actor}.json").resolve())
            for actor in actors
        },
        "resultFiles": {
            actor: str((output / "results" / f"{actor}.json").resolve())
            for actor in actors
        },
        "unityLog": str((output / "unity-editor.log").resolve()),
    }
    protocol.validate_run_config(config, repository)
    return config


def write_private_json_atomic(path: pathlib.Path, value: Mapping[str, object]) -> None:
    """Atomically write private coordination state without redacting its token."""

    target = pathlib.Path(path)
    target.parent.mkdir(parents=True, exist_ok=True)
    temporary = target.with_name(target.name + "." + uuid.uuid4().hex + ".tmp")
    try:
        with temporary.open("x", encoding="utf-8", newline="\n") as stream:
            json.dump(value, stream, sort_keys=True, separators=(",", ":"), ensure_ascii=True)
            stream.write("\n")
            stream.flush()
            os.fsync(stream.fileno())
        with contextlib.suppress(OSError):
            os.chmod(temporary, 0o600)
        os.replace(temporary, target)
    except BaseException:
        with contextlib.suppress(OSError):
            temporary.unlink()
        raise


def load_run_config(path: pathlib.Path) -> dict[str, object]:
    """Read and validate one bounded immutable worker config."""

    config_path = pathlib.Path(path).resolve()
    try:
        if config_path.stat().st_size <= 0 or config_path.stat().st_size > MAX_CONFIG_BYTES:
            raise AcceptanceFailure("FAIL_PREFLIGHT", "run-config size is invalid.")
        value = json.loads(config_path.read_text(encoding="utf-8"))
    except AcceptanceFailure:
        raise
    except (OSError, UnicodeError, json.JSONDecodeError) as exc:
        raise AcceptanceFailure("FAIL_PREFLIGHT", "run-config is unavailable or malformed.") from exc
    if not isinstance(value, dict):
        raise AcceptanceFailure("FAIL_PREFLIGHT", "run-config root must be an object.")
    protocol.validate_run_config(value, repository_root())
    expected = pathlib.Path(str(value["outputRoot"])) / "run-config.json"
    if config_path != expected.resolve():
        raise AcceptanceFailure("FAIL_PREFLIGHT", "Worker config path is not its owned immutable path.")
    return value


class _Win32JobApi:
    """Minimal Job Object API with KILL_ON_JOB_CLOSE."""

    JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000
    PROCESS_SET_QUOTA = 0x0100
    PROCESS_TERMINATE = 0x0001
    PROCESS_QUERY_LIMITED_INFORMATION = 0x1000
    JobObjectExtendedLimitInformation = 9

    class _IO_COUNTERS(ctypes.Structure):
        """Mirror the Win32 I/O counters structure."""

        _fields_ = [
            ("ReadOperationCount", ctypes.c_uint64),
            ("WriteOperationCount", ctypes.c_uint64),
            ("OtherOperationCount", ctypes.c_uint64),
            ("ReadTransferCount", ctypes.c_uint64),
            ("WriteTransferCount", ctypes.c_uint64),
            ("OtherTransferCount", ctypes.c_uint64),
        ]

    class _BASIC_LIMIT_INFORMATION(ctypes.Structure):
        """Mirror the Win32 basic limit information structure."""

        _fields_ = [
            ("PerProcessUserTimeLimit", ctypes.c_int64),
            ("PerJobUserTimeLimit", ctypes.c_int64),
            ("LimitFlags", ctypes.c_uint32),
            ("MinimumWorkingSetSize", ctypes.c_size_t),
            ("MaximumWorkingSetSize", ctypes.c_size_t),
            ("ActiveProcessLimit", ctypes.c_uint32),
            ("Affinity", ctypes.c_size_t),
            ("PriorityClass", ctypes.c_uint32),
            ("SchedulingClass", ctypes.c_uint32),
        ]

    class _EXTENDED_LIMIT_INFORMATION(ctypes.Structure):
        """Mirror the Win32 extended limit information structure."""

        pass

    _EXTENDED_LIMIT_INFORMATION._fields_ = [
        ("BasicLimitInformation", _BASIC_LIMIT_INFORMATION),
        ("IoInfo", _IO_COUNTERS),
        ("ProcessMemoryLimit", ctypes.c_size_t),
        ("JobMemoryLimit", ctypes.c_size_t),
        ("PeakProcessMemoryUsed", ctypes.c_size_t),
        ("PeakJobMemoryUsed", ctypes.c_size_t),
    ]

    def __init__(self) -> None:
        """Initialize the Win32 job API."""

        self.kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)

    def create_kill_on_close_job(self) -> int:
        """Handle the create kill on close job step."""

        handle = self.kernel32.CreateJobObjectW(None, None)
        if not handle:
            raise OSError(ctypes.get_last_error(), "CreateJobObjectW failed")
        info = self._EXTENDED_LIMIT_INFORMATION()
        info.BasicLimitInformation.LimitFlags = self.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
        ok = self.kernel32.SetInformationJobObject(
            handle,
            self.JobObjectExtendedLimitInformation,
            ctypes.byref(info),
            ctypes.sizeof(info),
        )
        if not ok:
            error = ctypes.get_last_error()
            self.kernel32.CloseHandle(handle)
            raise OSError(error, "SetInformationJobObject failed")
        return int(handle)

    def assign_pid(self, handle: int, pid: int) -> bool:
        """Handle the assign PID step."""

        access = (
            self.PROCESS_SET_QUOTA
            | self.PROCESS_TERMINATE
            | self.PROCESS_QUERY_LIMITED_INFORMATION
        )
        process_handle = self.kernel32.OpenProcess(access, False, int(pid))
        if not process_handle:
            return False
        try:
            return bool(self.kernel32.AssignProcessToJobObject(handle, process_handle))
        finally:
            self.kernel32.CloseHandle(process_handle)

    def close_handle(self, handle: int) -> None:
        """Handle the close handle step."""

        self.kernel32.CloseHandle(handle)


class WindowsKillOnCloseJob:
    """Parent-held hard-close owner for every Batch helper child."""

    def __init__(self, *, api=None, platform_name: str | None = None) -> None:
        """Initialize the windows kill on close job."""

        self._platform = platform_name or os.name
        self._api = api
        self._handle: int | None = None
        if self._platform == "nt":
            try:
                self._api = api or _Win32JobApi()
                self._handle = int(self._api.create_kill_on_close_job())
            except (OSError, AttributeError, ValueError) as exc:
                raise AcceptanceFailure(
                    "FAIL_PREFLIGHT",
                    "Windows kill-on-close Job Object could not be created.",
                ) from exc

    def assign(self, process) -> None:
        """Assign one process to the owned job."""

        if self._platform != "nt":
            return
        if self._handle is None or self._api is None:
            raise AcceptanceFailure("FAIL_PREFLIGHT", "Windows Job Object is not active.")
        if not self._api.assign_pid(self._handle, int(process.pid)):
            raise AcceptanceFailure(
                "FAIL_PREFLIGHT",
                "A helper-owned child could not be assigned to the Windows Job Object.",
            )

    def close(self) -> None:
        """Close all resources owned by this helper."""

        if self._handle is None or self._api is None:
            return
        handle = self._handle
        self._handle = None
        self._api.close_handle(handle)

    def __enter__(self):
        """Enter the windows kill on close job context."""

        return self

    def __exit__(self, _type, _value, _traceback):
        """Exit the windows kill on close job context without suppressing failures."""

        self.close()


def process_group_options(platform_name: str | None = None) -> dict[str, object]:
    """Give every child a graceful process-group boundary."""

    platform = platform_name or os.name
    if platform == "nt":
        return {"creationflags": getattr(subprocess, "CREATE_NEW_PROCESS_GROUP", 0x200)}
    return {"start_new_session": True}


def terminate_owned_process(process, *, grace_seconds: float = 10.0) -> int:
    """Gracefully stop only one caller-owned Popen, then bound the fallback."""

    exit_code = process.poll()
    if exit_code is not None:
        return int(exit_code)
    try:
        if os.name == "nt":
            process.send_signal(getattr(signal, "CTRL_BREAK_EVENT", signal.SIGTERM))
        else:
            os.killpg(process.pid, signal.SIGTERM)
        return int(process.wait(timeout=grace_seconds))
    except (OSError, ProcessLookupError, subprocess.TimeoutExpired):
        with contextlib.suppress(OSError):
            process.kill()
        try:
            return int(process.wait(timeout=3.0))
        except subprocess.TimeoutExpired:
            return -1


def process_exit_is_acceptable(
    role: str,
    exit_code: int,
    *,
    owner_requested: bool,
) -> bool:
    """Classify one raw child exit without rewriting Windows daemon evidence."""

    return protocol.process_exit_is_acceptable(
        role,
        exit_code,
        owner_requested=owner_requested,
    )


__all__ = [name for name in globals() if not name.startswith("__")]
