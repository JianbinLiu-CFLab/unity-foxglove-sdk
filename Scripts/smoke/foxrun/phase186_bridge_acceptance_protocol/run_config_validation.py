from __future__ import annotations
from .configuration_and_contracts import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase186_bridge_acceptance_protocol.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE


def make_run_config(
    *,
    repository: pathlib.Path,
    project: pathlib.Path,
    output_root: pathlib.Path,
    run_id: str,
    token: str,
    case_id: str,
    head: str,
    bridge_port: int,
    domain_id: int,
    foxglove_port: int = 8765,
    runtime_row_id: str | None = None,
) -> dict[str, Any]:
    """Build one immutable run authority object."""

    contract = require_case(case_id)
    if contract.row_id is not None:
        row = require_row(contract.row_id)
        if runtime_row_id is not None and runtime_row_id != row.row_id:
            raise _fail(
                "FAIL_RUNTIME_SELECTION",
                "manual case runtime row differs from case authority",
            )
    else:
        row = require_row(runtime_row_id) if runtime_row_id is not None else None
    return {
        "schemaVersion": RUN_CONFIG_SCHEMA_VERSION,
        "runId": require_run_id(run_id),
        "token": require_token(token),
        "tokenHash": token_sha256(token),
        "caseId": contract.case_id,
        "rowId": contract.row_id,
        "runtimeRowId": row.row_id if row is not None else None,
        "distro": row.distro if row is not None else None,
        "rmw": row.rmw if row is not None else None,
        "manual": contract.manual,
        "head": require_head(head),
        "repository": str(pathlib.Path(repository).resolve()),
        "projectPath": str(pathlib.Path(project).resolve()),
        "outputRoot": str(pathlib.Path(output_root).resolve()),
        "bridgeHost": "127.0.0.1",
        "bridgePort": bridge_port,
        "foxgloveHost": "127.0.0.1",
        "foxglovePort": foxglove_port,
        "domainId": domain_id,
        "interfaceType": INTERFACE_TYPE,
        "interfaceDigest": INTERFACE_DIGEST,
        "topics": list(topics_for_case(case_id, token)),
        "requiredActors": sorted(contract.required_actors),
        "unityLog": str((pathlib.Path(output_root) / "unity.log").resolve()),
        "externalGate": str(
            (pathlib.Path(output_root) / "unity-external-gate.json").resolve()
        ),
        "exerciseGate": str(
            (pathlib.Path(output_root) / "unity-exercise-gate.json").resolve()
        ),
        "createdAt": timestamp(),
    }


def validate_run_config(value: Mapping[str, Any], repository: pathlib.Path) -> Mapping[str, Any]:
    """Validate current-run authority before any actor starts."""

    if not isinstance(value, Mapping):
        raise _fail("FAIL_PROTOCOL", "run config must be an object")
    _exact_keys(value, _RUN_CONFIG_KEYS, "run config")
    if value["schemaVersion"] != RUN_CONFIG_SCHEMA_VERSION:
        raise _fail("FAIL_PROTOCOL", "unsupported run config schema")
    run_id = require_run_id(value["runId"])
    token = require_token(value["token"])
    if value["tokenHash"] != token_sha256(token):
        raise _fail("FAIL_PROTOCOL", "run config token hash differs")
    contract = require_case(value["caseId"])
    if value["rowId"] != contract.row_id:
        raise _fail("FAIL_RUNTIME_SELECTION", "run config row differs from case authority")
    runtime_row_id = value["runtimeRowId"]
    if contract.row_id is not None:
        if runtime_row_id != contract.row_id:
            raise _fail(
                "FAIL_RUNTIME_SELECTION",
                "manual run config runtime row differs from case authority",
            )
        row = require_row(runtime_row_id)
    else:
        row = require_row(runtime_row_id) if runtime_row_id is not None else None
    if row is None:
        if value["distro"] is not None or value["rmw"] is not None:
            raise _fail(
                "FAIL_RUNTIME_SELECTION",
                "row-independent preflight cannot retain ROS/RMW aliases",
            )
    else:
        if value["distro"] != row.distro or value["rmw"] != row.rmw:
            raise _fail("FAIL_RUNTIME_SELECTION", "run config ROS/RMW differs from row authority")
    if value["manual"] is not contract.manual:
        raise _fail("FAIL_PROTOCOL", "run config execution mode differs from case authority")
    require_head(value["head"])
    root = pathlib.Path(repository).resolve()
    if _absolute_path(value["repository"], "repository") != root:
        raise _fail("FAIL_PREFLIGHT", "run config repository differs from current repository")
    project = _absolute_path(value["projectPath"], "projectPath")
    output = _absolute_path(value["outputRoot"], "outputRoot")
    owned_root = (root / "build" / "phase186").resolve()
    if not _is_below(output, owned_root) or output.name != run_id:
        raise _fail("FAIL_PREFLIGHT", "run output is not the exact owned Phase186 run directory")
    repository_project = (root / "Unity2Foxglove").resolve()
    bridge_only_project = owned_unity_project_path(root, run_id)
    if project not in {repository_project, bridge_only_project}:
        raise _fail(
            "FAIL_PREFLIGHT",
            "run config project is neither the repository project nor its exact owned Bridge-only project",
        )
    if value["bridgeHost"] != "127.0.0.1":
        raise _fail("FAIL_PREFLIGHT", "Bridge must use IPv4 loopback")
    port = value["bridgePort"]
    foxglove_port = value["foxglovePort"]
    domain = value["domainId"]
    if isinstance(port, bool) or not isinstance(port, int) or not 1 <= port <= 65535:
        raise _fail("FAIL_PREFLIGHT", "Bridge port is outside 1..65535")
    if value["foxgloveHost"] != "127.0.0.1":
        raise _fail("FAIL_PREFLIGHT", "Foxglove must use IPv4 loopback")
    if (
        isinstance(foxglove_port, bool)
        or not isinstance(foxglove_port, int)
        or not 1 <= foxglove_port <= 65535
        or foxglove_port == port
    ):
        raise _fail("FAIL_PREFLIGHT", "Foxglove port is invalid or collides with Bridge")
    if (
        isinstance(domain, bool)
        or not isinstance(domain, int)
        or not 0 <= domain <= WINDOWS_SAFE_ROS_DOMAIN_ID_MAX
    ):
        raise _fail("FAIL_PREFLIGHT", "Windows ROS domain ID is outside 0..166")
    if value["interfaceType"] != INTERFACE_TYPE or value["interfaceDigest"] != INTERFACE_DIGEST:
        raise _fail("FAIL_PREFLIGHT", "Phase181 interface identity differs from authority")
    if tuple(value["topics"]) != topics_for_case(contract.case_id, token):
        raise _fail("FAIL_PREFLIGHT", "run topic set differs from current token authority")
    if tuple(value["requiredActors"]) != tuple(sorted(contract.required_actors)):
        raise _fail("FAIL_PREFLIGHT", "run actor set differs from case authority")
    expected_unity_log = (output / "unity.log").resolve()
    expected_gate = (output / "unity-external-gate.json").resolve()
    expected_exercise_gate = (output / "unity-exercise-gate.json").resolve()
    if _absolute_path(value["unityLog"], "unityLog") != expected_unity_log:
        raise _fail("FAIL_PREFLIGHT", "Unity log path differs from run authority")
    if _absolute_path(value["externalGate"], "externalGate") != expected_gate:
        raise _fail("FAIL_PREFLIGHT", "Unity external gate path differs from run authority")
    if _absolute_path(value["exerciseGate"], "exerciseGate") != expected_exercise_gate:
        raise _fail("FAIL_PREFLIGHT", "Unity exercise gate path differs from run authority")
    return value


_CLEANUP_KEYS = {
    "complete",
    "cleanupErrors",
    "residualProcesses",
    "residualPorts",
    "residualOverlays",
    "residualTemporaryProjects",
}


def clean_cleanup_evidence() -> dict[str, Any]:
    """Clean cleanup evidence."""
    return {
        "complete": True,
        "cleanupErrors": [],
        "residualProcesses": [],
        "residualPorts": [],
        "residualOverlays": [],
        "residualTemporaryProjects": [],
    }


_TERMINAL_KEYS = {
    "schemaVersion",
    "runId",
    "tokenHash",
    "caseId",
    "rowId",
    "head",
    "verdict",
    "evidenceRoot",
    "startedAt",
    "finishedAt",
    "missingPrerequisite",
    "failureCode",
    "failureMessage",
    "actors",
    "observations",
    "cleanup",
}


__all__ = [name for name in globals() if not name.startswith("__")]
