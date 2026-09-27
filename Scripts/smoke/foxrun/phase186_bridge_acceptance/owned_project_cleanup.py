from __future__ import annotations
from .evidence_and_run_state import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase186_bridge_acceptance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE


def _preflight(
    repository: pathlib.Path,
    project: pathlib.Path,
    unity_composition: str,
    args: argparse.Namespace,
    run_root: pathlib.Path,
    run_id: str,
    token: str,
    bridge_port: int,
    foxglove_port: int,
) -> dict[str, Any]:
    """Handle preflight for Phase186 acceptance."""
    head = require_exact_head(repository, args.expected_head)
    require_clean_tracked_tree(repository)
    unity = resolve_unity_editor(project, args.unity_editor)
    packages = validate_package_manifests(repository)
    project_packages = validate_unity_project_composition(
        repository,
        project,
        unity_composition,
        run_id,
    )
    authority = validate_static_authority(repository)
    contract = protocol.require_case(args.case)
    runtime_row_id = args.runtime_row or contract.row_id or "jazzy-fastrtps"
    row = protocol.require_row(runtime_row_id)
    domain_id = args.domain_id if args.domain_id is not None else row.domain_id
    config = protocol.make_run_config(
        repository=repository,
        project=project,
        output_root=run_root,
        run_id=run_id,
        token=token,
        case_id=contract.case_id,
        head=head,
        bridge_port=bridge_port,
        foxglove_port=foxglove_port,
        domain_id=domain_id,
        runtime_row_id=row.row_id,
    )
    protocol.validate_run_config(config, repository)
    write_json_atomic(run_root / "run-config.json", config)
    return {
        "schemaVersion": 1,
        "runId": run_id,
        "caseId": contract.case_id,
        "rowId": contract.row_id,
        "runtimeRowId": row.row_id,
        "tokenHash": protocol.token_sha256(token),
        "head": head,
        "unity": {"path": str(unity.path), "version": unity.version},
        "bridgeEndpoint": {"host": "127.0.0.1", "port": bridge_port},
        "foxgloveEndpoint": {"host": "127.0.0.1", "port": foxglove_port},
        "domainId": domain_id,
        "packages": packages,
        "unityComposition": project_packages,
        "authority": authority,
        "verdict": "PREFLIGHT PASS",
        "liveVerdict": "NOT CLAIMED",
        "createdAt": protocol.timestamp(),
    }


def _create_owned_unity_project(
    repository: pathlib.Path,
    run_id: str,
    composition: str,
) -> bridge_project.OwnedBridgeOnlyProject | None:
    """Create owned unity project."""
    if composition == "repository-all-providers":
        return None
    if composition != "bridge-only":
        raise AcceptanceFailure(
            "FAIL_PACKAGE_COMPOSITION", "unknown Unity package composition"
        )
    try:
        return bridge_project.create_bridge_only_project(
            repository,
            run_id,
        )
    except bridge_project.BridgeOnlyProjectFailure as exc:
        raise AcceptanceFailure("FAIL_PACKAGE_COMPOSITION", str(exc)) from exc


def _remove_owned_unity_project(
    owned: bridge_project.OwnedBridgeOnlyProject | None,
) -> None:
    """Remove owned unity project."""
    if owned is None:
        return
    try:
        bridge_project.cleanup_bridge_only_project(owned)
    except bridge_project.BridgeOnlyProjectFailure as exc:
        raise AcceptanceFailure("FAIL_CLEANUP", str(exc)) from exc


def _record_temporary_project_cleanup_failure(
    run_root: pathlib.Path,
    cleanup: Mapping[str, Any] | None,
    project: pathlib.Path,
    error: BaseException,
) -> Mapping[str, Any]:
    """Record temporary project cleanup failure."""
    value = dict(
        cleanup
        or load_cleanup_evidence_if_present(run_root)
        or _incomplete_cleanup_evidence(
            "temporary Unity project cleanup before live cleanup evidence"
        )
    )
    residual = list(value.get("residualTemporaryProjects", []))
    project_text = str(pathlib.Path(project).resolve())
    if project_text not in residual:
        residual.append(project_text)
    errors = list(value.get("cleanupErrors", []))
    errors.append(str(error)[:512] or type(error).__name__)
    value["residualTemporaryProjects"] = residual
    value["cleanupErrors"] = errors
    value["complete"] = False
    write_json_atomic(run_root / "cleanup.json", value)
    return value


def _incomplete_cleanup_evidence(stage: str) -> dict[str, Any]:
    """Describe cleanup that was not reached or observed; never claim it clean."""

    return {
        "complete": False,
        "cleanupErrors": [
            f"interrupted during {str(stage)[:384]}; owned live cleanup was not observed"
        ],
        "residualProcesses": [],
        "residualPorts": [],
        "residualOverlays": [],
        "residualTemporaryProjects": [],
    }


def _emit_terminal_handoff(
    status: Any | None,
    *,
    verdict: str,
    reason: str,
    evidence_root: pathlib.Path,
    machine_line: str,
) -> None:
    """Emit reporter-only human handoff before the stable machine marker."""

    if status is not None:
        status.terminal(
            verdict,
            reason,
            str(pathlib.Path(evidence_root).resolve()),
        )
    print(machine_line, flush=True)


def _persist_preflight_failure(
    run_root: pathlib.Path,
    *,
    run_id: str,
    token: str,
    case_id: str,
    head: str | None,
    stage: str,
    error: protocol.ProtocolFailure,
    cleanup: Mapping[str, Any],
    status: Any,
) -> Mapping[str, Any]:
    """Persist a reporter-mode preflight failure without inventing authority."""

    failure_code = (
        str(error.code)
        if str(error.code).startswith("FAIL_")
        else "FAIL_PREFLIGHT"
    )
    prefix = failure_code + ": "
    failure_message = str(error)
    if failure_message.startswith(prefix):
        failure_message = failure_message[len(prefix) :]
    reason = f"{failure_code}: {failure_message}"
    if head is not None:
        result = protocol.make_failure_summary(
            run_id=run_id,
            token=token,
            case_id=case_id,
            head=head,
            evidence_root=str(run_root),
            failure_code=failure_code,
            failure_message=failure_message,
            cleanup=cleanup,
        )
        persist_terminal(run_root, result)
        _emit_terminal_handoff(
            status,
            verdict="FAIL",
            reason=reason,
            evidence_root=run_root,
            machine_line=protocol.format_terminal_line(result),
        )
        return result

    evidence_path = (run_root / "terminal-preauthority-failure.json").resolve()
    result = {
        "schemaVersion": 1,
        "runId": protocol.require_run_id(run_id),
        "tokenHash": protocol.token_sha256(token),
        "caseId": protocol.require_case(case_id).case_id,
        "head": None,
        "headObserved": False,
        "verdict": "FAIL",
        "failureCode": failure_code,
        "stage": stage,
        "failureMessage": failure_message,
        "cleanup": dict(cleanup),
    }
    write_json_atomic(evidence_path, result)
    machine_line = (
        "PHASE186_PREAUTHORITY_FAIL"
        + f" run={run_id} case={case_id} headObserved=false"
        + f" failureCode={failure_code} evidence={evidence_path}"
    )
    _emit_terminal_handoff(
        status,
        verdict="FAIL",
        reason=reason,
        evidence_root=run_root,
        machine_line=machine_line,
    )
    return result


def _persist_interrupted(
    run_root: pathlib.Path,
    *,
    run_id: str,
    token: str,
    case_id: str,
    head: str | None,
    stage: str,
    cleanup: Mapping[str, Any] | None = None,
    status: Any | None = None,
) -> Mapping[str, Any]:
    """Persist one stable Ctrl+C result with honest cleanup observability."""

    if head is None:
        evidence_path = (run_root / "terminal-interrupted.json").resolve()
        result = {
            "schemaVersion": 1,
            "runId": protocol.require_run_id(run_id),
            "tokenHash": protocol.token_sha256(token),
            "caseId": protocol.require_case(case_id).case_id,
            "head": None,
            "headObserved": False,
            "verdict": "FAIL",
            "failureCode": "FAIL_INTERRUPTED",
            "stage": stage,
            "failureMessage": f"interrupted during {stage}",
            "cleanup": _incomplete_cleanup_evidence(stage),
        }
        write_json_atomic(evidence_path, result)
        machine_line = (
            "PHASE186_INTERRUPTED_FAIL"
            + f" run={run_id} case={case_id} headObserved=false"
            + f" evidence={evidence_path}"
        )
        _emit_terminal_handoff(
            status,
            verdict="FAIL",
            reason=f"FAIL_INTERRUPTED: interrupted during {stage}",
            evidence_root=run_root,
            machine_line=machine_line,
        )
        return result

    result = protocol.make_failure_summary(
        run_id=run_id,
        token=token,
        case_id=case_id,
        head=head,
        evidence_root=str(run_root),
        failure_code="FAIL_INTERRUPTED",
        failure_message=f"interrupted during {stage}",
        cleanup=cleanup or _incomplete_cleanup_evidence(stage),
    )
    persist_terminal(run_root, result)
    _emit_terminal_handoff(
        status,
        verdict="FAIL",
        reason=f"FAIL_INTERRUPTED: interrupted during {stage}",
        evidence_root=run_root,
        machine_line=protocol.format_terminal_line(result),
    )
    return result




__all__ = [name for name in globals() if not name.startswith("__")]
