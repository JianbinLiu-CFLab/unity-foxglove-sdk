from __future__ import annotations
from .bridge_build_and_ros_runtime import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase184_profile_acceptance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
def _start_case_workers_serially(
    *,
    config: Mapping[str, object],
    repository: pathlib.Path,
    output: pathlib.Path,
    runtime: PreparedRosRuntime | None,
    worker_roles: Iterable[str],
    owner: OwnedProcessSet,
    streams: list[TextIO],
    desktop_barrier: pathlib.Path | None = None,
) -> None:
    """Start one worker at a time so ROS participants cannot race initialization."""

    for role in sorted(worker_roles):
        if role == "foxglove-client":
            python_executable = pathlib.Path(sys.executable)
            environment = _clean_environment(os.environ)
            if desktop_barrier is not None:
                environment[DESKTOP_CLIENT_BARRIER_ENV] = str(desktop_barrier)
            cwd = repository
        else:
            if runtime is None:
                raise AcceptanceFailure(
                    "FAIL_RUNTIME_SELECTION",
                    f"{role} requires a selected ROS runtime.",
                )
            python_executable = pathlib.Path(runtime.toolchain.python_executable)
            environment = _without_desktop_client_barrier(
                runtime.actor_environment
            )
            cwd = runtime.peer_runtime_workspace
        _launch_logged_process(
            role,
            build_worker_command(
                python_executable,
                role,
                pathlib.Path(str(config["outputRoot"])) / "run-config.json",
            ),
            cwd=cwd,
            environment=environment,
            log_path=output / f"{role}.log",
            owner=owner,
            streams=streams,
        )
        _wait_for_actor_readiness(config, (role,), owner)


def _installed_bridge_executable(runtime: PreparedRosRuntime | None) -> pathlib.Path:
    """Handle the installed bridge executable step."""

    if runtime is None or runtime.bridge_install is None:
        raise AcceptanceFailure(
            "FAIL_BRIDGE",
            "The selected case has no built Bridge overlay.",
        )
    return _require_file(
        runtime.bridge_install
        / "lib"
        / "unity2foxglove_ros2_bridge"
        / "unity2foxglove_ros2_bridge.exe",
        "FAIL_BRIDGE",
        "Installed native Bridge executable",
    )


def _start_bridge_actor(
    *,
    config: Mapping[str, object],
    output: pathlib.Path,
    runtime: PreparedRosRuntime | None,
    owner: OwnedProcessSet,
    streams: list[TextIO],
) -> dict[str, object]:
    """Start one Bridge and prove its correlated health before Unity starts."""

    if owner.process("bridge") is not None:
        raise AcceptanceFailure(
            "FAIL_BRIDGE",
            "The selected case attempted to start its Bridge more than once.",
        )
    bridge_executable = _installed_bridge_executable(runtime)
    log_path = output / "bridge.log"
    bridge = _launch_logged_process(
        "bridge",
        build_bridge_command(
            bridge_executable,
            str(config["bridgeHost"]),
            int(config["bridgePort"]),
        ),
        cwd=runtime.bridge_runtime_workspace or output,
        environment=_without_desktop_client_barrier(
            runtime.actor_environment
        ),
        log_path=log_path,
        owner=owner,
        streams=streams,
    )
    health = wait_for_bridge_health(config, bridge)
    ready = {
        "state": "u2r2-health-ready",
        "sidecarName": health["sidecarName"],
        "sidecarVersion": health["sidecarVersion"],
    }
    write_actor_ready(config, "bridge", ready)
    return {"bridge-health": health}


def _start_case_actors(
    *,
    config: Mapping[str, object],
    repository: pathlib.Path,
    output: pathlib.Path,
    runtime: PreparedRosRuntime | None,
    owner: OwnedProcessSet,
    streams: list[TextIO],
    desktop_barrier: pathlib.Path | None = None,
) -> tuple[set[str], dict[str, object]]:
    """Start every required non-Unity actor and return readiness evidence."""

    contract = protocol.CASE_CONTRACTS[str(config["case"])]
    parent_ready_roles: set[str] = set()
    parent_evidence: dict[str, object] = {}

    if "zenoh-router" in contract.required_actors:
        if (
            runtime is None
            or runtime.zenoh_router is None
            or runtime.zenoh_router_environment is None
            or runtime.zenoh_router_endpoint is None
        ):
            raise AcceptanceFailure(
                "FAIL_RUNTIME_SELECTION",
                "The stream case has no prepared owned Zenoh router.",
            )
        router = _launch_logged_process(
            "zenoh-router",
            [str(runtime.zenoh_router)],
            cwd=repository,
            environment=_without_desktop_client_barrier(
                runtime.zenoh_router_environment
            ),
            log_path=output / "zenoh-router.log",
            owner=owner,
            streams=streams,
        )
        ready = wait_for_owned_zenoh_router(
            router,
            output / "zenoh-router.log",
            runtime.zenoh_router_endpoint,
        )
        ready["topologyId"] = config["zenohTopologyId"]
        write_actor_ready(config, "zenoh-router", ready)
        parent_ready_roles.add("zenoh-router")
        parent_evidence["zenoh-router"] = ready

    if "bridge" in contract.required_actors:
        parent_evidence.update(
            _start_bridge_actor(
                config=config,
                output=output,
                runtime=runtime,
                owner=owner,
                streams=streams,
            )
        )
        parent_ready_roles.add("bridge")

    worker_roles = set(contract.required_actors) - {"bridge", "zenoh-router"}
    _start_case_workers_serially(
        config=config,
        repository=repository,
        output=output,
        runtime=runtime,
        worker_roles=worker_roles,
        owner=owner,
        streams=streams,
        desktop_barrier=desktop_barrier,
    )
    all_ready = worker_roles | parent_ready_roles
    expected_ready = set(contract.required_actors)
    if all_ready != expected_ready:
        raise AcceptanceFailure(
            "FAIL_PREFLIGHT",
            "Actor readiness did not cover the exact selected case.",
        )
    return worker_roles, parent_evidence


def _wait_for_unity_exit(
    config: Mapping[str, object],
    unity,
    owner: OwnedProcessSet,
    worker_roles: Iterable[str],
) -> TerminalMarker:
    """Wait for unity exit."""

    unity_log = pathlib.Path(str(config["unityLog"]))
    watchdog = protocol.ProgressWatchdog("unity-startup")
    last_progress = _progress_snapshot((unity_log,))
    watchdog.progress("Unity Batch process started")
    while unity.poll() is None:
        progress = _progress_snapshot((unity_log,))
        if progress != last_progress:
            last_progress = progress
            watchdog.progress(f"Unity log bytes={max(0, progress[0][1])}")
        marker = find_terminal_marker(
            read_log_lines(unity_log),
            str(config["case"]),
            str(config["token"]),
        )
        if marker is not None and marker.verdict == "FAIL":
            raise AcceptanceFailure(
                "FAIL_TERMINAL",
                "Unity reported a correlated case failure.",
            )
        for role in worker_roles:
            process = owner.process(role)
            if process is None or process.poll() is None:
                continue
            result_path = _actor_path(config, "resultFiles", role)
            if not result_path.is_file():
                raise AcceptanceFailure(
                    "FAIL_PROCESS_EXIT",
                    f"{role} exited before producing result evidence.",
                )
            read_actor_document(config, role, "resultFiles")
        try:
            watchdog.check()
        except protocol.ProtocolFailure as exc:
            raise AcceptanceFailure("FAIL_UNITY_STARTUP", str(exc)) from exc
        time.sleep(0.1)
    if int(unity.returncode) != 0:
        raise AcceptanceFailure(
            "FAIL_PROCESS_EXIT",
            f"Unity Batch exited with code {unity.returncode}.",
        )
    return wait_for_terminal_marker(config, 5.0)


def _write_parent_actor_results(
    config: Mapping[str, object],
    output: pathlib.Path,
    parent_evidence: Mapping[str, object],
) -> None:
    """Write parent actor results."""

    contract = protocol.CASE_CONTRACTS[str(config["case"])]
    if "bridge" in contract.required_actors:
        health = parent_evidence.get("bridge-health")
        if not isinstance(health, Mapping):
            raise AcceptanceFailure(
                "FAIL_BRIDGE",
                "Bridge health evidence is absent.",
            )
        validate_bridge_health_response(health, b"", str(config["token"]))
        bridge = parse_bridge_publisher_evidence(config, output / "bridge.log")
        bridge.update(
            {
                "healthReady": True,
                "healthProcess": "bridge",
                "publisherProcess": "bridge",
                "sameProcessHealthAndPublisher": True,
                "sidecarVersion": health["sidecarVersion"],
                "healthTokenSha256": protocol.token_sha256(str(config["token"])),
            }
        )
        write_actor_result(config, "bridge", verdict="PASS", evidence=bridge)
    if "zenoh-router" in contract.required_actors:
        evidence = parent_evidence.get("zenoh-router")
        if not isinstance(evidence, Mapping):
            raise AcceptanceFailure(
                "FAIL_TERMINAL",
                "Owned Zenoh router evidence is absent.",
            )
        write_actor_result(
            config,
            "zenoh-router",
            verdict="PASS",
            evidence={
                **dict(evidence),
                "sessionConfigOwned": True,
                "routerConfigOwned": True,
            },
        )




__all__ = [name for name in globals() if not name.startswith("__")]
