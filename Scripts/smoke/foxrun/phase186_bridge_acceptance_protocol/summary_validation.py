from __future__ import annotations
from .summary_builders import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase186_bridge_acceptance_protocol.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE


def _require_clean_cleanup(value: Mapping[str, Any]) -> None:
    """Require clean cleanup."""
    if value["complete"] is not True:
        raise _fail("FAIL_CLEANUP", "cleanup is not complete")
    for key in _CLEANUP_KEYS - {"complete"}:
        if value[key]:
            raise _fail("FAIL_CLEANUP", f"cleanup retained {key}")


def _validate_actor(actor: object, logical_role: str) -> None:
    """Validate actor."""
    label = "actor " + logical_role
    if not isinstance(actor, Mapping):
        raise _fail("FAIL_PROCESS_IDENTITY", f"{label} evidence must be an object")
    expected = {
        "pid",
        "executable",
        "started",
        "ready",
        "identityVerified",
        "exited",
        "exitCode",
        "termination",
        "processRole",
        "cohosted",
    }
    _exact_keys(actor, expected, label)
    pid = actor["pid"]
    if isinstance(pid, bool) or not isinstance(pid, int) or pid <= 0:
        raise _fail("FAIL_PROCESS_IDENTITY", f"{label} PID is invalid")
    _absolute_path(actor["executable"], label + " executable")
    for key in ("started", "ready", "identityVerified", "exited"):
        if actor[key] is not True:
            raise _fail("FAIL_PROCESS_IDENTITY", f"{label} did not prove {key}")
    termination = actor["termination"]
    if termination not in {"self", "owner-requested"}:
        raise _fail("FAIL_PROCESS_EXIT", f"{label} termination is invalid")
    exit_code = actor["exitCode"]
    if isinstance(exit_code, bool) or not isinstance(exit_code, int):
        raise _fail("FAIL_PROCESS_EXIT", f"{label} exit code is invalid")
    if termination == "self" and exit_code != 0:
        raise _fail("FAIL_PROCESS_EXIT", f"{label} did not exit zero")
    if termination == "owner-requested" and exit_code not in {
        0,
        1,
        -1073741510,  # CTRL+C / CTRL+BREAK on Windows
        3221225786,  # Unsigned 32-bit STATUS_CONTROL_C_EXIT
        -1073741515,  # loader teardown after owned Job close
        3221225781,  # Unsigned 32-bit STATUS_DLL_NOT_FOUND
    }:
        raise _fail("FAIL_PROCESS_EXIT", f"{label} owned termination is unexpected")
    process_role = actor["processRole"]
    cohosted = actor["cohosted"]
    if not isinstance(process_role, str) or not isinstance(cohosted, bool):
        raise _fail("FAIL_PROCESS_IDENTITY", f"{label} process ownership is invalid")
    if logical_role == "graph-observer":
        if process_role != "ros-peer" or cohosted is not True:
            raise _fail(
                "FAIL_PROCESS_IDENTITY",
                "graph observer must be explicitly cohosted by the ROS peer",
            )
    elif process_role != logical_role or cohosted is not False:
        raise _fail(
            "FAIL_PROCESS_IDENTITY",
            f"{label} cannot alias another process role",
        )


def _validate_observation(value: object, label: str) -> None:
    """Validate observation."""
    if not isinstance(value, Mapping):
        raise _fail("FAIL_EVIDENCE", f"{label} observation must be an object")
    _exact_keys(value, {"observed", "source", "path"}, label)
    if value["observed"] is not True:
        raise _fail("FAIL_EVIDENCE", f"{label} was not observed")
    source = value["source"]
    if (
        not isinstance(source, str)
        or not source
        or source.lower() in _FORBIDDEN_OBSERVATION_SOURCES
    ):
        raise _fail("FAIL_EVIDENCE", f"{label} uses non-live or forbidden evidence")
    _absolute_path(value["path"], label + " path")


def validate_terminal_summary(value: Mapping[str, Any]) -> Mapping[str, Any]:
    """Validate terminal evidence without promoting any weaker result to PASS."""

    if not isinstance(value, Mapping):
        raise _fail("FAIL_PROTOCOL", "terminal summary must be an object")
    _exact_keys(value, _TERMINAL_KEYS, "terminal summary")
    if value["schemaVersion"] != TERMINAL_SCHEMA_VERSION:
        raise _fail("FAIL_PROTOCOL", "unsupported terminal schema")
    require_run_id(value["runId"])
    if (
        not isinstance(value["tokenHash"], str)
        or _SHA256.fullmatch(value["tokenHash"]) is None
        or len(set(value["tokenHash"])) == 1
    ):
        raise _fail("FAIL_PROTOCOL", "terminal token hash is invalid")
    contract = require_case(value["caseId"])
    if value["rowId"] != contract.row_id:
        raise _fail("FAIL_RUNTIME_SELECTION", "terminal row differs from case authority")
    require_head(value["head"])
    _absolute_path(value["evidenceRoot"], "evidenceRoot")
    if value["verdict"] not in {"PASS", "FAIL", "NOT RUN"}:
        raise _fail("FAIL_PROTOCOL", "terminal verdict is unknown")
    for label in ("startedAt", "finishedAt"):
        if not isinstance(value[label], str) or not value[label]:
            raise _fail("FAIL_PROTOCOL", f"{label} is absent")
    _validate_cleanup_shape(value["cleanup"])

    if value["verdict"] == "NOT RUN":
        missing = value["missingPrerequisite"]
        if not isinstance(missing, str) or not missing.strip() or len(missing) > 512:
            raise _fail("FAIL_PREFLIGHT", "NOT RUN lacks one bounded prerequisite")
        if value["actors"] or value["observations"]:
            raise _fail("FAIL_PROTOCOL", "NOT RUN cannot carry synthetic live evidence")
        if value["failureCode"] is not None or value["failureMessage"] is not None:
            raise _fail("FAIL_PROTOCOL", "NOT RUN cannot carry failure fields")
        _require_clean_cleanup(value["cleanup"])
        return value

    if value["missingPrerequisite"] is not None:
        raise _fail("FAIL_PROTOCOL", "only NOT RUN may name a missing prerequisite")
    if value["verdict"] == "FAIL":
        if (
            not isinstance(value["failureCode"], str)
            or re.fullmatch(r"FAIL_[A-Z0-9_]{2,64}", value["failureCode"]) is None
            or not isinstance(value["failureMessage"], str)
            or not value["failureMessage"].strip()
            or len(value["failureMessage"]) > 512
        ):
            raise _fail("FAIL_PROTOCOL", "FAIL lacks bounded failure details")
        if not isinstance(value["actors"], Mapping) or not isinstance(value["observations"], Mapping):
            raise _fail("FAIL_PROTOCOL", "FAIL evidence sections must be objects")
        return value

    if value["failureCode"] is not None or value["failureMessage"] is not None:
        raise _fail("FAIL_PROTOCOL", "PASS cannot carry failure fields")
    _require_clean_cleanup(value["cleanup"])

    actors = value["actors"]
    if not isinstance(actors, Mapping) or set(actors) != set(contract.required_actors):
        raise _fail("FAIL_PROCESS_IDENTITY", "PASS actor set differs from case authority")
    for actor_name, actor in actors.items():
        _validate_actor(actor, actor_name)
    if "graph-observer" in actors:
        graph_actor = actors["graph-observer"]
        peer_actor = actors.get("ros-peer")
        if not isinstance(peer_actor, Mapping):
            raise _fail(
                "FAIL_PROCESS_IDENTITY",
                "cohosted graph observer lacks its ROS peer process",
            )
        shared_identity = {
            "pid",
            "executable",
            "started",
            "ready",
            "identityVerified",
            "exited",
            "exitCode",
            "termination",
        }
        if any(graph_actor[key] != peer_actor[key] for key in shared_identity):
            raise _fail(
                "FAIL_PROCESS_IDENTITY",
                "cohosted graph observer differs from its ROS peer process",
            )
    observations = value["observations"]
    if not isinstance(observations, Mapping) or set(observations) != set(contract.required_observations):
        raise _fail("FAIL_EVIDENCE", "PASS observation set differs from case authority")
    for name, observation in observations.items():
        _validate_observation(observation, name)
    return value


def verdict_exit_code(value: Mapping[str, Any] | str) -> int:
    """Handle verdict exit code for Phase186 acceptance."""
    verdict = value.get("verdict") if isinstance(value, Mapping) else value
    return {"PASS": 0, "FAIL": 1, "NOT RUN": 3}.get(str(verdict), 2)


__all__ = [name for name in globals() if not name.startswith("__")]
