from __future__ import annotations
from .summary_validation import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase186_bridge_acceptance_protocol.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE


def _marker_fields(line: str, prefix: str) -> dict[str, str]:
    """Handle marker fields for Phase186 acceptance."""
    if not isinstance(line, str) or not line.startswith(prefix + " "):
        raise _fail("FAIL_TERMINAL", "terminal marker prefix is absent")
    fields: dict[str, str] = {}
    for part in line[len(prefix) + 1 :].strip().split():
        if "=" not in part:
            raise _fail("FAIL_TERMINAL", "terminal marker field is malformed")
        key, value = part.split("=", 1)
        if not key or not value or key in fields:
            raise _fail("FAIL_TERMINAL", "terminal marker field is empty or duplicated")
        fields[key] = value
    return fields


def format_terminal_line(value: Mapping[str, Any]) -> str:
    """Format terminal line."""
    validated = validate_terminal_summary(value)
    verdict_token = str(validated["verdict"]).replace(" ", "_")
    return (
        TERMINAL_PREFIX
        + verdict_token
        + " run="
        + str(validated["runId"])
        + " case="
        + str(validated["caseId"])
        + " tokenHash="
        + str(validated["tokenHash"])
        + " head="
        + str(validated["head"])
        + " verdict="
        + verdict_token
    )


def parse_terminal_line(line: str, run_id: str, token: str, head: str) -> dict[str, str]:
    """Parse terminal line."""
    require_run_id(run_id)
    require_token(token)
    require_head(head)
    prefix = next(
        (
            candidate
            for candidate in (
                TERMINAL_PREFIX + "PASS",
                TERMINAL_PREFIX + "FAIL",
                TERMINAL_PREFIX + "NOT_RUN",
            )
            if line.startswith(candidate + " ")
        ),
        None,
    )
    if prefix is None:
        raise _fail("FAIL_TERMINAL", "terminal marker verdict prefix is absent")
    fields = _marker_fields(line, prefix)
    expected = {
        "run": run_id,
        "tokenHash": token_sha256(token),
        "head": head,
        "verdict": prefix.removeprefix(TERMINAL_PREFIX),
    }
    if set(fields) != {"run", "case", "tokenHash", "head", "verdict"}:
        raise _fail("FAIL_TERMINAL", "terminal marker fields differ from authority")
    for key, expected_value in expected.items():
        if fields[key] != expected_value:
            raise _fail("FAIL_TERMINAL", f"terminal marker {key} is stale or foreign")
    require_case(fields["case"])
    fields["verdict"] = fields["verdict"].replace("_", " ")
    return fields


def format_manual_completion_marker(
    *, case_id: str, run_id: str, token: str, head: str, verdict: str
) -> str:
    """Format manual completion marker."""
    contract = require_case(case_id)
    if not contract.manual:
        raise _fail("FAIL_PREFLIGHT", "manual completion marker requires a manual case")
    if verdict not in {"PASS", "FAIL"}:
        raise _fail("FAIL_TERMINAL", "manual completion verdict must be PASS or FAIL")
    return (
        f"{MANUAL_COMPLETE_PREFIX} case={contract.case_id} run={require_run_id(run_id)} "
        f"tokenHash={token_sha256(token)} head={require_head(head)} verdict={verdict}"
    )


def parse_manual_completion_marker(
    line: str,
    *,
    case_id: str,
    run_id: str,
    token: str,
    head: str,
) -> dict[str, str]:
    """Parse manual completion marker."""
    contract = require_case(case_id)
    if not contract.manual:
        raise _fail("FAIL_PREFLIGHT", "manual marker parser requires a manual case")
    fields = _marker_fields(line, MANUAL_COMPLETE_PREFIX)
    expected = {
        "case": contract.case_id,
        "run": require_run_id(run_id),
        "tokenHash": token_sha256(token),
        "head": require_head(head),
    }
    if set(fields) != {"case", "run", "tokenHash", "head", "verdict"}:
        raise _fail("FAIL_TERMINAL", "manual marker fields differ from authority")
    for key, expected_value in expected.items():
        if fields[key] != expected_value:
            raise _fail("FAIL_TERMINAL", f"manual marker {key} is stale or foreign")
    if fields["verdict"] not in {"PASS", "FAIL"}:
        raise _fail("FAIL_TERMINAL", "manual marker verdict is invalid")
    return fields


__all__ = [name for name in globals() if not name.startswith("__")]
