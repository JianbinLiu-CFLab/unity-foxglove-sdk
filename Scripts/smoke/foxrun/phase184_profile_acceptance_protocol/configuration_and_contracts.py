#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Pure evidence protocol shared by the Phase184-G acceptance workers."""

from __future__ import annotations
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase184_profile_acceptance_protocol.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE

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


__all__ = [name for name in globals() if not name.startswith("__")]
