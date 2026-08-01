#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Pure, fail-closed evidence protocol for Phase186-H acceptance.

This module deliberately performs no process launch and imports no Unity or ROS
runtime.  It is the shared authority used by the parent coordinator, workers,
Unity-log parser, CI selector, and regression tests.
"""

from __future__ import annotations

import copy
import datetime as _datetime
import hashlib
import json
import pathlib
import re
from dataclasses import dataclass
from types import MappingProxyType
from typing import Any, Mapping


RUN_CONFIG_SCHEMA_VERSION = 3
TERMINAL_SCHEMA_VERSION = 1
INTERFACE_TYPE = (
    "unity2foxglove_foxrun_interfaces_v1/msg/"
    "Phase181State48D288ED82F1Envelope"
)
INTERFACE_DIGEST = (
    "120864853239fae290b5199cd02dbf02f107299bccd8972b06d8cf59fc7594fd"
)
TERMINAL_PREFIX = "PHASE186_ACCEPTANCE_"
MANUAL_COMPLETE_PREFIX = "PHASE186_MANUAL_COMPLETE"
MANUAL_READY_PREFIX = "PHASE186_MANUAL_READY"
COORDINATOR_UNITY_READY_TIMEOUT_SECONDS = 240.0
# Workers are launched before Unity so the coordinator can prove actor
# ownership. Their readiness budget must outlive the coordinator's first-import
# budget; otherwise a valid slow Unity import can kill the witnesses first.
ACTOR_UNITY_READY_TIMEOUT_SECONDS = (
    COORDINATOR_UNITY_READY_TIMEOUT_SECONDS + 60.0
)

_HEAD = re.compile(r"\A[0-9a-f]{40}\Z")
_SHA256 = re.compile(r"\A[0-9a-f]{64}\Z")
_RUN_ID = re.compile(r"\Aphase186h-[A-Za-z0-9][A-Za-z0-9._-]{11,79}\Z")
_TOKEN = re.compile(r"\Ap186h_[A-Za-z0-9]{24,64}\Z")
_CASE_ID = re.compile(r"\A[a-z0-9]+(?:-[a-z0-9]+)*\Z")
_TOPIC = re.compile(r"\A/(?:[A-Za-z_][A-Za-z0-9_]*/)*[A-Za-z_][A-Za-z0-9_]*\Z")
_FORBIDDEN_OBSERVATION_SOURCES = frozenset(
    {"cached", "configuration", "unit-test", "skipped", "fixture"}
)


class ProtocolFailure(RuntimeError):
    """A stable, bounded acceptance protocol failure."""

    def __init__(self, code: str, message: str):
        self.code = str(code)
        super().__init__(f"{self.code}: {message}")


def _fail(code: str, message: str) -> ProtocolFailure:
    return ProtocolFailure(code, message)


@dataclass(frozen=True)
class RowContract:
    """One exact Windows ROS/RMW row."""

    row_id: str
    distro: str
    rmw: str
    domain_id: int


@dataclass(frozen=True)
class CaseContract:
    """One immutable acceptance case and its actor/evidence contract."""

    case_id: str
    row_id: str | None
    manual: bool
    required_actors: frozenset[str]
    topic_stems: tuple[str, ...]
    required_observations: frozenset[str]


ROWS: Mapping[str, RowContract] = MappingProxyType(
    {
        "humble-fastrtps": RowContract(
            "humble-fastrtps", "humble", "rmw_fastrtps_cpp", 186
        ),
        "jazzy-fastrtps": RowContract(
            "jazzy-fastrtps", "jazzy", "rmw_fastrtps_cpp", 187
        ),
        "lyrical-fastrtps": RowContract(
            "lyrical-fastrtps", "lyrical", "rmw_fastrtps_cpp", 188
        ),
        "lyrical-zenoh": RowContract(
            "lyrical-zenoh", "lyrical", "rmw_zenoh_cpp", 189
        ),
    }
)

_LIVE_OBSERVATIONS = frozenset(
    {
        "unity",
        "bridge",
        "peer",
        "graph",
        "qos",
        "data",
        "origin",
        "resources",
        "packages",
    }
)

AUTOMATIC_CASE_IDS = (
    "frozen-v1",
    "bridge-source",
    "full-duplex",
    "fanout-fairness-health",
    "reconnect-degraded-recovery",
    "bounds-hostile-peer",
    "lifecycle",
    "slow-main-thread-640hz",
    "product-inspector",
)
MANUAL_CASE_IDS = (
    "manual-jazzy-fastrtps-duplex",
    "manual-lyrical-zenoh-duplex",
)


def _case(
    case_id: str,
    *,
    row_id: str | None,
    manual: bool,
    actors: set[str],
    topics: tuple[str, ...],
    observations: set[str] | None = None,
) -> CaseContract:
    return CaseContract(
        case_id=case_id,
        row_id=row_id,
        manual=manual,
        required_actors=frozenset(actors),
        topic_stems=topics,
        required_observations=(
            _LIVE_OBSERVATIONS
            if observations is None
            else frozenset(observations)
        ),
    )


CASES: Mapping[str, CaseContract] = MappingProxyType(
    {
        "frozen-v1": _case(
            "frozen-v1",
            row_id=None,
            manual=False,
            actors={"sidecar", "wire-peer"},
            topics=("frozen_publish", "frozen_health"),
            observations={"bridge", "peer", "data", "resources", "packages"},
        ),
        "bridge-source": _case(
            "bridge-source",
            row_id=None,
            manual=False,
            actors={"unity", "sidecar", "ros-peer", "graph-observer"},
            topics=("standard_source", "phase181_source"),
        ),
        "full-duplex": _case(
            "full-duplex",
            row_id=None,
            manual=False,
            actors={"unity", "sidecar", "ros-peer", "graph-observer"},
            topics=("duplex_state",),
        ),
        "fanout-fairness-health": _case(
            "fanout-fairness-health",
            row_id=None,
            manual=False,
            actors={
                "unity",
                "sidecar",
                "ros-peer",
                "graph-observer",
                "foxglove-client",
            },
            topics=("fanout_hot", "fanout_cold", "fanout_failed"),
        ),
        "reconnect-degraded-recovery": _case(
            "reconnect-degraded-recovery",
            row_id=None,
            manual=False,
            actors={"unity", "sidecar", "ros-peer", "graph-observer"},
            topics=("reconnect_state", "released_state"),
        ),
        "bounds-hostile-peer": _case(
            "bounds-hostile-peer",
            row_id=None,
            manual=False,
            actors={"unity", "hostile-peer", "sidecar"},
            topics=("hostile_control", "hostile_payload"),
            observations={"unity", "bridge", "peer", "data", "resources", "packages"},
        ),
        "lifecycle": _case(
            "lifecycle",
            row_id=None,
            manual=False,
            actors={"unity", "sidecar", "ros-peer", "graph-observer"},
            topics=("lifecycle_state",),
        ),
        "slow-main-thread-640hz": _case(
            "slow-main-thread-640hz",
            row_id=None,
            manual=False,
            actors={"unity", "sidecar", "ros-peer", "graph-observer"},
            topics=("slow_ingress", "slow_control"),
        ),
        "product-inspector": _case(
            "product-inspector",
            row_id=None,
            manual=False,
            actors={"unity", "sidecar"},
            topics=("product_publish", "product_subscribe"),
            observations={"unity", "resources", "packages"},
        ),
        "manual-jazzy-fastrtps-duplex": _case(
            "manual-jazzy-fastrtps-duplex",
            row_id="jazzy-fastrtps",
            manual=True,
            actors={"sidecar", "ros-peer", "graph-observer"},
            topics=("manual_duplex", "manual_standard", "manual_slow"),
        ),
        "manual-lyrical-zenoh-duplex": _case(
            "manual-lyrical-zenoh-duplex",
            row_id="lyrical-zenoh",
            manual=True,
            actors={"sidecar", "ros-peer", "graph-observer", "zenoh-router"},
            topics=("manual_duplex", "manual_standard", "manual_slow"),
        ),
    }
)


# One shared declaration authority is consumed by the generated Unity partial,
# the external ROS peer, and the graph observer.  Keeping this beside CASES
# prevents the three live actors from silently testing different contracts.
CASE_CONTRACT_KINDS: Mapping[str, tuple[str, ...]] = MappingProxyType(
    {
        "frozen-v1": ("standard_publish", "standard_publish"),
        "bridge-source": ("standard_subscribe", "custom_subscribe"),
        "full-duplex": ("custom_duplex",),
        "fanout-fairness-health": (
            "standard_publish",
            "standard_publish",
            "standard_publish",
        ),
        "reconnect-degraded-recovery": ("custom_duplex", "standard_subscribe"),
        "bounds-hostile-peer": ("standard_publish", "custom_publish"),
        "lifecycle": ("custom_duplex",),
        "slow-main-thread-640hz": ("custom_subscribe", "standard_duplex"),
        "product-inspector": ("standard_publish", "standard_publish"),
        "manual-jazzy-fastrtps-duplex": (
            "custom_duplex",
            "standard_duplex",
            "custom_subscribe",
        ),
        "manual-lyrical-zenoh-duplex": (
            "custom_duplex",
            "standard_duplex",
            "custom_subscribe",
        ),
    }
)

if set(CASE_CONTRACT_KINDS) != set(CASES) or any(
    len(CASE_CONTRACT_KINDS[case_id]) != len(contract.topic_stems)
    for case_id, contract in CASES.items()
):  # pragma: no cover - import-time authority invariant.
    raise RuntimeError("Phase186 case declarations differ from topic authority")


def timestamp() -> str:
    """Return one bounded ISO-8601 timestamp."""

    return _datetime.datetime.now().astimezone().isoformat(timespec="milliseconds")


def token_sha256(token: str) -> str:
    """Hash a run token so durable evidence need not expose it."""

    require_token(token)
    return hashlib.sha256(token.encode("utf-8")).hexdigest()


def deep_copy_json(value: Any) -> Any:
    """Copy JSON-compatible evidence without preserving shared state."""

    return json.loads(json.dumps(value))


def require_head(value: object) -> str:
    if not isinstance(value, str) or _HEAD.fullmatch(value) is None:
        raise _fail("FAIL_PREFLIGHT", "feature HEAD must be a full lowercase Git SHA-1")
    return value


def require_run_id(value: object) -> str:
    if not isinstance(value, str) or _RUN_ID.fullmatch(value) is None:
        raise _fail("FAIL_PREFLIGHT", "run ID is unsafe or outside the fixed bound")
    return value


def require_token(value: object) -> str:
    if not isinstance(value, str) or _TOKEN.fullmatch(value) is None:
        raise _fail("FAIL_PREFLIGHT", "run token is unsafe or outside the fixed bound")
    return value


def require_case(case_id: object) -> CaseContract:
    if not isinstance(case_id, str) or _CASE_ID.fullmatch(case_id) is None:
        raise _fail("FAIL_PREFLIGHT", "case ID is malformed")
    contract = CASES.get(case_id)
    if contract is None:
        raise _fail("FAIL_PREFLIGHT", f"unknown Phase186-H case {case_id!r}")
    return contract


def require_row(row_id: object) -> RowContract:
    if not isinstance(row_id, str) or row_id not in ROWS:
        raise _fail("FAIL_RUNTIME_SELECTION", "row must be one exact maintained ROS/RMW row")
    return ROWS[row_id]


def topics_for_case(case_id: str, token: str) -> tuple[str, ...]:
    """Return unique current-run topics that cannot overlap older phases."""

    contract = require_case(case_id)
    safe_token = require_token(token)
    topics = tuple(
        f"/foxrun/phase186/{safe_token}/{stem}" for stem in contract.topic_stems
    )
    if len(topics) != len(set(topics)) or any(_TOPIC.fullmatch(topic) is None for topic in topics):
        raise _fail("FAIL_PREFLIGHT", "generated topic set is malformed or duplicated")
    return topics


def _absolute_path(value: object, label: str) -> pathlib.Path:
    if not isinstance(value, str) or not value:
        raise _fail("FAIL_PREFLIGHT", f"{label} must be a non-empty absolute path")
    path = pathlib.Path(value)
    if not path.is_absolute():
        raise _fail("FAIL_PREFLIGHT", f"{label} must be absolute")
    try:
        return path.resolve(strict=False)
    except OSError as exc:
        raise _fail("FAIL_PREFLIGHT", f"{label} cannot be resolved: {exc}") from exc


def _is_below(path: pathlib.Path, parent: pathlib.Path) -> bool:
    return path != parent and parent in path.parents


def _exact_keys(value: Mapping[str, Any], expected: set[str], label: str) -> None:
    actual = set(value)
    if actual != expected:
        raise _fail(
            "FAIL_PROTOCOL",
            f"{label} keys differ; missing={sorted(expected - actual)}, "
            f"unexpected={sorted(actual - expected)}",
        )


_RUN_CONFIG_KEYS = {
    "schemaVersion",
    "runId",
    "token",
    "tokenHash",
    "caseId",
    "rowId",
    "runtimeRowId",
    "distro",
    "rmw",
    "manual",
    "head",
    "repository",
    "projectPath",
    "outputRoot",
    "bridgeHost",
    "bridgePort",
    "foxgloveHost",
    "foxglovePort",
    "domainId",
    "interfaceType",
    "interfaceDigest",
    "topics",
    "requiredActors",
    "unityLog",
    "externalGate",
    "exerciseGate",
    "createdAt",
}


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
    bridge_only_project = (output / "bridge-only-unity").resolve()
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
    if isinstance(domain, bool) or not isinstance(domain, int) or not 0 <= domain <= 232:
        raise _fail("FAIL_PREFLIGHT", "ROS domain ID is outside 0..232")
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


def _base_terminal(
    *, run_id: str, token: str, case_id: str, head: str, evidence_root: str
) -> dict[str, Any]:
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


def _actor_for_tests(index: int) -> dict[str, Any]:
    return {
        "pid": 1000 + index,
        "executable": rf"C:\owned\actor{index}.exe",
        "started": True,
        "ready": True,
        "identityVerified": True,
        "exited": True,
        "exitCode": 0,
        "termination": "self",
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
    result["actors"] = {
        actor: _actor_for_tests(index)
        for index, actor in enumerate(sorted(contract.required_actors), start=1)
    }
    result["observations"] = {
        name: {
            "observed": True,
            "source": "live",
            "path": rf"C:\evidence\{name}.json",
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
    if not isinstance(value, Mapping):
        raise _fail("FAIL_CLEANUP", "cleanup evidence must be an object")
    _exact_keys(value, _CLEANUP_KEYS, "cleanup")
    if not isinstance(value["complete"], bool):
        raise _fail("FAIL_CLEANUP", "cleanup complete flag must be boolean")
    for key in _CLEANUP_KEYS - {"complete"}:
        if not isinstance(value[key], list) or len(value[key]) > 256:
            raise _fail("FAIL_CLEANUP", f"cleanup {key} is invalid or unbounded")


def _require_clean_cleanup(value: Mapping[str, Any]) -> None:
    if value["complete"] is not True:
        raise _fail("FAIL_CLEANUP", "cleanup is not complete")
    for key in _CLEANUP_KEYS - {"complete"}:
        if value[key]:
            raise _fail("FAIL_CLEANUP", f"cleanup retained {key}")


def _validate_actor(actor: object, label: str) -> None:
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


def _validate_observation(value: object, label: str) -> None:
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
        _validate_actor(actor, "actor " + actor_name)
    observations = value["observations"]
    if not isinstance(observations, Mapping) or set(observations) != set(contract.required_observations):
        raise _fail("FAIL_EVIDENCE", "PASS observation set differs from case authority")
    for name, observation in observations.items():
        _validate_observation(observation, name)
    return value


def verdict_exit_code(value: Mapping[str, Any] | str) -> int:
    verdict = value.get("verdict") if isinstance(value, Mapping) else value
    return {"PASS": 0, "FAIL": 1, "NOT RUN": 3}.get(str(verdict), 2)


def _marker_fields(line: str, prefix: str) -> dict[str, str]:
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
