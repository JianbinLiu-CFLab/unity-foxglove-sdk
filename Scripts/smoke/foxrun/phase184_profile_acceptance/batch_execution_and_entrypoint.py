from __future__ import annotations
from .parent_run_setup_and_manual import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase184_profile_acceptance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
def run_batch_parent(args: argparse.Namespace) -> int:
    """Run batch parent."""

    prepared = _prepare_parent_run(args, "batch")
    repository = prepared.repository
    editor = prepared.editor
    run_id = prepared.run_id
    output = prepared.output
    config = prepared.config
    config_path = prepared.config_path
    desktop_barrier = (
        desktop_live_protocol.resolve_desktop_client_barrier_path(output)
        if args.wait_for_desktop_client
        else None
    )

    stack = contextlib.ExitStack()
    owner: OwnedProcessSet | None = None
    streams: list[TextIO] = []
    runtime: PreparedRosRuntime | None = None
    terminal: TerminalMarker | None = None
    results: dict[str, dict[str, object]] = {}
    process_codes: dict[str, int] = {}
    owner_stopped_roles: frozenset[str] = frozenset()
    cleanup: dict[str, bool] = {
        "processes": False,
        "files": False,
        "junctions": False,
        "subst": False,
    }
    failure: AcceptanceFailure | None = None
    try:
        job = WindowsKillOnCloseJob()
        owner = OwnedProcessSet(job)
        _ensure_acceptance_scene(editor, repository, output, job)
        if str(config["rosDistro"]) != "core":
            runtime = _prepare_ros_runtime(
                config=config,
                editor=editor,
                repository=repository,
                output=output,
                stack=stack,
                job=job,
            )
        worker_roles, parent_evidence = _start_case_actors(
            config=config,
            repository=repository,
            output=output,
            runtime=runtime,
            owner=owner,
            streams=streams,
            desktop_barrier=desktop_barrier,
        )
        unity_environment = (
            _without_desktop_client_barrier(runtime.unity_environment)
            if runtime is not None
            else _clean_environment(os.environ)
        )
        unity = _launch_logged_process(
            "unity",
            build_unity_batch_command(
                editor,
                repository / "Unity2Foxglove",
                config_path,
                pathlib.Path(str(config["unityLog"])),
            ),
            cwd=repository,
            environment=unity_environment,
            log_path=output / "unity-process.log",
            owner=owner,
            streams=streams,
        )
        terminal = _wait_for_unity_exit(config, unity, owner, worker_roles)
        _write_parent_actor_results(config, output, parent_evidence)
        results = _wait_for_actor_results(
            config,
            protocol.CASE_CONTRACTS[str(config["case"])].required_actors,
            owner,
        )
        _wait_for_clean_worker_exits(owner, worker_roles)
    except AcceptanceFailure as exc:
        failure = exc
    except Exception as exc:
        failure = AcceptanceFailure(
            "FAIL_PREFLIGHT",
            "Unexpected parent failure: " + type(exc).__name__,
        )
    finally:
        if owner is not None:
            try:
                owner.close()
                process_codes = owner.exit_codes()
                owner_stopped_roles = owner.owner_stopped_roles()
            except Exception:
                failure = failure or AcceptanceFailure(
                    "FAIL_CLEANUP",
                    "The Batch process owner could not close every child.",
                )
        for stream in streams:
            with contextlib.suppress(Exception):
                stream.close()
        subst_roots = runtime.subst_roots if runtime is not None else ()
        try:
            stack.close()
        except Exception:
            failure = failure or AcceptanceFailure(
                "FAIL_CLEANUP",
                "The Batch short-path cleanup stack failed.",
            )
        if owner is not None:
            try:
                cleanup = _cleanup_evidence(output, owner, subst_roots)
            except Exception:
                failure = failure or AcceptanceFailure(
                    "FAIL_CLEANUP",
                    "The Batch cleanup evidence could not be collected.",
                )

    if failure is not None:
        _write_failure_record(
            output,
            repository,
            case=str(args.case),
            run_id=run_id,
            failure=failure,
        )
        raise failure
    if terminal is None or owner is None:
        raise AcceptanceFailure(
            "FAIL_TERMINAL",
            "The Batch parent reached no terminal evidence.",
        )
    required_processes = (
        protocol.CASE_CONTRACTS[str(config["case"])].required_actors
        | {"unity"}
    )
    missing_codes = set(required_processes) - set(process_codes)
    unacceptable = {
        role: process_codes[role]
        for role in required_processes & set(process_codes)
        if not process_exit_is_acceptable(
            role,
            process_codes[role],
            owner_requested=role in owner_stopped_roles,
        )
    }
    if missing_codes or unacceptable:
        failure = AcceptanceFailure(
            "FAIL_PROCESS_EXIT",
            "Process exits are incomplete; "
            f"missing={sorted(missing_codes)}, unacceptable={unacceptable}.",
        )
        _write_failure_record(
            output,
            repository,
            case=str(args.case),
            run_id=run_id,
            failure=failure,
        )
        raise failure
    if not all(cleanup.values()):
        failure = AcceptanceFailure(
            "FAIL_CLEANUP",
            "Owned process/file/subst cleanup evidence is incomplete.",
        )
        _write_failure_record(
            output,
            repository,
            case=str(args.case),
            run_id=run_id,
            failure=failure,
        )
        raise failure

    summary = build_pass_summary(
        config=config,
        terminal=terminal,
        results=results,
        process_exit_codes=process_codes,
        unity_version=_unity_version_from_log(
            pathlib.Path(str(config["unityLog"]))
        ),
        cleanup=cleanup,
        owner_stopped_roles=owner_stopped_roles,
    )
    protocol.write_json_atomic(
        output / "summary.json",
        summary,
        repo_root=repository,
    )
    print(
        f"PHASE184G_BATCH_CASE_PASS case={config['case']} "
        f"profile={config['profile']} summary={output / 'summary.json'}",
        flush=True,
    )
    return 0


def _worker_main(args: argparse.Namespace) -> int:
    """Handle the worker main step."""

    config = load_run_config(args.run_config)
    if args.worker == "foxglove-client":
        return run_foxglove_client_worker(config)
    if args.worker == "ros2-peer":
        return run_ros2_peer_worker(config)
    if args.worker == "graph-observer":
        return run_graph_observer_worker(config)
    raise AcceptanceFailure("FAIL_PREFLIGHT", "Unknown worker role.")


def main(argv: Sequence[str] | None = None) -> int:
    """Run the Phase184 acceptance command."""

    try:
        args = validate_arguments(parse_args(argv))
        if args.execution_mode == "worker":
            return _worker_main(args)
        if args.execution_mode == "manual":
            return run_manual_parent(args)
        return run_batch_parent(args)
    except AcceptanceFailure as exc:
        print(exc.code, file=sys.stderr, flush=True)
        return 1
    except KeyboardInterrupt:
        print("FAIL_CLEANUP", file=sys.stderr, flush=True)
        return 130


if __name__ == "__main__":
    raise SystemExit(main())


__all__ = [name for name in globals() if not name.startswith("__")]
