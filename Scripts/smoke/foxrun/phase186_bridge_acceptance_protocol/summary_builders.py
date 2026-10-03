from __future__ import annotations
from .run_config_validation import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase186_bridge_acceptance_protocol.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE


def _base_terminal(
    *, run_id: str, token: str, case_id: str, head: str, evidence_root: str
) -> dict[str, Any]:
    """Handle base terminal for Phase186 acceptance."""
    contract = require_case(case_id)
    now = timestamp()
    return {
        "schemaVersion": TERMINAL_SCHEMA_VERSION,
        "runId": require_run_id(run_id),
        "tokenHash": token_sha256(token),
        "caseId": contract.case_id,
        "rowId": contract.row_id,
        "head": require_head(head),
        "verdict": "FAIL",
        "evidenceRoot": str(evidence_root),
        "startedAt": now,
        "finishedAt": now,
        "missingPrerequisite": None,
        "failureCode": None,
        "failureMessage": None,
        "actors": {},
        "observations": {},
        "cleanup": clean_cleanup_evidence(),
    }


def make_not_run_summary(
    *,
    run_id: str,
    token: str,
    case_id: str,
    head: str,
    prerequisite: str,
    evidence_root: str,
) -> dict[str, Any]:
    """Create a blocking, machine-readable missing-prerequisite result."""

    if not isinstance(prerequisite, str) or not prerequisite.strip() or len(prerequisite) > 512:
        raise _fail("FAIL_PREFLIGHT", "NOT RUN requires one bounded named prerequisite")
    result = _base_terminal(
        run_id=run_id,
        token=token,
        case_id=case_id,
        head=head,
        evidence_root=evidence_root,
    )
    result["verdict"] = "NOT RUN"
    result["missingPrerequisite"] = prerequisite.strip()
    validate_terminal_summary(result)
    return result


def make_failure_summary(
    *,
    run_id: str,
    token: str,
    case_id: str,
    head: str,
    evidence_root: str,
    failure_code: str,
    failure_message: str,
    cleanup: Mapping[str, Any] | None = None,
    actors: Mapping[str, Any] | None = None,
    observations: Mapping[str, Any] | None = None,
) -> dict[str, Any]:
    """Create a terminal failure which can honestly retain cleanup residue."""

    if (
        not isinstance(failure_code, str)
        or re.fullmatch(r"FAIL_[A-Z0-9_]{2,64}", failure_code) is None
    ):
        raise _fail("FAIL_PROTOCOL", "failure code is malformed")
    if (
        not isinstance(failure_message, str)
        or not failure_message.strip()
        or len(failure_message) > 512
    ):
        raise _fail("FAIL_PROTOCOL", "failure message is empty or unbounded")
    result = _base_terminal(
        run_id=run_id,
        token=token,
        case_id=case_id,
        head=head,
        evidence_root=evidence_root,
    )
    result["failureCode"] = failure_code
    result["failureMessage"] = failure_message.strip()
    if cleanup is not None:
        result["cleanup"] = deep_copy_json(cleanup)
    if actors is not None:
        result["actors"] = deep_copy_json(actors)
    if observations is not None:
        result["observations"] = deep_copy_json(observations)
    validate_terminal_summary(result)
    return result


def _actor_for_tests(index: int, logical_role: str) -> dict[str, Any]:
    """Handle actor for tests for Phase186 acceptance."""
    return {
        "pid": 1000 + index,
        "executable": str(_SYNTHETIC_EVIDENCE_ROOT / f"actor{index}.exe"),
        "started": True,
        "ready": True,
        "identityVerified": True,
        "exited": True,
        "exitCode": 0,
        "termination": "self",
        "processRole": logical_role,
        "cohosted": False,
    }


def make_pass_summary_for_tests(
    *, run_id: str, token: str, case_id: str, head: str, evidence_root: str
) -> dict[str, Any]:
    """Build a structurally complete synthetic object for pure validator tests."""

    result = _base_terminal(
        run_id=run_id,
        token=token,
        case_id=case_id,
        head=head,
        evidence_root=evidence_root,
    )
    contract = require_case(case_id)
    result["verdict"] = "PASS"
    actors = {
        actor: _actor_for_tests(index, actor)
        for index, actor in enumerate(sorted(contract.required_actors), start=1)
    }
    if "graph-observer" in actors and "ros-peer" in actors:
        actors["graph-observer"] = deep_copy_json(actors["ros-peer"])
        actors["graph-observer"]["processRole"] = "ros-peer"
        actors["graph-observer"]["cohosted"] = True
    result["actors"] = actors
    result["observations"] = {
        name: {
            "observed": True,
            "source": "live",
            "path": str(_SYNTHETIC_EVIDENCE_ROOT / f"{name}.json"),
        }
        for name in sorted(contract.required_observations)
    }
    return make_pass_summary(
        run_id=run_id,
        token=token,
        case_id=case_id,
        head=head,
        evidence_root=evidence_root,
        actors=result["actors"],
        observations=result["observations"],
        cleanup=result["cleanup"],
    )


def make_pass_summary(
    *,
    run_id: str,
    token: str,
    case_id: str,
    head: str,
    evidence_root: str,
    actors: Mapping[str, Any],
    observations: Mapping[str, Any],
    cleanup: Mapping[str, Any],
) -> dict[str, Any]:
    """Create a live PASS only from complete exact actor/observation evidence."""

    result = _base_terminal(
        run_id=run_id,
        token=token,
        case_id=case_id,
        head=head,
        evidence_root=evidence_root,
    )
    result["verdict"] = "PASS"
    result["actors"] = deep_copy_json(actors)
    result["observations"] = deep_copy_json(observations)
    result["cleanup"] = deep_copy_json(cleanup)
    validate_terminal_summary(result)
    return result


def _validate_cleanup_shape(value: object) -> None:
    """Validate cleanup shape."""
    if not isinstance(value, Mapping):
        raise _fail("FAIL_CLEANUP", "cleanup evidence must be an object")
    _exact_keys(value, _CLEANUP_KEYS, "cleanup")
    if not isinstance(value["complete"], bool):
        raise _fail("FAIL_CLEANUP", "cleanup complete flag must be boolean")
    for key in _CLEANUP_KEYS - {"complete"}:
        if not isinstance(value[key], list) or len(value[key]) > 256:
            raise _fail("FAIL_CLEANUP", f"cleanup {key} is invalid or unbounded")


__all__ = [name for name in globals() if not name.startswith("__")]
