from __future__ import annotations
from .sidecar_and_evidence import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase186_bridge_live.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
def run_live(
    repository: pathlib.Path,
    config: Mapping[str, Any],
    *,
    unity_editor: pathlib.Path,
    manual_timeout_seconds: float,
    reporter: Any | None = None,
) -> tuple[dict[str, Any], dict[str, Any], dict[str, Any]]:
    """Run one exact live case and return terminal actor/observation/cleanup sections."""

    output = pathlib.Path(str(config["outputRoot"]))
    config_path = output / "run-config.json"
    try:
        runtime = prepare_runtime(repository, config, reporter=reporter)
    except BaseException:
        if reporter is not None:
            reporter.transition(
                "5/5", "validating completion and cleaning owned resources"
            )
        raise
    if reporter is not None:
        reporter.transition(
            "3/5", "starting and proving sidecar, peer, observer, and router"
        )
    owner = OwnedLiveProcesses()
    pointer: pathlib.Path | None = None
    worker_results: dict[str, Mapping[str, Any]] = {}
    health_generations: list[Mapping[str, Any]] = []
    failure: BaseException | None = None
    cleanup_errors: list[str] = []
    try:
        if runtime.zenoh_router is not None:
            router = owner.launch(
                "zenoh-router",
                "zenoh-router",
                [str(runtime.zenoh_router)],
                cwd=runtime.zenoh_router.parent,
                environment=runtime.zenoh_router_environment or runtime.environment,
                output_root=output,
            )
            assert runtime.zenoh_endpoint is not None
            _wait_port(*runtime.zenoh_endpoint, router, 60.0)
        _sidecar, health = _launch_sidecar(owner, runtime, config, "sidecar-1")
        health_generations.append(health)

        for role in _worker_process_roles(config):
            python = (
                runtime.python_executable
                if role in {"ros-peer", "graph-observer"}
                else pathlib.Path(sys.executable).resolve(strict=True)
            )
            environment = (
                runtime.environment
                if role in {"ros-peer", "graph-observer"}
                else _clean_unity_environment(os.environ)
            )
            owner.launch(
                role,
                role,
                _worker_command(python, role, config_path),
                cwd=repository,
                environment=environment,
                output_root=output,
            )
            _wait_actor_document(config, owner, role, "ready", 180.0)
            if role == "ros-peer" and "graph-observer" in _worker_roles(config):
                _wait_actor_document(
                    config,
                    owner,
                    "graph-observer",
                    "ready",
                    180.0,
                    owner_role="ros-peer",
                )

        if config["caseId"] == "frozen-v1":
            for role in _worker_roles(config):
                worker_results[role] = _wait_actor_document(
                    config, owner, role, "result", 180.0
                )
        elif bool(config["manual"]):
            pointer = _write_manual_pointer(repository, config)
            if reporter is None:
                print(
                    "PHASE186_MANUAL_READY"
                    + f" case={config['caseId']} run={config['runId']}"
                    + f" tokenHash={config['tokenHash']} head={config['head']}"
                    + f" pointer={pointer}",
                    flush=True,
                )
            else:
                reporter.transition("4/5", "prepare the Unity scene")
                reporter.unity_prepare(
                    "the Phase186 ROS2 Bridge acceptance scene"
                )
            deadline = time.monotonic() + manual_timeout_seconds
            gate_written = False
            scene_preparing_reported = False
            scene_ready_reported = False
            reported_progress: set[str] = set()
            while time.monotonic() < deadline:
                _mirror_manual_log(config)
                prepare_failure = _manual_scene_prepare_failure(config)
                if prepare_failure is not None:
                    raise LiveFailure(
                        "FAIL_UNITY_PREPARE",
                        "Unity scene preparation failed: " + prepare_failure,
                    )
                context_failure = _manual_context_failure(config)
                if context_failure is not None:
                    raise LiveFailure(
                        "FAIL_UNITY_CONTEXT",
                        "Unity acceptance context failed: " + context_failure,
                    )
                _raise_if_worker_process_exited(config, owner)
                if (
                    not scene_preparing_reported
                    and _manual_scene_preparing_in_log(config)
                ):
                    scene_preparing_reported = True
                    if reporter is not None:
                        reporter.transition(
                            "4/5",
                            "Unity is generating and compiling; waiting for stable READY",
                        )
                if (
                    not scene_ready_reported
                    and _manual_scene_ready_in_log(config)
                ):
                    scene_ready_reported = True
                    if reporter is not None:
                        reporter.transition(
                            "4/5",
                            "Unity scene compiled; Play Mode is now ready",
                        )
                        reporter.unity_play_ready(
                            "the Phase186 ROS2 Bridge acceptance scene"
                        )
                    else:
                        print(
                            "PHASE186_MANUAL_PLAY_READY"
                            + f" case={config['caseId']} run={config['runId']}"
                            + f" tokenHash={config['tokenHash']} head={config['head']}",
                            flush=True,
                        )
                if reporter is not None:
                    _report_manual_progress(config, reporter, reported_progress)
                for role in _worker_roles(config):
                    if role not in worker_results:
                        document = _read_actor_document(config, role, "result")
                        if document is not None:
                            worker_results[role] = document
                if len(worker_results) == len(_worker_roles(config)) and not gate_written:
                    _write_gate(config)
                    gate_written = True
                if _manual_marker_in_log(config):
                    _wait_manual_editor_release(config, reporter)
                    break
                time.sleep(0.1)
            else:
                raise LiveFailure("FAIL_TERMINAL", "manual completion marker expired")
            if len(worker_results) != len(_worker_roles(config)):
                raise LiveFailure("FAIL_EVIDENCE", "manual live actor evidence is incomplete")
            _parse_unity_evidence(
                config,
                require_pass_marker=False,
            )
        else:
            owner.launch(
                "unity",
                "unity",
                _unity_command(unity_editor, config),
                cwd=repository,
                environment=_build_unity_environment(os.environ, config),
                output_root=output,
            )
            _wait_unity_ready(config, owner)
            if config["caseId"] in {"reconnect-degraded-recovery", "lifecycle"}:
                time.sleep(0.75)
                _restart_sidecar_after_observed_disconnect(
                    owner,
                    runtime,
                    config,
                    health_generations,
                )
            roles = _worker_roles(config)
            if config["caseId"] == "fanout-fairness-health":
                worker_results["graph-observer"] = _wait_actor_document(
                    config,
                    owner,
                    "graph-observer",
                    "result",
                    LIVE_ACTOR_RESULT_TIMEOUT_SECONDS,
                    owner_role="ros-peer",
                )
                _write_exercise_gate(config)
            for role in roles:
                if role in worker_results:
                    continue
                worker_results[role] = _wait_actor_document(
                    config,
                    owner,
                    role,
                    "result",
                    LIVE_ACTOR_RESULT_TIMEOUT_SECONDS,
                    owner_role=_owner_role_for_document(config, role),
                )
            _write_gate(config)
            unity = owner.record("unity").process
            try:
                exit_code = unity.wait(timeout=240.0)
            except subprocess.TimeoutExpired as exc:
                raise LiveFailure("FAIL_TERMINAL", "Unity terminal exit expired") from exc
            if exit_code != 0:
                raise LiveFailure("FAIL_PROCESS_EXIT", f"Unity exited {exit_code}")
            _parse_unity_evidence(config)

        bridge_document = {
            "runtimeRowId": runtime.row_id,
            "distro": runtime.distro,
            "rmw": runtime.rmw,
            "healthGenerations": health_generations,
            "buildSummary": str(
                repository
                / "build"
                / "phase186"
                / "bridge"
                / runtime.row_id
                / "build-summary.json"
            ),
        }
        live_peer._write_json_atomic(output / "bridge-evidence.json", bridge_document)
    except BaseException as exc:
        failure = exc
    finally:
        if reporter is not None:
            reporter.transition(
                "5/5", "validating completion and cleaning owned resources"
            )
        try:
            _remove_manual_pointer(pointer, config)
        except BaseException as exc:
            cleanup_errors.append(f"manual pointer cleanup: {exc}")
        for key, label in (
            ("externalGate", "external gate"),
            ("exerciseGate", "exercise gate"),
        ):
            try:
                candidate = pathlib.Path(str(config[key]))
                if candidate.exists():
                    candidate.unlink()
            except BaseException as exc:
                cleanup_errors.append(f"{label} cleanup: {exc}")
        try:
            owner.close()
        except BaseException as exc:
            cleanup_errors.append(f"owned process cleanup: {exc}")

    cleanup = _cleanup_document(
        config,
        owner,
        pointer,
        extra_endpoints=(runtime.zenoh_endpoint,) if runtime.zenoh_endpoint else (),
        cleanup_errors=cleanup_errors,
    )
    if failure is not None:
        raise failure
    if not cleanup["complete"]:
        raise LiveFailure("FAIL_CLEANUP", "owned live cleanup is incomplete")

    actors: dict[str, Any] = {}
    required = set(config["requiredActors"])
    for role in sorted(required):
        if role == "sidecar":
            preferred = "sidecar-2" if owner.has_record("sidecar-2") else "sidecar-1"
            actors[role] = owner.actor_evidence(role, preferred_key=preferred)
        elif role == "graph-observer" and "ros-peer" in required:
            actors[role] = owner.actor_evidence(
                role,
                preferred_key="ros-peer",
                allow_role_alias=True,
            )
        else:
            actors[role] = owner.actor_evidence(role)
    observations = _build_observations(config, worker_results)
    return actors, observations, cleanup


__all__ = [name for name in globals() if not name.startswith("__")]
