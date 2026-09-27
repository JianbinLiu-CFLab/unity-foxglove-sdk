from __future__ import annotations
from .manual_session_lifecycle import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase184_profile_acceptance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
@dataclass(frozen=True)
class PreparedParentRun:
    """Immutable allocation shared by Batch and manual parent modes."""

    repository: pathlib.Path
    editor: pathlib.Path
    run_id: str
    token: str
    output: pathlib.Path
    config: Mapping[str, object]
    config_path: pathlib.Path


def _prepare_parent_run(
    args: argparse.Namespace,
    execution_mode: str,
) -> PreparedParentRun:
    """Allocate one run and persist a failure record after its root exists."""

    repository = repository_root()
    editor = _require_file(
        args.unity_editor,
        "FAIL_UNITY_STARTUP",
        "Explicit Unity Editor executable",
    )
    run_id, token = _new_run_identity(args.run_id)
    output = _prepare_run_directory(repository, run_id)
    try:
        identity = load_static_interface_identity(repository)
        domain_id = choose_parent_domain_id(args.domain_id, execution_mode)
        foxglove_port = (
            int(args.foxglove_port)
            if args.foxglove_port is not None
            else choose_owned_loopback_port()
        )
        bridge_port = (
            int(args.bridge_port)
            if args.bridge_port is not None
            else choose_owned_loopback_port((foxglove_port,))
        )
        if foxglove_port == bridge_port:
            raise AcceptanceFailure(
                "FAIL_PREFLIGHT",
                "Foxglove and Bridge ports must be distinct.",
            )
        require_available_loopback_port(foxglove_port, "Foxglove")
        require_available_loopback_port(bridge_port, "Bridge")
        profile = str(args.profile)
        peer_workspace = (
            repository / "build" / "phase181" / profile / "peer-workspace"
        )
        config = make_run_config(
            repository=repository,
            run_id=run_id,
            token=token,
            case=str(args.case),
            profile=profile,
            output_root=output,
            domain_id=domain_id,
            foxglove_port=foxglove_port,
            bridge_port=bridge_port,
            phase181_workspace=peer_workspace,
            interface_package=identity.package,
            interface_type=identity.envelope_type,
            interface_digest=identity.digest,
            execution_mode=execution_mode,
        )
        config_path = output / "run-config.json"
        write_private_json_atomic(config_path, config)
        return PreparedParentRun(
            repository=repository,
            editor=editor,
            run_id=run_id,
            token=token,
            output=output,
            config=config,
            config_path=config_path,
        )
    except AcceptanceFailure as exc:
        _write_failure_record(
            output,
            repository,
            case=str(args.case),
            run_id=run_id,
            failure=exc,
        )
        raise
    except Exception as exc:
        failure = AcceptanceFailure(
            "FAIL_PREFLIGHT",
            "Unexpected run allocation failure: " + type(exc).__name__,
        )
        _write_failure_record(
            output,
            repository,
            case=str(args.case),
            run_id=run_id,
            failure=failure,
        )
        raise failure from exc


def run_manual_parent(args: argparse.Namespace) -> int:
    """Own external dependencies while leaving interactive Unity entirely user-owned."""

    if str(args.case) not in {"multi-target", "stream-640hz"}:
        raise AcceptanceFailure(
            "FAIL_PREFLIGHT",
            "Manual Editor mode is limited to the two approved Phase184-G suites.",
        )
    prepared = _prepare_parent_run(args, "manual")
    repository = prepared.repository
    editor = prepared.editor
    run_id = prepared.run_id
    token = prepared.token
    output = prepared.output
    config = prepared.config
    config_path = prepared.config_path
    pointer = repository / "build" / "phase184" / "acceptance" / "manual-active.json"

    stack = contextlib.ExitStack()
    owner: OwnedProcessSet | None = None
    streams: list[TextIO] = []
    runtime: PreparedRosRuntime | None = None
    terminal: TerminalMarker | None = None
    results: dict[str, dict[str, object]] = {}
    process_codes: dict[str, int] = {}
    owner_stopped_roles: frozenset[str] = frozenset()
    cleanup = {"processes": False, "files": False, "junctions": False, "subst": False}
    pointer_written = False
    failure: AcceptanceFailure | None = None
    try:
        _recover_abandoned_manual_pointer(pointer)
        job = WindowsKillOnCloseJob()
        owner = OwnedProcessSet(job)
        _ensure_acceptance_scene(editor, repository, output, job)
        runtime = _prepare_ros_runtime(
            config=config,
            editor=editor,
            repository=repository,
            output=output,
            stack=stack,
            job=job,
        )
        mirror = EditorLogMirror(
            default_unity_editor_log_path(),
            pathlib.Path(str(config["unityLog"])),
            token,
        )
        mirror.capture()
        worker_roles, parent_evidence = _start_case_actors(
            config=config,
            repository=repository,
            output=output,
            runtime=runtime,
            owner=owner,
            streams=streams,
        )
        helper_created = _process_creation_unix_seconds(os.getpid())
        if helper_created is None:
            raise AcceptanceFailure(
                "FAIL_PREFLIGHT",
                "The manual helper process creation time is unavailable.",
            )
        _write_manual_pointer(
            pointer,
            config_path,
            token,
            helper_pid=os.getpid(),
            helper_created=helper_created,
            expires_utc=dt.datetime.now(dt.timezone.utc) + dt.timedelta(hours=1),
        )
        pointer_written = True
        print(manual_play_prompt(str(config["case"])), flush=True)
        terminal = _wait_for_manual_session(
            config,
            mirror,
            owner,
            worker_roles,
        )
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
            "Unexpected manual parent failure: " + type(exc).__name__,
        )
    finally:
        if pointer_written and not _remove_manual_pointer_if_owned(
            pointer,
            token,
            os.getpid(),
        ):
            failure = failure or AcceptanceFailure(
                "FAIL_CLEANUP",
                "The owned manual-active pointer could not be removed.",
            )
        if owner is not None:
            try:
                owner.close()
                process_codes = owner.exit_codes()
                owner_stopped_roles = owner.owner_stopped_roles()
            except Exception:
                failure = failure or AcceptanceFailure(
                    "FAIL_CLEANUP",
                    "The manual process owner could not close every child.",
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
                "The manual short-path cleanup stack failed.",
            )
        if owner is not None:
            try:
                cleanup = _cleanup_evidence(output, owner, subst_roots)
            except Exception:
                failure = failure or AcceptanceFailure(
                    "FAIL_CLEANUP",
                    "The manual cleanup evidence could not be collected.",
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
            "The manual helper reached no current terminal evidence.",
        )
    required = protocol.CASE_CONTRACTS[str(config["case"])].required_actors
    missing = set(required) - set(process_codes)
    unacceptable = {
        role: process_codes[role]
        for role in required & set(process_codes)
        if not process_exit_is_acceptable(
            role,
            process_codes[role],
            owner_requested=role in owner_stopped_roles,
        )
    }
    if missing or unacceptable or not all(cleanup.values()):
        failure = AcceptanceFailure(
            "FAIL_CLEANUP",
            "Manual helper cleanup was incomplete; "
            f"missing={sorted(missing)}, unacceptable={unacceptable}.",
        )
        _write_failure_record(
            output,
            repository,
            case=str(args.case),
            run_id=run_id,
            failure=failure,
        )
        raise failure

    protocol.write_json_atomic(
        output / "manual-evidence.json",
        {
            "schemaVersion": 1,
            "evidenceType": "MANUAL_EVIDENCE",
            "runId": run_id,
            "case": config["case"],
            "profile": config["profile"],
            "tokenSha256": protocol.token_sha256(token),
            "routeTerminal": terminal.verdict,
            "actorEvidenceComplete": set(results) == set(required),
            "processExitCodes": process_codes,
            "processTerminations": {
                role: (
                    "owner_requested"
                    if role in owner_stopped_roles
                    else "self"
                )
                for role in sorted(required)
            },
            "cleanup": cleanup,
            "status": "USER_CONFIRMATION_REQUIRED",
        },
        repo_root=repository,
    )
    print(
        f"PHASE184G_MANUAL_ROUTE_READY case={config['case']} "
        f"evidence={output / 'manual-evidence.json'}",
        flush=True,
    )
    return 0




__all__ = [name for name in globals() if not name.startswith("__")]
