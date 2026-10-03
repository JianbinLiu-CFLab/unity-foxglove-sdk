#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Pure, fail-closed evidence protocol for Phase186-H acceptance.

This module deliberately performs no process launch and imports no Unity or ROS
runtime.  It is the shared authority used by the parent coordinator, workers,
Unity-log parser, CI selector, and regression tests.
"""

from __future__ import annotations
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase186_bridge_acceptance_protocol.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE

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
COORDINATOR_UNITY_READY_TIMEOUT_SECONDS = 900.0
# Workers are launched before Unity so the coordinator can prove actor
# ownership. The coordinator matches the Unity Batch probe's 15-minute bound;
# its first import can consume more than eight minutes on a cold package cache.
# Worker readiness must outlive that budget so valid witnesses are not killed
# before Unity reaches Play Mode.
ACTOR_UNITY_READY_TIMEOUT_SECONDS = (
    COORDINATOR_UNITY_READY_TIMEOUT_SECONDS + 60.0
)
WINDOWS_SAFE_ROS_DOMAIN_ID_MAX = 166

_HEAD = re.compile(r"\A[0-9a-f]{40}\Z")
_SHA256 = re.compile(r"\A[0-9a-f]{64}\Z")
_RUN_ID = re.compile(r"\Aphase186h-[A-Za-z0-9][A-Za-z0-9._-]{11,79}\Z")
_TOKEN = re.compile(r"\Ap186h_[A-Za-z0-9]{24,64}\Z")
_CASE_ID = re.compile(r"\A[a-z0-9]+(?:-[a-z0-9]+)*\Z")
_TOPIC = re.compile(r"\A/(?:[A-Za-z_][A-Za-z0-9_]*/)*[A-Za-z_][A-Za-z0-9_]*\Z")
_FORBIDDEN_OBSERVATION_SOURCES = frozenset(
    {"cached", "configuration", "unit-test", "skipped", "fixture"}
)
_SYNTHETIC_EVIDENCE_ROOT = pathlib.Path(__file__).resolve().parent / "__test_evidence__"


class ProtocolFailure(RuntimeError):
    """A stable, bounded acceptance protocol failure."""

    def __init__(self, code: str, message: str):
        """Initialize the helper state."""
        self.code = str(code)
        super().__init__(f"{self.code}: {message}")


def _fail(code: str, message: str) -> ProtocolFailure:
    """Handle fail for Phase186 acceptance."""
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
            "humble-fastrtps", "humble", "rmw_fastrtps_cpp", 160
        ),
        "jazzy-fastrtps": RowContract(
            "jazzy-fastrtps", "jazzy", "rmw_fastrtps_cpp", 161
        ),
        "lyrical-fastrtps": RowContract(
            "lyrical-fastrtps", "lyrical", "rmw_fastrtps_cpp", 162
        ),
        "lyrical-zenoh": RowContract(
            "lyrical-zenoh", "lyrical", "rmw_zenoh_cpp", 163
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
    """Handle case for Phase186 acceptance."""
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
            "custom_publish",
            "custom_publish",
            "custom_publish",
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
    """Require head."""
    if not isinstance(value, str) or _HEAD.fullmatch(value) is None:
        raise _fail("FAIL_PREFLIGHT", "feature HEAD must be a full lowercase Git SHA-1")
    return value


def require_run_id(value: object) -> str:
    """Require run id."""
    if not isinstance(value, str) or _RUN_ID.fullmatch(value) is None:
        raise _fail("FAIL_PREFLIGHT", "run ID is unsafe or outside the fixed bound")
    return value


def owned_unity_project_path(
    repository: pathlib.Path,
    run_id: str,
) -> pathlib.Path:
    """Return the short, deterministic project path owned by one live run."""

    root = pathlib.Path(repository).resolve()
    identity = hashlib.sha256(
        require_run_id(run_id).encode("utf-8")
    ).hexdigest()[:16]
    return (root / "build" / "phase186" / "u" / identity).resolve()


def require_token(value: object) -> str:
    """Require token."""
    if not isinstance(value, str) or _TOKEN.fullmatch(value) is None:
        raise _fail("FAIL_PREFLIGHT", "run token is unsafe or outside the fixed bound")
    return value


def require_case(case_id: object) -> CaseContract:
    """Require case."""
    if not isinstance(case_id, str) or _CASE_ID.fullmatch(case_id) is None:
        raise _fail("FAIL_PREFLIGHT", "case ID is malformed")
    contract = CASES.get(case_id)
    if contract is None:
        raise _fail("FAIL_PREFLIGHT", f"unknown Phase186-H case {case_id!r}")
    return contract


def require_row(row_id: object) -> RowContract:
    """Require row."""
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
    """Handle absolute path for Phase186 acceptance."""
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
    """Return whether below."""
    return path != parent and parent in path.parents


def _exact_keys(value: Mapping[str, Any], expected: set[str], label: str) -> None:
    """Handle exact keys for Phase186 acceptance."""
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


__all__ = [name for name in globals() if not name.startswith("__")]
