from __future__ import annotations
from .owned_project_cleanup import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase186_bridge_acceptance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
def main(
    argv: Sequence[str] | None = None,
    *,
    status: Any | None = None,
    resolve_current_head: bool = False,
) -> int:
    """Run one preflight-only, automatic, or blocking manual acceptance case."""

    args = validate_arguments(
        parse_args(
            argv,
            expected_head_required=not resolve_current_head,
        ),
        allow_missing_expected_head=resolve_current_head,
    )
    if resolve_current_head and (
        not bool(args.manual) or args.expected_head is not None
    ):
        raise protocol.ProtocolFailure(
            "FAIL_PREFLIGHT",
            "coordinator-owned HEAD resolution requires manual mode without --expected-head",
        )
    repository = repository_root()
    run_id, token = _new_run_identity(args.case, args.run_id)
    run_root: pathlib.Path | None = None
    owned_project: bridge_project.OwnedBridgeOnlyProject | None = None
    interruption_stage = "repository/HEAD/Unity/ports preflight"
    try:
        run_root = _owned_run_root(repository, args.output_root, run_id)
        if status is not None:
            status.transition(
                "1/5", "checking repository, HEAD, Unity, and ports"
            )
        if resolve_current_head:
            args.expected_head = protocol.require_head(git_head(repository))
        owned_project = _create_owned_unity_project(
            repository,
            run_id,
            args.unity_composition,
        )
        project = (
            owned_project.path
            if owned_project is not None
            else repository / "Unity2Foxglove"
        )
        with reserve_loopback_port(args.bridge_port) as reservation, reserve_loopback_port(
            args.foxglove_port
        ) as foxglove_reservation:
            if reservation.port == foxglove_reservation.port:
                raise AcceptanceFailure(
                    "FAIL_PREFLIGHT", "reserved Bridge and Foxglove ports collide"
                )
            preflight = _preflight(
                repository,
                project,
                args.unity_composition,
                args,
                run_root,
                run_id,
                token,
                reservation.port,
                foxglove_reservation.port,
            )
            write_json_atomic(run_root / "preflight.json", preflight)
        if args.preflight_only:
            _remove_owned_unity_project(owned_project)
            owned_project = None
            print(
                "PHASE186_PREFLIGHT_PASS"
                + f" run={run_id} case={args.case} tokenHash={protocol.token_sha256(token)}"
                + f" head={args.expected_head}",
                flush=True,
            )
            return EXIT_PASS
        from Scripts.smoke.foxrun import phase186_bridge_live as live

        interruption_stage = "runtime/build, live startup, or manual wait"
        config = _read_json_object(run_root / "run-config.json", "run config")
        installed: InstalledUnityRunBinding | None = None
        live_error: BaseException | None = None
        actors: Mapping[str, Any] | None = None
        observations: Mapping[str, Any] | None = None
        cleanup: Mapping[str, Any] | None = None
        try:
            if args.case != "frozen-v1":
                installed = install_unity_run_binding(
                    project,
                    config,
                )
            actors, observations, cleanup = live.run_live(
                repository,
                config,
                unity_editor=pathlib.Path(str(preflight["unity"]["path"])),
                manual_timeout_seconds=float(args.manual_timeout_seconds),
                reporter=status,
            )
        except BaseException as exc:
            live_error = exc
        finally:
            if installed is not None:
                try:
                    cleanup_unity_run_binding(installed)
                except BaseException as exc:
                    live_error = exc
            if owned_project is not None:
                project_path = owned_project.path
                try:
                    _remove_owned_unity_project(owned_project)
                    owned_project = None
                except BaseException as exc:
                    cleanup = _record_temporary_project_cleanup_failure(
                        run_root,
                        cleanup,
                        project_path,
                        exc,
                    )
                    live_error = exc

        if live_error is not None:
            if isinstance(live_error, live.LiveNotRun):
                result = persist_not_run(
                    run_root,
                    run_id=run_id,
                    token=token,
                    case_id=args.case,
                    head=args.expected_head,
                    prerequisite=live_error.prerequisite,
                )
                _emit_terminal_handoff(
                    status,
                    verdict="NOT RUN",
                    reason=str(result["missingPrerequisite"]),
                    evidence_root=run_root,
                    machine_line=protocol.format_terminal_line(result),
                )
                return EXIT_NOT_RUN
            failure_code = (
                "FAIL_INTERRUPTED"
                if isinstance(live_error, KeyboardInterrupt)
                else
                live_error.code
                if isinstance(live_error, protocol.ProtocolFailure)
                and str(live_error.code).startswith("FAIL_")
                else "FAIL_RUNTIME"
            )
            failure_cleanup = (
                cleanup
                if cleanup is not None
                else load_cleanup_evidence_if_present(run_root)
                or _incomplete_cleanup_evidence(interruption_stage)
            )
            failure_message = (
                f"interrupted during {interruption_stage}"
                if isinstance(live_error, KeyboardInterrupt)
                else str(live_error)[:512] or type(live_error).__name__
            )
            result = protocol.make_failure_summary(
                run_id=run_id,
                token=token,
                case_id=args.case,
                head=args.expected_head,
                evidence_root=str(run_root),
                failure_code=failure_code,
                failure_message=failure_message,
                cleanup=failure_cleanup,
            )
            persist_terminal(run_root, result)
            _emit_terminal_handoff(
                status,
                verdict="FAIL",
                reason=f"{failure_code}: {failure_message}",
                evidence_root=run_root,
                machine_line=protocol.format_terminal_line(result),
            )
            return EXIT_FAIL

        if actors is None or observations is None or cleanup is None:
            raise AcceptanceFailure(
                "FAIL_EVIDENCE", "live runner returned no terminal evidence"
            )
        result = protocol.make_pass_summary(
            run_id=run_id,
            token=token,
            case_id=args.case,
            head=args.expected_head,
            evidence_root=str(run_root),
            actors=actors,
            observations=observations,
            cleanup=cleanup,
        )
        persist_terminal(run_root, result)
        _emit_terminal_handoff(
            status,
            verdict="PASS",
            reason="manual acceptance passed with terminal cleanup complete",
            evidence_root=run_root,
            machine_line=protocol.format_terminal_line(result),
        )
        return EXIT_PASS
    except KeyboardInterrupt:
        if status is not None:
            status.transition(
                "5/5", "validating completion and cleaning owned resources"
            )
        if owned_project is not None:
            try:
                _remove_owned_unity_project(owned_project)
                owned_project = None
            except protocol.ProtocolFailure:
                pass
        if run_root is None:
            print(
                f"FAIL_INTERRUPTED: interrupted during {interruption_stage}",
                file=sys.stderr,
            )
            return EXIT_FAIL
        _persist_interrupted(
            run_root,
            run_id=run_id,
            token=token,
            case_id=args.case,
            head=args.expected_head,
            stage=interruption_stage,
            status=status,
        )
        return EXIT_FAIL
    except LivePrerequisiteMissing as exc:
        if owned_project is not None:
            try:
                _remove_owned_unity_project(owned_project)
                owned_project = None
            except protocol.ProtocolFailure as cleanup_error:
                print(str(cleanup_error), file=sys.stderr)
                return EXIT_FAIL
        if run_root is None:
            print(str(exc), file=sys.stderr)
            return EXIT_NOT_RUN
        result = persist_not_run(
            run_root,
            run_id=run_id,
            token=token,
            case_id=args.case,
            head=args.expected_head,
            prerequisite=str(exc),
        )
        _emit_terminal_handoff(
            status,
            verdict="NOT RUN",
            reason=str(result["missingPrerequisite"]),
            evidence_root=run_root,
            machine_line=protocol.format_terminal_line(result),
        )
        return EXIT_NOT_RUN
    except protocol.ProtocolFailure as exc:
        if status is not None and run_root is not None:
            failure_cleanup: Mapping[str, Any] = (
                protocol.clean_cleanup_evidence()
            )
            if owned_project is not None:
                project_path = owned_project.path
                try:
                    _remove_owned_unity_project(owned_project)
                    owned_project = None
                except protocol.ProtocolFailure as cleanup_error:
                    failure_cleanup = _record_temporary_project_cleanup_failure(
                        run_root,
                        failure_cleanup,
                        project_path,
                        cleanup_error,
                    )
            _persist_preflight_failure(
                run_root,
                run_id=run_id,
                token=token,
                case_id=args.case,
                head=args.expected_head,
                stage=interruption_stage,
                error=exc,
                cleanup=failure_cleanup,
                status=status,
            )
            return EXIT_FAIL
        if owned_project is not None:
            try:
                _remove_owned_unity_project(owned_project)
                owned_project = None
            except protocol.ProtocolFailure as cleanup_error:
                print(str(cleanup_error), file=sys.stderr)
                return EXIT_FAIL
        print(str(exc), file=sys.stderr)
        return EXIT_FAIL


if __name__ == "__main__":
    raise SystemExit(main())


__all__ = [name for name in globals() if not name.startswith("__")]
