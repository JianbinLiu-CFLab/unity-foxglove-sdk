#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Pure evidence protocol shared by the Phase184-G acceptance workers."""

from __future__ import annotations

import hashlib
import json
import math
import os
import pathlib
import re
import subprocess
import time
import uuid
from dataclasses import dataclass
from typing import Any, Callable, Mapping, Sequence


RUN_CONFIG_SCHEMA_VERSION = 1
SUMMARY_SCHEMA_VERSION = 4
MAX_DIAGNOSTIC_CHARACTERS = 512
STREAM_CAPACITY = 32
MIN_STREAM_LAST_SEQUENCE_PERMILLE = 750
PARENT_DAEMON_ROLES = frozenset({"bridge", "zenoh-router"})
WINDOWS_CONTROL_BREAK_EXIT_CODES = frozenset(
    {
        -1073741510,  # Signed 32-bit STATUS_CONTROL_C_EXIT.
        3221225786,  # Unsigned 32-bit STATUS_CONTROL_C_EXIT.
    }
)


def process_exit_is_acceptable(
    role: str,
    exit_code: int,
    *,
    owner_requested: bool,
) -> bool:
    """Accept only truthful zero exits or owned Windows daemon control breaks."""

    if exit_code == 0:
        return True
    return (
        owner_requested
        and role in PARENT_DAEMON_ROLES
        and exit_code in WINDOWS_CONTROL_BREAK_EXIT_CODES
    )


FAILURE_CODES = {
    "preflight",
    "build",
    "runtime-selection",
    "unity-startup",
    "client",
    "peer",
    "bridge",
    "graph",
    "qos",
    "fanout",
    "origin",
    "stream",
    "terminal",
    "process-exit",
    "cleanup",
    "manual-stopped-early",
}

OPERATION_STALL_SECONDS = {
    "preflight": 30,
    "build": 1800,
    "runtime-selection": 900,
    "unity-startup": 900,
    "client": 120,
    "peer": 120,
    "bridge": 120,
    "graph": 120,
    "qos": 120,
    "fanout": 120,
    "origin": 120,
    "stream": 180,
    "terminal": 30,
    "process-exit": 30,
    "cleanup": 30,
    "teardown": 30,
}

_SAFE_RUN_ID = re.compile(r"\Aphase184g-[A-Za-z0-9][A-Za-z0-9._-]{7,79}\Z")
_SAFE_TOKEN = re.compile(r"\Ap184g_[A-Za-z0-9]{12,64}\Z")
_SAFE_TOPOLOGY_ID = re.compile(r"\A[A-Za-z0-9][A-Za-z0-9._-]{0,63}\Z")
_LOWER_SHA256 = re.compile(r"\A[0-9a-f]{64}\Z")
_SAFE_INTERFACE_PACKAGE = re.compile(r"\A[a-z][a-z0-9_]{0,254}\Z")
_SAFE_INTERFACE_TYPE = re.compile(
    r"\A[a-z][a-z0-9_]{0,254}/msg/[A-Za-z][A-Za-z0-9_]{0,254}\Z"
)
_CONCRETE_ROS_TOPIC = re.compile(
    r"\A/(?:[A-Za-z_][A-Za-z0-9_]*/)*[A-Za-z_][A-Za-z0-9_]*\Z"
)
_WINDOWS_ABSOLUTE_PATH = re.compile(r"\A(?:[A-Za-z]:[\\/]|\\\\)")
_TOKEN_IN_TEXT = re.compile(r"p184g_[A-Za-z0-9]{1,64}")

_SUMMARY_SECTION_NAMES = (
    "foxglove",
    "rosGraph",
    "qos",
    "targets",
    "origin",
    "stream",
)


def is_valid_ros_topic_name(topic: object) -> bool:
    """Return whether one absolute topic is safe for concrete ROS2 actors."""

    return isinstance(topic, str) and _CONCRETE_ROS_TOPIC.fullmatch(topic) is not None


class ProtocolFailure(RuntimeError):
    """A stable, machine-classifiable acceptance protocol failure."""

    def __init__(self, code: str, message: str):
        """Initialize the protocol failure."""

        self.code = code
        super().__init__(f"{code}: {message}")


@dataclass(frozen=True)
class ApplicabilityRule:
    """Whether a summary section is required for one deep acceptance case."""

    required: bool
    reason: str | None = None

    def __post_init__(self) -> None:
        """Validate the applicability rule invariants."""

        if self.required and self.reason is not None:
            raise ValueError("Required applicability rules cannot have an N/A reason.")
        if not self.required and not self.reason:
            raise ValueError("N/A applicability rules require a stable reason.")


@dataclass(frozen=True)
class ProfileContract:
    """One representative runtime/RMW selection."""

    runtime: str
    rmw: str
    discovery_range: str


@dataclass(frozen=True)
class CaseContract:
    """Immutable route, actor, and evidence contract for one deep case."""

    profile: str
    topics: tuple[str, ...]
    required_actors: frozenset[str]
    deliberately_absent_actors: Mapping[str, str]
    applicability: Mapping[str, ApplicabilityRule]


def _required() -> ApplicabilityRule:
    """Handle the required step."""

    return ApplicabilityRule(required=True)


def _not_applicable(reason: str) -> ApplicabilityRule:
    """Handle the not applicable step."""

    return ApplicabilityRule(required=False, reason=reason)


PROFILE_CONTRACTS: Mapping[str, ProfileContract] = {
    "core-foxglove": ProfileContract(
        runtime="core",
        rmw="none",
        discovery_range="LOCALHOST",
    ),
    "jazzy-fastrtps": ProfileContract(
        runtime="jazzy",
        rmw="rmw_fastrtps_cpp",
        discovery_range="SUBNET",
    ),
    "lyrical-zenoh": ProfileContract(
        runtime="lyrical",
        rmw="rmw_zenoh_cpp",
        discovery_range="LOCALHOST",
    ),
}

CASE_CONTRACTS: Mapping[str, CaseContract] = {
    "foxglove-profile": CaseContract(
        profile="core-foxglove",
        topics=(
            "/foxrun/phase184/profile/default",
            "/foxrun/phase184/profile/json",
        ),
        required_actors=frozenset({"foxglove-client"}),
        deliberately_absent_actors={},
        applicability={
            "foxglove": _required(),
            "rosGraph": _not_applicable("Foxglove-only case"),
            "qos": _not_applicable("No ROS direction"),
            "targets": _required(),
            "origin": _required(),
            "stream": _not_applicable("Ordinary fields"),
        },
    ),
    "multi-target": CaseContract(
        profile="jazzy-fastrtps",
        topics=("/foxrun/phase184/multi/state",),
        required_actors=frozenset(
            {"foxglove-client", "ros2-peer", "graph-observer", "bridge"}
        ),
        deliberately_absent_actors={},
        applicability={name: _required() for name in _SUMMARY_SECTION_NAMES}
        | {"stream": _not_applicable("Ordinary field")},
    ),
    "degraded-target": CaseContract(
        profile="jazzy-fastrtps",
        topics=("/foxrun/phase184/degraded/state",),
        required_actors=frozenset({"foxglove-client", "graph-observer"}),
        deliberately_absent_actors={"bridge": "Bridge deliberately not started"},
        applicability={
            "foxglove": _required(),
            "rosGraph": _required(),
            "qos": _not_applicable("No ROS publisher is allowed"),
            "targets": _required(),
            "origin": _not_applicable("Publish-only field"),
            "stream": _not_applicable("Ordinary field"),
        },
    ),
    "qos-contract": CaseContract(
        profile="jazzy-fastrtps",
        topics=(
            "/foxrun/phase184/qos/system_default",
            "/foxrun/phase184/qos/keep_all",
            "/foxrun/phase184/qos/keep_last_depth",
        ),
        required_actors=frozenset({"ros2-peer", "graph-observer", "bridge"}),
        deliberately_absent_actors={},
        applicability={
            "foxglove": _not_applicable("No Foxglove direction"),
            "rosGraph": _required(),
            "qos": _required(),
            "targets": _required(),
            "origin": _not_applicable("Publish-only fields"),
            "stream": _not_applicable("Ordinary fields"),
        },
    ),
    "stream-640hz": CaseContract(
        profile="lyrical-zenoh",
        topics=(
            "/foxrun/phase184/stream/state",
            "/foxrun/phase184/zenoh/origin",
        ),
        required_actors=frozenset(
            {"ros2-peer", "graph-observer", "zenoh-router"}
        ),
        deliberately_absent_actors={},
        applicability={
            "foxglove": _not_applicable("No Foxglove direction"),
            "rosGraph": _required(),
            "qos": _required(),
            "targets": _required(),
            "origin": _required(),
            "stream": _required(),
        },
    ),
}


_QOS_FIELDS = {"profile", "reliability", "durability", "history", "depth"}
_TRANSPORT_QOS_FIELDS = _QOS_FIELDS - {"profile"}
_RESOLVED_SYSTEM_DEFAULT_POLICIES = {
    "reliability": frozenset({"system_default", "reliable", "best_effort"}),
    "durability": frozenset({"system_default", "volatile", "transient_local"}),
    "history": frozenset({"system_default", "keep_last", "keep_all"}),
}


def expected_qos_by_topic(case: str) -> dict[str, dict[str, object]]:
    """Return the canonical portable QoS contract owned by the protocol."""

    contract = CASE_CONTRACTS.get(case)
    if contract is None:
        raise _fail("preflight", f"Unknown Phase184-G case {case!r}.")
    topics = contract.topics
    if case == "multi-target":
        return {
            topics[0]: {
                "profile": "default",
                "reliability": "reliable",
                "durability": "volatile",
                "history": "keep_last",
                "depth": 10,
            }
        }
    if case == "qos-contract":
        return {
            topics[0]: {
                "profile": "system_default",
                "reliability": "system_default",
                "durability": "system_default",
                "history": "system_default",
                "depth": 0,
            },
            topics[1]: {
                "profile": "default",
                "reliability": "reliable",
                "durability": "volatile",
                "history": "keep_all",
                "depth": 0,
            },
            topics[2]: {
                "profile": "default",
                "reliability": "best_effort",
                "durability": "transient_local",
                "history": "keep_last",
                "depth": 7,
            },
        }
    if case == "stream-640hz":
        sensor_data = {
            "profile": "sensor_data",
            "reliability": "best_effort",
            "durability": "volatile",
            "history": "keep_last",
            "depth": 5,
        }
        return {topic: dict(sensor_data) for topic in topics}
    return {}


_EXPECTED_PROFILE_EVIDENCE: Mapping[
    str, tuple[str, tuple[str, ...], str, str]
] = {
    "foxglove-profile": ("Foxglove", ("Foxglove",), "protobuf,json", "protobuf,json"),
    "multi-target": (
        "Ros2Native",
        ("Foxglove", "Ros2Native", "Ros2Bridge"),
        "protobuf",
        "protobuf",
    ),
    "degraded-target": (
        "None",
        ("Foxglove", "Ros2Bridge"),
        "protobuf",
        "not_applicable",
    ),
    "qos-contract": (
        "None",
        ("Ros2Native", "Ros2Bridge"),
        "protobuf",
        "not_applicable",
    ),
    "stream-640hz": (
        "Ros2Native",
        ("Ros2Native",),
        "protobuf",
        "protobuf",
    ),
}

_EXPECTED_TARGET_STATES: Mapping[str, Mapping[str, str]] = {
    "foxglove-profile": {"foxglove": "Ready"},
    "multi-target": {
        "foxglove": "Ready",
        "ros2Native": "Ready",
        "ros2Bridge": "Ready",
    },
    "degraded-target": {
        "foxglove": "Ready",
        "ros2Bridge": "Unavailable",
    },
    "qos-contract": {
        topic: "Ready" for topic in CASE_CONTRACTS["qos-contract"].topics
    },
    "stream-640hz": {"ros2Native": "Ready"},
}

_EXPECTED_TARGET_DIAGNOSTICS: Mapping[str, Mapping[str, int]] = {
    "foxglove-profile": {"failedTargets": 0},
    "multi-target": {"failedTargets": 0, "bridgeRuntimeFailures": 0},
    "degraded-target": {"failedTargets": 1, "bridgeDiagnostics": 1},
    "qos-contract": {"failedTargets": 0},
    "stream-640hz": {
        "copyFailed": 0,
        "staleCallbacks": 0,
        "rejectedAfterStop": 0,
    },
}

_EXPECTED_FOXGLOVE_SAMPLE_STAGES: Mapping[str, tuple[str, ...]] = {
    "foxglove-profile": (
        "profile-outbound",
        "json-outbound",
        "profile-a",
        "profile-b",
        "profile-local-after-remote",
    ),
    "multi-target": ("multi-local-1", "multi-local-3"),
    "degraded-target": ("degraded-local",),
}

_EXPECTED_PUBLISHER_SAMPLE_STAGES: Mapping[str, Mapping[str, str]] = {
    "multi-target": {
        "multi-local-1": "multi-local-1",
        "multi-local-3": "multi-local-3",
    },
    "qos-contract": {
        CASE_CONTRACTS["qos-contract"].topics[0]: "qos-system-default",
        CASE_CONTRACTS["qos-contract"].topics[1]: "qos-keep-all",
        CASE_CONTRACTS["qos-contract"].topics[2]: "qos-keep-last-depth",
    },
    "stream-640hz": {"origin-local": "origin-local"},
}

_PUBLISHER_ATTRIBUTION_BY_CASE: Mapping[str, frozenset[str]] = {
    "multi-target": frozenset(
        {
            "message-info-publisher-gid",
            "publication-sequence-plus-graph-gid",
        }
    ),
    "qos-contract": frozenset(
        {
            "message-info-publisher-gid",
            "publication-sequence-plus-graph-gid",
        }
    ),
    "stream-640hz": frozenset(
        {
            "message-info-publisher-gid",
            "sole-external-graph-gid",
        }
    ),
}


def failure_code(stage: str, *, blocked: bool = False) -> str:
    """Return the stable public code for one planned failure domain."""

    if stage not in FAILURE_CODES:
        raise ValueError(f"Unknown Phase184-G failure stage: {stage}")
    prefix = "BLOCKED" if blocked else "FAIL"
    return f"{prefix}_{stage.replace('-', '_').upper()}"


def _fail(stage: str, message: str, *, blocked: bool = False) -> ProtocolFailure:
    """Handle the fail step."""

    return ProtocolFailure(failure_code(stage, blocked=blocked), message)


def validate_case_profile(case: str, profile: str | None) -> CaseContract:
    """Resolve a case's locked profile and reject contradictory overrides."""

    contract = CASE_CONTRACTS.get(case)
    if contract is None:
        raise _fail("preflight", f"Unknown Phase184-G case {case!r}.")
    if profile is not None and profile != contract.profile:
        raise _fail(
            "runtime-selection",
            f"Case {case!r} requires profile {contract.profile!r}, not {profile!r}.",
        )
    return contract


def validate_execution_mode(*, batch: bool, manual_editor: bool) -> str:
    """Require exactly one supported Unity execution mode."""

    if batch == manual_editor:
        raise _fail(
            "preflight",
            "Exactly one of Batch mode and manual Editor mode must be selected.",
        )
    return "batch" if batch else "manual"


def token_sha256(token: str) -> str:
    """Hash a correlation token without retaining it in durable evidence."""

    return hashlib.sha256(token.encode("utf-8")).hexdigest()


def _require_mapping(value: object, label: str, stage: str = "preflight") -> Mapping[str, Any]:
    """Require mapping."""

    if not isinstance(value, Mapping):
        raise _fail(stage, f"{label} must be an object.")
    return value


def _require_string(value: object, label: str, stage: str = "preflight") -> str:
    """Require string."""

    if not isinstance(value, str) or not value:
        raise _fail(stage, f"{label} must be a non-empty string.")
    return value


def _require_bounded_int(
    value: object,
    label: str,
    minimum: int,
    maximum: int,
    stage: str = "preflight",
) -> int:
    """Require bounded int."""

    if isinstance(value, bool) or not isinstance(value, int):
        raise _fail(stage, f"{label} must be an integer.")
    if value < minimum or value > maximum:
        raise _fail(stage, f"{label} must be in [{minimum}, {maximum}].")
    return value


def _resolved_absolute_path(value: object, label: str) -> pathlib.Path:
    """Handle the resolved absolute path step."""

    text = _require_string(value, label)
    candidate = pathlib.Path(text)
    if not candidate.is_absolute():
        raise _fail("preflight", f"{label} must be absolute.")
    try:
        return candidate.resolve(strict=False)
    except OSError as exc:
        raise _fail("preflight", f"{label} cannot be resolved: {exc}") from exc


def _is_below(candidate: pathlib.Path, parent: pathlib.Path) -> bool:
    """Return whether below."""

    return candidate == parent or parent in candidate.parents


def _require_exact_keys(
    value: Mapping[str, Any],
    expected: set[str],
    label: str,
    stage: str = "preflight",
) -> None:
    """Require exact keys."""

    actual = set(value)
    if actual != expected:
        missing = sorted(expected - actual)
        unexpected = sorted(actual - expected)
        raise _fail(
            stage,
            f"{label} keys differ; missing={missing}, unexpected={unexpected}.",
        )


def validate_run_config(
    config: Mapping[str, Any],
    repo_root: os.PathLike[str] | str,
) -> CaseContract:
    """Validate the immutable coordination authority before any actor starts."""

    config = _require_mapping(config, "run-config")
    required_keys = {
        "schemaVersion",
        "executionMode",
        "runId",
        "token",
        "case",
        "profile",
        "projectPath",
        "outputRoot",
        "rosDistro",
        "rmw",
        "domainId",
        "discoveryRange",
        "zenohTopologyId",
        "phase181Workspace",
        "phase181Install",
        "bridgeOverlayInstall",
        "foxgloveHost",
        "foxglovePort",
        "bridgeHost",
        "bridgePort",
        "interfacePackage",
        "interfaceType",
        "interfaceDigest",
        "topics",
        "observationWindows",
        "readyFiles",
        "resultFiles",
        "unityLog",
    }
    _require_exact_keys(config, required_keys, "run-config")

    if config["schemaVersion"] != RUN_CONFIG_SCHEMA_VERSION:
        raise _fail("preflight", "Unsupported run-config schemaVersion.")
    if config["executionMode"] not in {"batch", "manual"}:
        raise _fail("preflight", "executionMode must be batch or manual.")

    run_id = _require_string(config["runId"], "runId")
    token = _require_string(config["token"], "token")
    if _SAFE_RUN_ID.fullmatch(run_id) is None:
        raise _fail("preflight", "runId contains unsafe characters or length.")
    if _SAFE_TOKEN.fullmatch(token) is None:
        raise _fail("preflight", "token contains unsafe characters or length.")

    case = _require_string(config["case"], "case")
    profile = _require_string(config["profile"], "profile")
    contract = validate_case_profile(case, profile)
    profile_contract = PROFILE_CONTRACTS[profile]
    if config["rosDistro"] != profile_contract.runtime or config["rmw"] != profile_contract.rmw:
        raise _fail(
            "runtime-selection",
            f"Profile {profile!r} requires runtime/RMW "
            f"{profile_contract.runtime!r}/{profile_contract.rmw!r}.",
        )

    repo = pathlib.Path(repo_root).resolve(strict=False)
    project = _resolved_absolute_path(config["projectPath"], "projectPath")
    output = _resolved_absolute_path(config["outputRoot"], "outputRoot")
    expected_project = (repo / "Unity2Foxglove").resolve(strict=False)
    acceptance_root = (repo / "build" / "phase184" / "acceptance").resolve(strict=False)
    if project != expected_project:
        raise _fail("preflight", "projectPath must select the repository Unity project.")
    if not _is_below(output, acceptance_root) or output == acceptance_root:
        raise _fail("preflight", "outputRoot must be an owned Phase184 acceptance run.")
    if output.name != run_id:
        raise _fail("preflight", "outputRoot leaf must equal runId.")

    phase181_root = (repo / "build" / "phase181").resolve(strict=False)
    phase181_workspace = _resolved_absolute_path(
        config["phase181Workspace"], "phase181Workspace"
    )
    phase181_install = _resolved_absolute_path(
        config["phase181Install"], "phase181Install"
    )
    bridge_overlay = _resolved_absolute_path(
        config["bridgeOverlayInstall"], "bridgeOverlayInstall"
    )
    if not _is_below(phase181_workspace, phase181_root):
        raise _fail("preflight", "phase181Workspace escaped build/phase181.")
    if not _is_below(phase181_install, phase181_workspace):
        raise _fail("preflight", "phase181Install escaped phase181Workspace.")
    if "bridge" in contract.required_actors:
        expected_bridge_overlay = (
            repo
            / "build"
            / "phase184"
            / "bridge-cache"
            / profile
            / "bridge-overlay"
            / "install"
        ).resolve(strict=False)
        if bridge_overlay != expected_bridge_overlay:
            raise _fail(
                "preflight",
                "bridgeOverlayInstall must select the exact profile-stable Bridge cache.",
            )
    elif not _is_below(bridge_overlay, output):
        raise _fail("preflight", "Unused bridgeOverlayInstall escaped outputRoot.")

    loopback_hosts = {"127.0.0.1", "localhost", "::1"}
    for key in ("foxgloveHost", "bridgeHost"):
        if config[key] not in loopback_hosts:
            raise _fail("preflight", f"{key} must be loopback.")
    foxglove_port = _require_bounded_int(
        config["foxglovePort"], "foxglovePort", 1, 65535
    )
    bridge_port = _require_bounded_int(config["bridgePort"], "bridgePort", 1, 65535)
    if foxglove_port == bridge_port:
        raise _fail("preflight", "Foxglove and Bridge ports must be distinct.")
    _require_bounded_int(config["domainId"], "domainId", 0, 232)
    if config["discoveryRange"] != profile_contract.discovery_range:
        raise _fail(
            "preflight",
            f"Profile {profile!r} requires discoveryRange "
            f"{profile_contract.discovery_range!r}.",
        )

    topology_id = config["zenohTopologyId"]
    if profile == "lyrical-zenoh":
        if not isinstance(topology_id, str) or _SAFE_TOPOLOGY_ID.fullmatch(topology_id) is None:
            raise _fail("runtime-selection", "Zenoh profile requires a safe topology id.")
    elif topology_id != "":
        raise _fail("runtime-selection", "Non-Zenoh profiles must not select a topology.")

    package = _require_string(config["interfacePackage"], "interfacePackage")
    interface_type = _require_string(config["interfaceType"], "interfaceType")
    digest = _require_string(config["interfaceDigest"], "interfaceDigest")
    if _SAFE_INTERFACE_PACKAGE.fullmatch(package) is None:
        raise _fail("preflight", "interfacePackage is malformed.")
    if (
        _SAFE_INTERFACE_TYPE.fullmatch(interface_type) is None
        or not interface_type.startswith(f"{package}/msg/")
    ):
        raise _fail("preflight", "interfaceType is malformed or has another package.")
    if _LOWER_SHA256.fullmatch(digest) is None:
        raise _fail("preflight", "interfaceDigest must be a lowercase SHA-256.")

    topics = config["topics"]
    if not isinstance(topics, list) or tuple(topics) != contract.topics:
        raise _fail("preflight", f"topics do not match case {case!r}.")
    if any(not is_valid_ros_topic_name(topic) for topic in topics):
        raise _fail(
            "preflight",
            f"Case {case!r} contains a non-concrete ROS2 topic name.",
        )

    windows = _require_mapping(config["observationWindows"], "observationWindows")
    expected_windows = {
        "positiveSeconds",
        "negativeSeconds",
        "streamProductionSeconds",
        "terminalSeconds",
        "teardownSeconds",
    }
    _require_exact_keys(windows, expected_windows, "observationWindows")
    for key in expected_windows:
        _require_bounded_int(windows[key], f"observationWindows.{key}", 1, 3600)

    actors = contract.required_actors | frozenset(contract.deliberately_absent_actors)
    expected_actor_keys = set(actors)
    for map_name, directory in (("readyFiles", "ready"), ("resultFiles", "results")):
        paths = _require_mapping(config[map_name], map_name)
        _require_exact_keys(paths, expected_actor_keys, map_name)
        for actor in actors:
            actual = _resolved_absolute_path(paths[actor], f"{map_name}.{actor}")
            expected = (output / directory / f"{actor}.json").resolve(strict=False)
            if actual != expected:
                raise _fail(
                    "preflight",
                    f"{map_name}.{actor} must use its immutable owned path.",
                )

    unity_log = _resolved_absolute_path(config["unityLog"], "unityLog")
    if unity_log != (output / "unity-editor.log").resolve(strict=False):
        raise _fail("preflight", "unityLog must use the owned run log path.")

    return contract


_REQUIRED_SECTION_FIELDS: Mapping[str, set[str]] = {
    "foxglove": {
        "deliveryObserved",
        "channelEncodings",
        "sampleToken",
        "sampleStages",
        "timestamp",
    },
    "rosGraph": {
        "endpointsObserved",
        "nodeIdentities",
        "publisherGids",
        "publishersByTopic",
        "samplePublisherGids",
        "negativeObservationSeconds",
    },
    "qos": {"requested", "transportObserved", "matches"},
    "targets": {
        "states",
        "diagnosticCounts",
        "healthyDelivery",
        "statusEvidence",
    },
    "origin": {"remoteApplied", "sameOriginDropped", "laterLocalPublished"},
    "stream": {
        "offered",
        "received",
        "accepted",
        "replaced",
        "rateDropped",
        "transportDropped",
        "dropped",
        "drained",
        "disposed",
        "maximumQueueDepth",
        "lastSequence",
        "retainedOrdered",
        "ownershipBalanced",
    },
}

_SECTION_BOOLEAN_FIELDS: Mapping[str, set[str]] = {
    "foxglove": {"deliveryObserved"},
    "rosGraph": {"endpointsObserved"},
    "qos": {"matches"},
    "targets": {"healthyDelivery"},
    "origin": {"remoteApplied", "sameOriginDropped", "laterLocalPublished"},
    "stream": {"retainedOrdered", "ownershipBalanced"},
}

_SECTION_FAILURE_STAGE = {
    "foxglove": "client",
    "rosGraph": "graph",
    "qos": "qos",
    "targets": "fanout",
    "origin": "origin",
    "stream": "stream",
}


def _require_string_list(
    value: object,
    label: str,
    stage: str,
    *,
    allow_empty: bool,
) -> list[str]:
    """Require string list."""

    if (
        not isinstance(value, list)
        or (not allow_empty and not value)
        or any(not isinstance(item, str) or not item for item in value)
    ):
        qualifier = "a string array" if allow_empty else "a non-empty string array"
        raise _fail(stage, f"{label} must be {qualifier}.")
    return value


def _normalized_policy(value: object) -> str:
    """Handle the normalized policy step."""

    text = str(value).lower()
    aliases = {
        "qosreliabilitypolicy.reliable": "reliable",
        "qosreliabilitypolicy.best_effort": "best_effort",
        "qosreliabilitypolicy.system_default": "system_default",
        "qosdurabilitypolicy.volatile": "volatile",
        "qosdurabilitypolicy.transient_local": "transient_local",
        "qosdurabilitypolicy.system_default": "system_default",
        "qoshistorypolicy.keep_last": "keep_last",
        "qoshistorypolicy.keep_all": "keep_all",
        "qoshistorypolicy.system_default": "system_default",
    }
    return aliases.get(text, text)


def _validate_qos_value(
    value: object,
    expected: Mapping[str, object],
    label: str,
    *,
    include_profile: bool,
) -> None:
    """Validate QoS value."""

    actual = _require_mapping(value, label, "qos")
    expected_keys = _QOS_FIELDS if include_profile else _TRANSPORT_QOS_FIELDS
    _require_exact_keys(actual, expected_keys, label, "qos")
    for key in expected_keys - {"depth"}:
        if _normalized_policy(actual[key]) != expected[key]:
            raise _fail("qos", f"{label}.{key} drifted from the requested policy.")
    depth = actual["depth"]
    if isinstance(depth, bool) or not isinstance(depth, int) or depth != expected["depth"]:
        raise _fail("qos", f"{label}.depth drifted from the requested policy.")


def _validate_graph_qos_value(
    value: object,
    expected: Mapping[str, object],
    label: str,
) -> None:
    """Validate represented graph axes and preserve explicit unknowns."""

    actual = _require_mapping(value, label, "qos")
    _require_exact_keys(
        actual,
        _TRANSPORT_QOS_FIELDS | {"representedAxes"},
        label,
        "qos",
    )
    represented = _require_string_list(
        actual["representedAxes"],
        f"{label}.representedAxes",
        "qos",
        allow_empty=True,
    )
    represented_set = set(represented)
    if (
        len(represented_set) != len(represented)
        or not represented_set.issubset(_TRANSPORT_QOS_FIELDS)
    ):
        raise _fail("qos", f"{label}.representedAxes is invalid.")

    for key in _TRANSPORT_QOS_FIELDS - {"depth"}:
        normalized = _normalized_policy(actual[key])
        if key in represented_set:
            expected_policy = expected[key]
            if (
                expected_policy == "system_default"
                and normalized not in _RESOLVED_SYSTEM_DEFAULT_POLICIES[key]
            ):
                raise _fail(
                    "qos",
                    f"{label}.{key} is not a valid resolved SystemDefault policy.",
                )
            if expected_policy != "system_default" and normalized != expected_policy:
                raise _fail(
                    "qos",
                    f"{label}.{key} drifted from the requested policy.",
                )
        elif normalized != "unknown":
            raise _fail(
                "qos",
                f"{label}.{key} is unlabeled graph evidence.",
            )

    depth = actual["depth"]
    if isinstance(depth, bool) or not isinstance(depth, int):
        raise _fail("qos", f"{label}.depth must be an integer.")
    if "depth" in represented_set:
        if expected["history"] == "system_default":
            if depth < 0:
                raise _fail("qos", f"{label}.depth is invalid.")
        elif depth != expected["depth"]:
            raise _fail("qos", f"{label}.depth drifted from the requested policy.")
    elif depth != 0:
        raise _fail("qos", f"{label}.depth is unlabeled graph evidence.")


def _validate_resolved_system_default_agreement(
    values: Sequence[Mapping[str, object]],
    expected: Mapping[str, object],
    label: str,
) -> None:
    """Validate resolved system default agreement."""

    if len(values) < 2:
        return
    for axis in ("reliability", "durability", "history"):
        if expected[axis] != "system_default":
            continue
        represented = [axis in value["representedAxes"] for value in values]
        if any(represented) and not all(represented):
            raise _fail(
                "qos",
                f"{label}.{axis} representation differs between endpoints.",
            )
        if all(represented) and len(
            {_normalized_policy(value[axis]) for value in values}
        ) != 1:
            raise _fail(
                "qos",
                f"{label}.{axis} resolved differently between endpoints.",
            )
    if expected["history"] == "system_default":
        represented = ["depth" in value["representedAxes"] for value in values]
        if any(represented) and not all(represented):
            raise _fail(
                "qos",
                f"{label}.depth representation differs between endpoints.",
            )
        if all(represented) and len({value["depth"] for value in values}) != 1:
            raise _fail(
                "qos",
                f"{label}.depth resolved differently between endpoints.",
            )


def _expected_graph_direction_counts(
    case: str,
    topic: str,
) -> tuple[int, int]:
    """Handle the expected graph direction counts step."""

    if case in {"multi-target", "qos-contract"}:
        return 2, 1 if case == "multi-target" else 0
    if case == "stream-640hz":
        stream_topic, origin_topic = CASE_CONTRACTS[case].topics
        if topic == stream_topic:
            return 0, 1
        if topic == origin_topic:
            return 1, 1
    return 0, 0


def _validate_qos_evidence(
    summary: Mapping[str, Any],
    case: str,
) -> None:
    """Validate QoS evidence."""

    expected_qos = expected_qos_by_topic(case)
    if summary["profile"]["requestedQos"] != expected_qos:
        raise _fail("qos", "profile.requestedQos drifted from the case contract.")
    if summary["qos"]["requested"] != expected_qos:
        raise _fail("qos", "qos.requested drifted from the case contract.")

    transport = _require_mapping(
        summary["qos"]["transportObserved"],
        "qos.transportObserved",
        "qos",
    )
    expected_sources = (
        {"graph", "bridge"}
        if case in {"multi-target", "qos-contract"}
        else {"graph"}
    )
    _require_exact_keys(transport, expected_sources, "qos.transportObserved", "qos")
    graph = _require_mapping(transport["graph"], "qos.transportObserved.graph", "qos")
    _require_exact_keys(graph, set(expected_qos), "qos.transportObserved.graph", "qos")
    for topic, expected in expected_qos.items():
        topic_evidence = _require_mapping(
            graph[topic],
            f"qos.transportObserved.graph.{topic}",
            "qos",
        )
        _require_exact_keys(
            topic_evidence,
            {"publishers", "subscriptions"},
            f"qos.transportObserved.graph.{topic}",
            "qos",
        )
        minimum_publishers, minimum_subscriptions = _expected_graph_direction_counts(
            case,
            topic,
        )
        for direction, minimum in (
            ("publishers", minimum_publishers),
            ("subscriptions", minimum_subscriptions),
        ):
            values = topic_evidence[direction]
            if not isinstance(values, list) or len(values) < minimum:
                raise _fail(
                    "qos",
                    f"qos.transportObserved.graph.{topic}.{direction} is incomplete.",
                )
            for index, value in enumerate(values):
                _validate_graph_qos_value(
                    value,
                    expected,
                    f"qos.transportObserved.graph.{topic}.{direction}[{index}]",
                )
            _validate_resolved_system_default_agreement(
                values,
                expected,
                f"qos.transportObserved.graph.{topic}.{direction}",
            )

    if "bridge" in expected_sources:
        bridge = _require_mapping(
            transport["bridge"],
            "qos.transportObserved.bridge",
            "qos",
        )
        _require_exact_keys(bridge, set(expected_qos), "qos.transportObserved.bridge", "qos")
        for topic, expected in expected_qos.items():
            _validate_qos_value(
                bridge[topic],
                expected,
                f"qos.transportObserved.bridge.{topic}",
                include_profile=True,
            )


def _validate_sample_publisher_evidence(
    graph: Mapping[str, Any],
    case: str,
    expected_token: str,
) -> None:
    """Validate sample publisher evidence."""

    samples = _require_mapping(
        graph["samplePublisherGids"],
        "rosGraph.samplePublisherGids",
        "graph",
    )
    expected_samples = _EXPECTED_PUBLISHER_SAMPLE_STAGES.get(case, {})
    _require_exact_keys(
        samples,
        set(expected_samples),
        "rosGraph.samplePublisherGids",
        "graph",
    )
    graph_gids = set(graph["publisherGids"])
    minimum = 2 if case in {"multi-target", "qos-contract"} else 1
    for key, suffix in expected_samples.items():
        entry = _require_mapping(
            samples[key],
            f"rosGraph.samplePublisherGids.{key}",
            "graph",
        )
        _require_exact_keys(
            entry,
            {"sampleSha256", "publisherGids", "attribution"},
            f"rosGraph.samplePublisherGids.{key}",
            "graph",
        )
        if entry["sampleSha256"] != token_sha256(expected_token + "-" + suffix):
            raise _fail("graph", f"Publisher sample {key!r} is not token-correlated.")
        gids = _require_string_list(
            entry["publisherGids"],
            f"rosGraph.samplePublisherGids.{key}.publisherGids",
            "graph",
            allow_empty=False,
        )
        if len(set(gids)) < minimum or not set(gids).issubset(graph_gids):
            raise _fail("graph", f"Publisher sample {key!r} has invalid endpoint GIDs.")
        attribution = _require_string(
            entry["attribution"],
            f"rosGraph.samplePublisherGids.{key}.attribution",
            "graph",
        )
        if attribution not in _PUBLISHER_ATTRIBUTION_BY_CASE.get(case, frozenset()):
            raise _fail(
                "graph",
                f"Publisher sample {key!r} has an unsupported attribution source.",
            )


def _validate_graph_evidence(
    graph: Mapping[str, Any],
    case: str,
    expected_token: str,
) -> None:
    """Validate graph evidence."""

    allow_empty = case == "degraded-target"
    nodes = _require_string_list(
        graph["nodeIdentities"],
        "rosGraph.nodeIdentities",
        "graph",
        allow_empty=allow_empty,
    )
    gids = _require_string_list(
        graph["publisherGids"],
        "rosGraph.publisherGids",
        "graph",
        allow_empty=allow_empty,
    )
    if len(nodes) != len(set(nodes)) or len(gids) != len(set(gids)):
        raise _fail("graph", "ROS graph identities and GIDs must be unique.")
    publishers = _require_mapping(
        graph["publishersByTopic"],
        "rosGraph.publishersByTopic",
        "graph",
    )
    topics = CASE_CONTRACTS[case].topics
    _require_exact_keys(publishers, set(topics), "rosGraph.publishersByTopic", "graph")
    observed_gids: set[str] = set()
    observed_nodes: set[str] = set()
    for topic in topics:
        entries = publishers[topic]
        expected_count, _ = _expected_graph_direction_counts(case, topic)
        if not isinstance(entries, list) or len(entries) != expected_count:
            raise _fail("graph", f"rosGraph.publishersByTopic.{topic} count drifted.")
        for index, raw in enumerate(entries):
            entry = _require_mapping(
                raw,
                f"rosGraph.publishersByTopic.{topic}[{index}]",
                "graph",
            )
            _require_exact_keys(
                entry,
                {"node", "gid"},
                f"rosGraph.publishersByTopic.{topic}[{index}]",
                "graph",
            )
            node = _require_string(entry["node"], "publisher node", "graph")
            gid = _require_string(entry["gid"], "publisher gid", "graph")
            observed_nodes.add(node)
            observed_gids.add(gid)
    if observed_gids != set(gids) or not observed_nodes.issubset(set(nodes)):
        raise _fail("graph", "ROS graph publisher identities do not match their topic evidence.")

    negative_seconds = graph["negativeObservationSeconds"]
    if isinstance(negative_seconds, bool) or not isinstance(
        negative_seconds,
        (int, float),
    ):
        raise _fail("graph", "rosGraph.negativeObservationSeconds must be numeric.")
    if case == "degraded-target":
        if negative_seconds < 3 or nodes or gids:
            raise _fail("graph", "Degraded graph evidence did not prove an empty negative window.")
    elif negative_seconds != 0:
        raise _fail("graph", "Positive graph evidence cannot claim a negative window.")

    _validate_sample_publisher_evidence(graph, case, expected_token)


def _validate_required_section(
    name: str,
    section: Mapping[str, Any],
    *,
    require_positive: bool,
) -> None:
    """Validate required section."""

    expected = {"applicability"} | _REQUIRED_SECTION_FIELDS[name]
    _require_exact_keys(section, expected, name, "terminal")
    if section["applicability"] != "required":
        raise _fail("terminal", f"{name} must be marked required.")
    for key in _SECTION_BOOLEAN_FIELDS[name]:
        value = section[key]
        if not isinstance(value, bool):
            raise _fail("terminal", f"{name}.{key} must be boolean.")
        if require_positive and not value:
            raise _fail(_SECTION_FAILURE_STAGE[name], f"{name}.{key} is false.")

    if name == "foxglove":
        if not isinstance(section["channelEncodings"], list) or not section["channelEncodings"]:
            raise _fail("client", "foxglove.channelEncodings is empty.")
        _require_string(section["sampleToken"], "foxglove.sampleToken", "client")
        _require_string_list(
            section["sampleStages"],
            "foxglove.sampleStages",
            "client",
            allow_empty=False,
        )
        if isinstance(section["timestamp"], bool) or not isinstance(
            section["timestamp"], (int, float)
        ):
            raise _fail("client", "foxglove.timestamp must be numeric.")
    elif name == "rosGraph":
        for key in ("nodeIdentities", "publisherGids"):
            if not isinstance(section[key], list):
                raise _fail("graph", f"rosGraph.{key} must be an array.")
        _require_mapping(section["publishersByTopic"], "rosGraph.publishersByTopic", "graph")
        _require_mapping(
            section["samplePublisherGids"],
            "rosGraph.samplePublisherGids",
            "graph",
        )
    elif name == "qos":
        _require_mapping(section["requested"], "qos.requested", "qos")
        _require_mapping(section["transportObserved"], "qos.transportObserved", "qos")
    elif name == "targets":
        states = _require_mapping(section["states"], "targets.states", "fanout")
        counts = _require_mapping(
            section["diagnosticCounts"], "targets.diagnosticCounts", "fanout"
        )
        if not states:
            raise _fail("fanout", "targets.states is empty.")
        if any(
            not isinstance(key, str)
            or value not in {"Ready", "Degraded", "Unavailable"}
            for key, value in states.items()
        ):
            raise _fail("fanout", "targets.states contains an invalid target state.")
        _require_mapping(section["statusEvidence"], "targets.statusEvidence", "fanout")
        for key, count in counts.items():
            if (
                not isinstance(key, str)
                or isinstance(count, bool)
                or not isinstance(count, int)
                or count < 0
            ):
                raise _fail("fanout", "targets.diagnosticCounts is malformed.")
    elif name == "stream":
        for key in _REQUIRED_SECTION_FIELDS["stream"] - _SECTION_BOOLEAN_FIELDS["stream"]:
            value = section[key]
            if isinstance(value, bool) or not isinstance(value, int) or value < 0:
                raise _fail("stream", f"stream.{key} must be a non-negative integer.")


def validate_summary(
    summary: Mapping[str, Any],
    *,
    expected_case: str,
    expected_token: str,
) -> CaseContract:
    """Fail closed on stale, incomplete, contradictory, or synthetic evidence."""

    summary = _require_mapping(summary, "summary", "terminal")
    expected_top_keys = {
        "summarySchemaVersion",
        "identity",
        "profile",
        *_SUMMARY_SECTION_NAMES,
        "processes",
        "cleanup",
        "verdict",
    }
    _require_exact_keys(summary, expected_top_keys, "summary", "terminal")
    if summary["summarySchemaVersion"] != SUMMARY_SCHEMA_VERSION:
        raise _fail("terminal", "Unsupported summary schema version.")

    contract = CASE_CONTRACTS.get(expected_case)
    if contract is None:
        raise _fail("terminal", f"Unknown expected case {expected_case!r}.")

    identity = _require_mapping(summary["identity"], "identity", "terminal")
    _require_exact_keys(
        identity,
        {
            "runId",
            "case",
            "tokenSha256",
            "unityVersion",
            "interfaceIdentity",
            "interfaceDigest",
        },
        "identity",
        "terminal",
    )
    if identity["case"] != expected_case:
        raise _fail("terminal", "Summary case does not match the requested case.")
    if identity["tokenSha256"] != token_sha256(expected_token):
        raise _fail("terminal", "Summary token digest is stale or mismatched.")
    run_id = _require_string(identity["runId"], "identity.runId", "terminal")
    if _SAFE_RUN_ID.fullmatch(run_id) is None:
        raise _fail("terminal", "identity.runId is malformed.")
    _require_string(identity["unityVersion"], "identity.unityVersion", "terminal")
    _require_string(
        identity["interfaceIdentity"], "identity.interfaceIdentity", "terminal"
    )
    digest = _require_string(
        identity["interfaceDigest"], "identity.interfaceDigest", "terminal"
    )
    if _LOWER_SHA256.fullmatch(digest) is None:
        raise _fail("terminal", "identity.interfaceDigest is malformed.")

    profile = _require_mapping(summary["profile"], "profile", "terminal")
    _require_exact_keys(
        profile,
        {
            "profile",
            "runtime",
            "rmw",
            "source",
            "targets",
            "publishEncoding",
            "subscribeEncoding",
            "requestedQos",
        },
        "profile",
        "terminal",
    )
    profile_contract = PROFILE_CONTRACTS[contract.profile]
    if (
        profile["profile"] != contract.profile
        or profile["runtime"] != profile_contract.runtime
        or profile["rmw"] != profile_contract.rmw
    ):
        raise _fail("runtime-selection", "Summary profile/runtime/RMW drifted.")
    _require_string(profile["source"], "profile.source", "terminal")
    if not isinstance(profile["targets"], list):
        raise _fail("terminal", "profile.targets must be an array.")
    for key in ("publishEncoding", "subscribeEncoding"):
        _require_string(profile[key], f"profile.{key}", "terminal")
    _require_mapping(profile["requestedQos"], "profile.requestedQos", "qos")
    expected_source, expected_targets, expected_publish, expected_subscribe = (
        _EXPECTED_PROFILE_EVIDENCE[expected_case]
    )
    if (
        profile["source"] != expected_source
        or profile["targets"] != list(expected_targets)
        or profile["publishEncoding"] != expected_publish
        or profile["subscribeEncoding"] != expected_subscribe
    ):
        raise _fail("terminal", "Summary Source/Targets/Encoding drifted from the case contract.")

    verdict = summary["verdict"]
    if not isinstance(verdict, str) or (
        verdict != "PASS"
        and re.fullmatch(r"(?:FAIL|BLOCKED)_[A-Z0-9_]+", verdict) is None
    ):
        raise _fail("terminal", "verdict must be PASS, FAIL_*, or BLOCKED_*.")
    require_positive = verdict == "PASS"

    for name, rule in contract.applicability.items():
        section = _require_mapping(summary[name], name, "terminal")
        if rule.required:
            _validate_required_section(
                name,
                section,
                require_positive=require_positive,
            )
        else:
            _require_exact_keys(
                section,
                {"applicability", "reason"},
                name,
                "terminal",
            )
            if (
                section["applicability"] != "not_applicable"
                or section["reason"] != rule.reason
            ):
                raise _fail("terminal", f"{name} has an unapproved N/A reason.")

    if require_positive:
        if contract.applicability["foxglove"].required:
            foxglove = summary["foxglove"]
            if foxglove["sampleToken"] != token_sha256(expected_token):
                raise _fail("client", "Foxglove sample evidence is not token-correlated.")
            expected_stages = _EXPECTED_FOXGLOVE_SAMPLE_STAGES[expected_case]
            if foxglove["sampleStages"] != list(expected_stages):
                raise _fail("client", "Foxglove sample stages drifted from the case contract.")

        if expected_case == "foxglove-profile":
            encodings = set(summary["foxglove"]["channelEncodings"])
            if encodings != {"json", "protobuf"}:
                raise _fail(
                    "client",
                    "Foxglove profile case must prove both JSON and Protobuf channels.",
                )
        elif expected_case in {"multi-target", "degraded-target"}:
            if set(summary["foxglove"]["channelEncodings"]) != {"protobuf"}:
                raise _fail(
                    "client",
                    "The selected fanout case must prove its Protobuf channel.",
                )

        if contract.applicability["qos"].required:
            _validate_qos_evidence(summary, expected_case)

        if contract.applicability["rosGraph"].required:
            _validate_graph_evidence(
                summary["rosGraph"],
                expected_case,
                expected_token,
            )

        targets = summary["targets"]
        if targets["states"] != _EXPECTED_TARGET_STATES[expected_case]:
            raise _fail("fanout", "Target states drifted from the exact case contract.")
        if targets["diagnosticCounts"] != _EXPECTED_TARGET_DIAGNOSTICS[expected_case]:
            raise _fail("fanout", "Target diagnostic counts drifted from the case contract.")

        if expected_case == "foxglove-profile":
            expected_status = {
                "aggregate": "Ready",
                "succeeded": "Foxglove",
                "failed": "None",
                "topics": 2,
            }
            if targets["statusEvidence"] != expected_status:
                raise _fail(
                    "fanout",
                    "Foxglove target evidence must come from both runtime dispatches.",
                )
        elif expected_case == "multi-target":
            expected_status = {
                "aggregate": "Ready",
                "succeeded": "Foxglove,Ros2Native,Ros2Bridge",
                "failed": "None",
                "bridgeRuntimeFailures": 0,
            }
            if targets["statusEvidence"] != expected_status:
                raise _fail(
                    "fanout",
                    "Multi-target evidence must come from the runtime dispatch.",
                )
        elif expected_case == "degraded-target":
            expected_status = {
                "aggregate": "Degraded",
                "succeeded": "Foxglove",
                "failed": "Ros2Bridge",
                "bridgeDiagnostics": 1,
            }
            if targets["statusEvidence"] != expected_status:
                raise _fail(
                    "fanout",
                    "Degraded target evidence must come from the runtime status transition.",
                )
        elif expected_case == "qos-contract":
            expected_topics = {
                topic: {
                    "aggregate": "Ready",
                    "succeeded": "Ros2Native,Ros2Bridge",
                    "failed": "None",
                }
                for topic in CASE_CONTRACTS["qos-contract"].topics
            }
            if targets["statusEvidence"] != {"topics": expected_topics}:
                raise _fail(
                    "fanout",
                    "QoS target evidence must cover each exact runtime dispatch.",
                )
        elif expected_case == "stream-640hz":
            status = _require_mapping(
                targets["statusEvidence"],
                "targets.statusEvidence",
                "fanout",
            )
            if (
                set(status)
                != {
                    "bindingState",
                    "received",
                    "copyFailed",
                    "staleCallbacks",
                    "rejectedAfterStop",
                }
                or status["bindingState"] not in {"Ready", "Receiving"}
                or status["received"] != summary["stream"]["received"]
                or status["copyFailed"] != 0
                or status["staleCallbacks"] != 0
                or status["rejectedAfterStop"] != 0
            ):
                raise _fail(
                    "fanout",
                    "Stream target evidence must come from the live native subscription.",
                )

        if expected_case == "stream-640hz":
            stream = summary["stream"]
            if (
                stream["offered"] != 1280
                or stream["received"] <= STREAM_CAPACITY
                or stream["received"] > stream["offered"]
                or stream["received"] + stream["transportDropped"]
                != stream["offered"]
                or stream["accepted"] + stream["rateDropped"]
                != stream["received"]
                or stream["transportDropped"] + stream["rateDropped"]
                != stream["dropped"]
                or stream["drained"] + stream["replaced"] != stream["accepted"]
                or stream["disposed"] != stream["drained"] + stream["replaced"]
                or stream["maximumQueueDepth"] != STREAM_CAPACITY
                or stream["replaced"] <= 0
                or (stream["lastSequence"] + 1) * 1000
                < stream["offered"] * MIN_STREAM_LAST_SEQUENCE_PERMILLE
            ):
                raise _fail(
                    "stream",
                    "Stream counters do not prove the locked delivery/capacity/ownership contract.",
                )

    processes = summary["processes"]
    if not isinstance(processes, list):
        raise _fail("process-exit", "processes must be an array.")
    by_role: dict[str, Mapping[str, Any]] = {}
    for item in processes:
        entry = _require_mapping(item, "process entry", "process-exit")
        role = _require_string(entry.get("role"), "process role", "process-exit")
        if role in by_role:
            raise _fail("process-exit", f"Duplicate process role {role!r}.")
        by_role[role] = entry
    expected_roles = (
        contract.required_actors
        | frozenset(contract.deliberately_absent_actors)
        | frozenset({"unity"})
    )
    if set(by_role) != set(expected_roles):
        raise _fail("process-exit", "Process roles do not match the case contract.")
    for role in contract.required_actors | frozenset({"unity"}):
        entry = by_role[role]
        _require_exact_keys(
            entry,
            {"role", "started", "exitCode", "termination"},
            f"processes.{role}",
            "process-exit",
        )
        if entry["started"] is not True:
            raise _fail("process-exit", f"Required actor {role!r} was not started.")
        exit_code = entry["exitCode"]
        termination = entry["termination"]
        if isinstance(exit_code, bool) or not isinstance(exit_code, int):
            raise _fail("process-exit", f"Actor {role!r} has an invalid exit code.")
        if termination not in {"self", "owner_requested"}:
            raise _fail(
                "process-exit",
                f"Actor {role!r} has invalid termination provenance.",
            )
        if termination == "owner_requested":
            if not process_exit_is_acceptable(
                role,
                exit_code,
                owner_requested=True,
            ):
                raise _fail(
                    "process-exit",
                    f"Actor {role!r} has invalid owner-requested exit evidence.",
                )
        elif require_positive and not process_exit_is_acceptable(
            role,
            exit_code,
            owner_requested=False,
        ):
            raise _fail("process-exit", f"Actor {role!r} has an invalid exit code.")
    for role, reason in contract.deliberately_absent_actors.items():
        entry = by_role[role]
        _require_exact_keys(
            entry,
            {"role", "started", "reason"},
            f"processes.{role}",
            "process-exit",
        )
        if entry["started"] is not False or entry["reason"] != reason:
            raise _fail("process-exit", f"Absent actor {role!r} is misrepresented.")

    cleanup = _require_mapping(summary["cleanup"], "cleanup", "cleanup")
    _require_exact_keys(
        cleanup,
        {"processes", "files", "junctions", "subst"},
        "cleanup",
        "cleanup",
    )
    for key, value in cleanup.items():
        if not isinstance(value, bool):
            raise _fail("cleanup", f"cleanup.{key} must be boolean.")
        if require_positive and not value:
            raise _fail("cleanup", f"cleanup.{key} is false.")

    return contract


def _redact_for_json(value: object, repo_root: pathlib.Path) -> object:
    """Handle the redact for JSON step."""

    if isinstance(value, Mapping):
        result: dict[str, object] = {}
        for key, nested in value.items():
            safe_key = str(key)
            normalized_key = safe_key.casefold()
            if normalized_key in {"commandline", "environment"}:
                continue
            if normalized_key == "token":
                if isinstance(nested, str):
                    result["tokenSha256"] = token_sha256(nested)
                continue
            result[safe_key] = _redact_for_json(nested, repo_root)
        return result
    if isinstance(value, (list, tuple)):
        return [_redact_for_json(item, repo_root) for item in value]
    if isinstance(value, pathlib.Path):
        value = str(value)
    if isinstance(value, str):
        redacted = _TOKEN_IN_TEXT.sub("<redacted-token>", value)
        root_text = str(repo_root).rstrip("\\/")
        if redacted.casefold() == root_text.casefold():
            redacted = "<repo>"
        elif redacted.casefold().startswith((root_text + os.sep).casefold()):
            relative = redacted[len(root_text) :].lstrip("\\/").replace("\\", "/")
            redacted = f"<repo>/{relative}"
        elif _WINDOWS_ABSOLUTE_PATH.match(redacted):
            redacted = "<redacted-path>"
        if len(redacted) > MAX_DIAGNOSTIC_CHARACTERS:
            redacted = redacted[: MAX_DIAGNOSTIC_CHARACTERS - 1] + "…"
        return redacted
    return value


def write_json_atomic(
    destination: os.PathLike[str] | str,
    payload: Mapping[str, Any],
    *,
    repo_root: os.PathLike[str] | str,
) -> None:
    """Write bounded, redacted JSON through a same-directory atomic replace."""

    path = pathlib.Path(destination)
    path.parent.mkdir(parents=True, exist_ok=True)
    repo = pathlib.Path(repo_root).resolve(strict=False)
    redacted = _redact_for_json(payload, repo)
    temporary = path.with_name(f"{path.name}.{uuid.uuid4().hex}.tmp")
    try:
        with temporary.open("w", encoding="utf-8", newline="\n") as handle:
            json.dump(redacted, handle, ensure_ascii=False, indent=2, sort_keys=True)
            handle.write("\n")
            handle.flush()
            os.fsync(handle.fileno())
        os.replace(temporary, path)
    finally:
        try:
            temporary.unlink()
        except FileNotFoundError:
            pass


class ProgressWatchdog:
    """No-progress watchdog whose deadline resets only on observable progress."""

    def __init__(
        self,
        operation: str,
        *,
        stall_seconds: float | None = None,
        now: Callable[[], float] = time.monotonic,
    ):
        """Initialize the progress watchdog."""

        if not operation or not isinstance(operation, str):
            raise ValueError("operation must be a non-empty string")
        if stall_seconds is None:
            try:
                stall_seconds = float(OPERATION_STALL_SECONDS[operation])
            except KeyError as exc:
                raise ValueError(f"No watchdog default for {operation!r}") from exc
        if not math.isfinite(stall_seconds) or stall_seconds <= 0:
            raise ValueError("stall_seconds must be finite and positive")
        self.operation = operation
        self.stall_seconds = float(stall_seconds)
        self._now = now
        self.started_at = now()
        self.last_progress_at = self.started_at
        self.last_progress = "operation started"

    def progress(self, description: str) -> None:
        """Record one bounded progress update."""

        self.last_progress = description
        self.last_progress_at = self._now()

    def check(self) -> None:
        """Run one bounded progress check."""

        age = self._now() - self.last_progress_at
        if age > self.stall_seconds:
            normalized = self.operation.replace("-", "_").upper()
            raise ProtocolFailure(
                f"FAIL_{normalized}_STALLED",
                f"No {self.operation} progress for {age:.1f}s; "
                f"last progress: {self.last_progress}.",
            )


def subprocess_group_options(platform_name: str) -> dict[str, object]:
    """Return safe owned-process-group construction options for Popen."""

    if platform_name == "nt":
        return {
            "creationflags": getattr(subprocess, "CREATE_NEW_PROCESS_GROUP", 0x200),
            "start_new_session": False,
        }
    if platform_name == "posix":
        return {"creationflags": 0, "start_new_session": True}
    raise ValueError(f"Unsupported process platform: {platform_name}")
