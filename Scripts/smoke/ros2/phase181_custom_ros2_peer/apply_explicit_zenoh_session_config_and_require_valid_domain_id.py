from __future__ import annotations
from .require_matching_unity_readiness_and_worker_phase_deadline import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase181_custom_ros2_peer.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
def apply_explicit_zenoh_session_config(
    env: dict[str, str],
    *,
    rmw: str,
    zenoh_session_config: pathlib.Path | None,
) -> None:
    """Clear ambient Zenoh routing and optionally install one wrapper-owned session config."""

    for key in ("ZENOH_ROUTER_CONFIG_URI", "ZENOH_SESSION_CONFIG_URI", "ZENOH_CONFIG_OVERRIDE"):
        env.pop(key, None)
    if zenoh_session_config is None:
        return
    if rmw != "rmw_zenoh_cpp":
        raise PeerFailure("FAIL_ZENOH_TOPOLOGY", "An explicit Zenoh session config requires rmw_zenoh_cpp.")
    candidate = pathlib.Path(zenoh_session_config)
    if candidate.suffix.lower() not in {".json", ".json5"}:
        raise PeerFailure("FAIL_ZENOH_TOPOLOGY", "The explicit Zenoh session config must be a JSON configuration file.")
    try:
        resolved = candidate.resolve(strict=True)
    except OSError as exc:
        raise PeerFailure("FAIL_ZENOH_TOPOLOGY", "The explicit Zenoh session config does not exist.") from exc
    if not resolved.is_file():
        raise PeerFailure("FAIL_ZENOH_TOPOLOGY", "The explicit Zenoh session config is not a file.")
    env["ZENOH_SESSION_CONFIG_URI"] = str(resolved)
def _require_valid_domain_id(domain_id: int, owner: str) -> int:
    """Return one exact ROS domain id or fail before constructing an environment."""

    if isinstance(domain_id, bool) or not isinstance(domain_id, int) or not 0 <= domain_id <= 232:
        raise PeerFailure("FAIL_ARGUMENTS", f"The {owner} ROS domain id is outside the supported range.")
    return domain_id
def build_peer_environment(
    source: Mapping[str, str],
    ros2_root: pathlib.Path,
    workspace_install: pathlib.Path,
    *,
    distro: str,
    rmw: str,
    domain_id: int,
    topology_id: str | None = None,
    zenoh_session_config: pathlib.Path | None = None,
) -> dict[str, str]:
    """Build an explicit secret-free environment for one owned peer workspace."""

    root = pathlib.Path(ros2_root)
    install = pathlib.Path(workspace_install)
    domain_id = _require_valid_domain_id(domain_id, "peer")
    env = ros2env.sanitized_subprocess_env(dict(source))
    existing_path = env.get("PATH", "")
    existing_pythonpath = env.get("PYTHONPATH", "")
    prefixes = [str(install), str(root)]
    env["AMENT_PREFIX_PATH"] = os.pathsep.join(prefixes)
    env["CMAKE_PREFIX_PATH"] = os.pathsep.join(prefixes)
    env["COLCON_PREFIX_PATH"] = os.pathsep.join(prefixes)
    env["PYTHONPATH"] = os.pathsep.join(
        [str(install / "Lib" / "site-packages"), str(root / "Lib" / "site-packages"), existing_pythonpath]
    ).strip(os.pathsep)
    env["PATH"] = os.pathsep.join([str(install / "bin"), str(install / "Lib"), str(root / "bin"), existing_path]).strip(os.pathsep)
    env["ROS_VERSION"] = "2"
    env["ROS_PYTHON_VERSION"] = "3"
    env["ROS_DISTRO"] = distro
    env["RMW_IMPLEMENTATION"] = rmw
    env["ROS_DOMAIN_ID"] = str(domain_id)
    env.pop("ROS_LOCALHOST_ONLY", None)
    env.pop("ROS_DISCOVERY_SERVER", None)
    apply_explicit_zenoh_session_config(
        env,
        rmw=rmw,
        zenoh_session_config=zenoh_session_config,
    )
    if topology_id:
        env["UNITY2FOXGLOVE_ZENOH_TOPOLOGY_ID"] = topology_id
    return env
def build_player_environment(
    source: Mapping[str, str],
    *,
    distro: str,
    rmw: str,
    domain_id: int,
    interface_revision: int,
    interface_digest: str,
    topology_id: str | None = None,
    zenoh_session_config: pathlib.Path | None = None,
    discovery_range: str = "SUBNET",
) -> dict[str, str]:
    """Build the Player's explicit, non-secret custom-interface environment.

    The Player receives only stable transport selection and static-interface
    identity.  Its opaque run token is passed as a command-line argument to
    the acceptance component and is deliberately excluded from this map and
    every persisted summary.
    """

    if distro not in {"humble", "jazzy", "lyrical"} or not rmw.startswith("rmw_"):
        raise PeerFailure("FAIL_ARGUMENTS", "The Player profile does not identify one supported ROS2 runtime and RMW.")
    domain_id = _require_valid_domain_id(domain_id, "Player")
    if not isinstance(interface_revision, int) or interface_revision <= 0:
        raise PeerFailure("FAIL_INTERFACE_DIGEST", "The Player interface revision is invalid.")
    protocol.require_interface_digest(interface_digest, interface_digest)
    if discovery_range not in {"LOCALHOST", "SUBNET", "SYSTEM_DEFAULT", "OFF"}:
        raise PeerFailure("FAIL_ARGUMENTS", "The Player discovery range is not a supported explicit value.")
    if topology_id is not None and not _safe_marker_token(topology_id):
        raise PeerFailure("FAIL_ZENOH_TOPOLOGY", "The Player Zenoh topology identity is not a safe bounded token.")

    env = ros2env.sanitized_subprocess_env(dict(source))
    # A Player launch is not allowed to quietly inherit a previous run's
    # discovery/configuration selection.  The outer wrapper owns the topology
    # choice and passes only its opaque ID here.
    for key in ("ROS_LOCALHOST_ONLY", "ROS_DISCOVERY_SERVER"):
        env.pop(key, None)
    env["ROS_VERSION"] = "2"
    env["ROS_PYTHON_VERSION"] = "3"
    env["ROS_DISTRO"] = distro
    env["RMW_IMPLEMENTATION"] = rmw
    env["ROS_DOMAIN_ID"] = str(domain_id)
    env["ROS_AUTOMATIC_DISCOVERY_RANGE"] = discovery_range
    env["UNITY2FOXGLOVE_FOXRUN_INTERFACE_REVISION"] = str(interface_revision)
    env["UNITY2FOXGLOVE_FOXRUN_INTERFACE_DIGEST"] = interface_digest
    apply_explicit_zenoh_session_config(
        env,
        rmw=rmw,
        zenoh_session_config=zenoh_session_config,
    )
    if topology_id is not None:
        env["UNITY2FOXGLOVE_ZENOH_TOPOLOGY_ID"] = topology_id
    else:
        env.pop("UNITY2FOXGLOVE_ZENOH_TOPOLOGY_ID", None)
    return env
def build_editor_batch_environment(
    source: Mapping[str, str],
    runtime_plugins: pathlib.Path,
    custom_plugin_alias: pathlib.Path,
) -> dict[str, str]:
    """Put the short custom native-plugin alias before the selected runtime and ROS loader paths."""

    env = ros2env.sanitized_subprocess_env(dict(source))
    existing_path = env.get("PATH", "")
    env["PATH"] = os.pathsep.join(
        entry
        for entry in (str(pathlib.Path(custom_plugin_alias)), str(pathlib.Path(runtime_plugins)), existing_path)
        if entry
    )
    return env
def build_player_command(
    player: pathlib.Path,
    player_log: pathlib.Path,
    token: str,
    timeout_seconds: float,
) -> list[str]:
    """Build one direct Player argv with bounded auto-quit and no shell."""

    if not _safe_marker_token(token):
        raise PeerFailure("FAIL_READY_TOKEN", "The Player correlation token is not safe.")
    timeout = _require_positive_timeout(timeout_seconds, "The Player auto-quit timeout")
    return [
        str(pathlib.Path(player)),
        "-batchmode",
        "-nographics",
        "-logFile",
        str(pathlib.Path(player_log)),
        "--phase181-custom-ros2-player-auto-quit",
        "--phase181-custom-ros2-token",
        token,
        "--phase181-custom-ros2-timeout-seconds",
        format(timeout, "g"),
    ]
def build_editor_batch_command(
    editor: pathlib.Path,
    project: pathlib.Path,
    editor_log: pathlib.Path,
) -> list[str]:
    """Build a direct Editor Batch argv that lets the Phase181 probe own its terminal exit."""

    return [
        str(pathlib.Path(editor)),
        "-batchmode",
        "-nographics",
        "-projectPath",
        str(pathlib.Path(project)),
        "-executeMethod",
        "Phase181BatchModeCustomRos2InteropProbe.Run",
        "-logFile",
        str(pathlib.Path(editor_log)),
    ]
def build_runtime_selection_batch_command(
    editor: pathlib.Path,
    project: pathlib.Path,
    selection_log: pathlib.Path,
    distro: str,
    rmw: str,
) -> list[str]:
    """Build the official Batch-only runtime/add-on selection transaction for one fixed matrix row."""

    if distro not in {"humble", "jazzy", "lyrical"}:
        raise PeerFailure("FAIL_RUNTIME_SELECTION", "The Batch runtime selector received an unsupported ROS2 distribution.")
    try:
        communication_mode = _COMMUNICATION_MODE_BY_RMW[rmw]
    except KeyError as exc:
        raise PeerFailure("FAIL_RUNTIME_SELECTION", "The Batch runtime selector received an unsupported RMW implementation.") from exc
    return [
        str(pathlib.Path(editor)),
        "-batchmode",
        "-nographics",
        "-projectPath",
        str(pathlib.Path(project)),
        "-executeMethod",
        _RUNTIME_SELECTION_EXECUTE_METHOD,
        "-phase181Ros2Distro",
        distro,
        "-phase181Ros2CommunicationMode",
        communication_mode,
        "-logFile",
        str(pathlib.Path(selection_log)),
    ]
def _runtime_selection_log_signature(selection_log: pathlib.Path) -> tuple[int, int] | None:
    """Return the minimal owned Unity-log progress signature without parsing its implementation detail."""

    try:
        status = pathlib.Path(selection_log).stat()
    except OSError:
        return None
    return status.st_size, status.st_mtime_ns
def wait_for_runtime_selection_process(
    process,
    selection_log: pathlib.Path,
    *,
    profile_id: str,
    stall_seconds: float = _RUNTIME_SELECTION_STALL_SECONDS,
    clock=None,
    sleep=None,
    terminate_process=None,
    max_seconds: float | None = _RUNTIME_SELECTION_MAX_SECONDS,
) -> int:
    """Wait without a total-duration limit while Unity's owned selection log continues to make progress."""

    if stall_seconds <= 0.0:
        raise ValueError("The Unity runtime-selection stall window must be positive.")
    now = clock or time.monotonic
    pause = sleep or time.sleep
    terminate = terminate_process or _terminate_owned_child
    last_signature = _runtime_selection_log_signature(selection_log)
    last_progress_at = now()
    deadline = last_progress_at + max_seconds if max_seconds is not None else None
    next_progress_message_at = last_progress_at + _RUNTIME_SELECTION_PROGRESS_INTERVAL_SECONDS

    while True:
        exit_code = process.poll()
        if exit_code is not None:
            return exit_code

        current_time = now()
        if deadline is not None and current_time >= deadline:
            terminate(process)
            raise PeerFailure("FAIL_RUNTIME_SELECTION", "Unity Batch runtime selection exceeded its bounded total duration.")
        try:
            if selection_log.stat().st_size > _RUNTIME_SELECTION_MAX_LOG_BYTES:
                terminate(process)
                raise PeerFailure("FAIL_RUNTIME_SELECTION", "Unity Batch runtime selection exceeded its bounded log capacity.")
        except FileNotFoundError:
            pass
        signature = _runtime_selection_log_signature(selection_log)
        if signature != last_signature:
            last_signature = signature
            last_progress_at = current_time
        if stream_output_is_stalled(last_progress_at, current_time, stall_seconds):
            terminate(process)
            raise PeerFailure(
                "FAIL_RUNTIME_SELECTION",
                "Unity Batch runtime selection stopped producing owned log progress before it completed.",
            )
        if current_time >= next_progress_message_at:
            print(
                "[phase181:" + profile_id + "] Unity runtime/add-on selection is still progressing; "
                + "waiting for its next Package Manager or compiler update.",
                flush=True,
            )
            next_progress_message_at = current_time + _RUNTIME_SELECTION_PROGRESS_INTERVAL_SECONDS
        pause(1.0)
def prepare_unity_batch_profile_selection(args: argparse.Namespace) -> None:
    """Select and resolve the exact Unity runtime/add-on pair before a Batch-only Phase181 peer starts."""

    if not args.unity_batch:
        return
    if args.surface != "editor":
        raise PeerFailure("FAIL_ARGUMENTS", "Batch runtime selection is valid only for the Unity Editor surface.")

    repository = workspace_root()
    profile_id = args.profile_id or (args.distro + "-" + args.rmw.removeprefix("rmw_").replace("_cpp", ""))
    output_directory = _profile_build_directory(repository, profile_id)
    editor = _require_unity_editor_path(args.unity_editor)
    selection_log = output_directory / "runtime-selection.log"
    selection_command = build_runtime_selection_batch_command(
        editor,
        repository / "Unity2Foxglove",
        selection_log,
        args.distro,
        args.rmw,
    )
    print(
        "[phase181:" + profile_id + "] Selecting the Unity runtime/add-on pair for "
        + args.distro + "/" + _COMMUNICATION_MODE_BY_RMW[args.rmw]
        + " through the official Package Manager transaction. There is no total timeout; "
        + format(_RUNTIME_SELECTION_STALL_SECONDS, "g")
        + " seconds without a Unity log update is treated as stalled.",
        flush=True,
    )
    try:
        selection_log.parent.mkdir(parents=True, exist_ok=True)
        selection_process = subprocess.Popen(
            selection_command,
            cwd=str(repository),
            env=ros2env.sanitized_subprocess_env(os.environ),
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            text=True,
            shell=False,
            **worker_launch_options(),
        )
    except OSError as exc:
        raise PeerFailure("FAIL_RUNTIME_SELECTION", "Unity Batch runtime selection could not be started.") from exc
    selection_exit = wait_for_runtime_selection_process(
        selection_process,
        selection_log,
        profile_id=profile_id,
    )
    if selection_exit != 0:
        raise PeerFailure("FAIL_RUNTIME_SELECTION", "Unity Batch runtime selection returned a nonzero status.")
    try:
        selection_output = selection_log.read_text(encoding="utf-8", errors="replace")
    except OSError as exc:
        raise PeerFailure("FAIL_RUNTIME_SELECTION", "Unity Batch selection did not produce its owned diagnostic log.") from exc
    if _RUNTIME_SELECTION_READY_MARKER not in selection_output:
        raise PeerFailure("FAIL_RUNTIME_SELECTION", "Unity Batch selection exited without its validated runtime/add-on readiness marker.")
    print(
        "[phase181:" + profile_id + "] Unity runtime/add-on selection is ready; starting the native peer preflight.",
        flush=True,
    )
def require_player_exit_code(exit_code: int | None) -> None:
    """Accept only the Player's explicit zero success exit after terminal proof."""

    if exit_code != 0:
        raise PeerFailure("FAIL_PLAYER_EXIT", "The Player did not exit with its required zero success code.")
def require_editor_batch_exit_code(exit_code: int | None) -> None:
    """Accept only the Batch probe's explicit zero exit after its complete evidence dwell."""

    if exit_code != 0:
        raise PeerFailure(
            "FAIL_EDITOR_BATCH_EXIT",
            "The Unity Editor Batch probe did not exit with its required zero success code; "
            + "operating-system exit code " + str(exit_code) + ".",
        )
def build_worker_command(
    python_executable: pathlib.Path,
    *,
    role: str,
    probe_role: str = "orchestrate",
    surface: str = "editor",
    workspace: pathlib.Path,
    interface_digest: str,
    token: str,
    unity_log: pathlib.Path | None = None,
    result_json: pathlib.Path | None = None,
    worker_ready_json: pathlib.Path | None = None,
    distro: str | None = None,
    rmw: str | None = None,
    domain_id: int | None = None,
    unity_log_offset: int | None = None,
    static_interface_package: pathlib.Path | None = None,
    ready_timeout_seconds: float | None = None,
    apply_timeout_seconds: float | None = None,
) -> list[str]:
    """Build the pinned-Python worker command without an ambient ROS CLI lookup."""

    protocol.require_interface_digest(interface_digest, interface_digest)
    _normalize_probe_role(probe_role)
    if surface not in {"editor", "player"}:
        raise PeerFailure("FAIL_ARGUMENTS", "The worker surface must be editor or player.")
    command = [
        str(python_executable),
        str(pathlib.Path(__file__).resolve()),
        "--worker",
        "--role",
        role,
        "--probe-role",
        probe_role,
        "--surface",
        surface,
        "--workspace",
        str(workspace),
        "--interface-digest",
        interface_digest,
        "--token",
        token,
    ]
    if unity_log is not None:
        command.extend(["--unity-log", str(unity_log)])
    if result_json is not None:
        command.extend(["--worker-result-json", str(result_json)])
    if worker_ready_json is not None:
        command.extend(["--worker-ready-json", str(worker_ready_json)])
    if distro is not None:
        command.extend(["--distro", distro])
    if rmw is not None:
        command.extend(["--rmw", rmw])
    if domain_id is not None:
        command.extend(["--domain-id", str(domain_id)])
    if unity_log_offset is not None:
        command.extend(["--unity-log-offset", str(unity_log_offset)])
    if static_interface_package is not None:
        command.extend(["--static-interface-package", str(static_interface_package)])
    if ready_timeout_seconds is not None:
        command.extend(["--ready-timeout-seconds", str(ready_timeout_seconds)])
    if apply_timeout_seconds is not None:
        command.extend(["--apply-timeout-seconds", str(apply_timeout_seconds)])
    return command
def _normalize_probe_role(value: str) -> str:
    """Accept one bounded named direction role for a shared generated peer."""

    if value not in PROBE_ROLES:
        raise PeerFailure("FAIL_ARGUMENTS", "The custom-interface peer role is not supported.")
    return value
def _role_requires(role: str) -> tuple[bool, bool, bool]:
    """Return whether a role needs Publish, full-duplex, and null/empty proof."""

    normalized = _normalize_probe_role(role)
    return (
        normalized in {"subscriber", "orchestrate"},
        normalized in {"bidirectional", "orchestrate"},
        normalized in {"bidirectional", "orchestrate"},
    )


__all__ = [name for name in globals() if not name.startswith("__")]
