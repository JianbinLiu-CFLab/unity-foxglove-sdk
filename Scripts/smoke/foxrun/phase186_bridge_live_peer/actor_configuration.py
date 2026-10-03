#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Current-run live actors for Phase186-H Bridge acceptance.

Every actor consumes the exact coordinator-owned run configuration, writes a
token-hash/SHA-bound readiness document, and exits only after producing live
evidence.  No actor can write the Unity completion gate or the terminal PASS.
"""

from __future__ import annotations
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase186_bridge_live_peer.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE

import argparse
import asyncio
import contextlib
import json
import os
import pathlib
import socket
import struct
import sys
import tempfile
import time
from collections.abc import Callable, Mapping, Sequence
from typing import Any


SCRIPT_DIRECTORY = pathlib.Path(__file__).resolve().parent
if str(SCRIPT_DIRECTORY) not in sys.path:
    sys.path.insert(0, str(SCRIPT_DIRECTORY))

try:
    from Scripts.smoke.foxrun import phase186_bridge_acceptance_protocol as protocol
except ImportError:  # Direct execution through a ROS-owned Python runtime.
    import phase186_bridge_acceptance_protocol as protocol


ROLES = (
    "ros-peer",
    "graph-observer",
    "foxglove-client",
    "wire-peer",
    "hostile-peer",
)
MAX_DOCUMENT_BYTES = 4 * 1024 * 1024
MAX_FRAME_HEADER_BYTES = 65_536
MAX_FRAME_PAYLOAD_BYTES = 67_108_864
FOXGLOVE_SUBPROTOCOL = "foxglove.sdk.v1"
FOXGLOVE_MESSAGE_OPCODE = 1
BRIDGE_NODE_NAME = "unity2foxglove_ros2_bridge"
SOURCE_DELIVERY_SETTLE_SECONDS = 0.75
LIVE_ACTOR_OPERATION_TIMEOUT_SECONDS = 300.0
POST_RECONNECT_EXERCISE_CASES = frozenset(
    {"reconnect-degraded-recovery", "lifecycle"}
)
IDENTITY_GATE_KEYS = frozenset(
    {"schemaVersion", "runId", "caseId", "tokenHash", "head", "ready"}
)


class LiveActorFailure(protocol.ProtocolFailure):
    """Stable live-actor failure."""


def repository_root() -> pathlib.Path:
    """Handle repository root for Phase186 acceptance."""
    for candidate in (SCRIPT_DIRECTORY, *SCRIPT_DIRECTORY.parents):
        if (candidate / "Packages").is_dir() and (candidate / "Scripts").is_dir():
            return candidate
    raise LiveActorFailure("FAIL_PREFLIGHT", "repository root could not be located")


def _read_config(path: pathlib.Path) -> Mapping[str, Any]:
    """Read config."""
    target = pathlib.Path(path).resolve()
    try:
        if target.stat().st_size <= 0 or target.stat().st_size > MAX_DOCUMENT_BYTES:
            raise OSError("run config size is invalid")
        value = json.loads(target.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as exc:
        raise LiveActorFailure("FAIL_PREFLIGHT", "run config is unavailable") from exc
    if not isinstance(value, Mapping):
        raise LiveActorFailure("FAIL_PROTOCOL", "run config must be an object")
    return protocol.validate_run_config(value, repository_root())


def _write_json_atomic(path: pathlib.Path, value: Mapping[str, Any]) -> None:
    """Write json atomic."""
    target = pathlib.Path(path)
    target.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile(
        mode="w",
        encoding="utf-8",
        newline="\n",
        dir=target.parent,
        prefix=target.name + ".",
        suffix=".tmp",
        delete=False,
    ) as stream:
        json.dump(value, stream, indent=2, sort_keys=True)
        stream.write("\n")
        temporary = pathlib.Path(stream.name)
    os.replace(temporary, target)


def _actor_path(config: Mapping[str, Any], role: str, kind: str) -> pathlib.Path:
    """Handle actor path for Phase186 acceptance."""
    return pathlib.Path(str(config["outputRoot"])) / "actors" / f"{role}-{kind}.json"


def _write_actor_document(
    config: Mapping[str, Any],
    role: str,
    kind: str,
    evidence: Mapping[str, Any],
) -> pathlib.Path:
    """Write actor document."""
    if role not in ROLES or kind not in {"ready", "result"}:
        raise LiveActorFailure("FAIL_PROTOCOL", "actor document identity is invalid")
    document = {
        "schemaVersion": 1,
        "runId": config["runId"],
        "caseId": config["caseId"],
        "runtimeRowId": config["runtimeRowId"],
        "tokenHash": config["tokenHash"],
        "head": config["head"],
        "role": role,
        "kind": kind,
        "pid": os.getpid(),
        "verdict": "READY" if kind == "ready" else "PASS",
        "evidence": dict(evidence),
        "createdAt": protocol.timestamp(),
    }
    target = _actor_path(config, role, kind)
    _write_json_atomic(target, document)
    return target


def _write_cohosted_graph_ready(config: Mapping[str, Any]) -> pathlib.Path:
    """Declare that the independent ROS peer also owns the graph API view."""

    return _write_actor_document(
        config,
        "graph-observer",
        "ready",
        {
            "state": "independent-graph-api-ready",
            "processRole": "ros-peer",
            "cohosted": True,
        },
    )


def _write_cohosted_graph_result(
    config: Mapping[str, Any], evidence: Mapping[str, Any]
) -> pathlib.Path:
    """Write graph evidence without creating a third FastDDS participant."""

    value = dict(evidence)
    value["processRole"] = "ros-peer"
    value["cohosted"] = True
    return _write_actor_document(
        config,
        "graph-observer",
        "result",
        value,
    )


def _read_log(path: pathlib.Path) -> str:
    """Read log."""
    try:
        size = path.stat().st_size
        with path.open("rb") as stream:
            if size > MAX_DOCUMENT_BYTES:
                stream.seek(size - MAX_DOCUMENT_BYTES)
            return stream.read(MAX_DOCUMENT_BYTES).decode("utf-8", errors="replace")
    except OSError:
        return ""


def _has_unity_marker(config: Mapping[str, Any], prefix: str) -> bool:
    """Return whether unity marker."""
    identity = (
        f"run={config['runId']} case={config['caseId']} "
        f"tokenHash={config['tokenHash']} head={config['head']}"
    )
    return any(
        line.startswith(prefix + " ") and identity in line
        for line in _read_log(pathlib.Path(str(config["unityLog"]))).splitlines()
    )


def _wait_until(predicate, timeout_seconds: float, code: str, message: str) -> None:
    """Wait for until."""
    deadline = time.monotonic() + timeout_seconds
    while time.monotonic() < deadline:
        if predicate():
            return
        time.sleep(0.05)
    raise LiveActorFailure(code, message)


def _wait_for_unity_ready(config: Mapping[str, Any]) -> None:
    """Wait for for unity ready."""
    _wait_until(
        lambda: _has_unity_marker(config, "PHASE186_ACCEPTANCE_READY"),
        protocol.ACTOR_UNITY_READY_TIMEOUT_SECONDS,
        "FAIL_TERMINAL",
        "current-run Unity readiness marker expired",
    )


def _identity_gate_ready(config: Mapping[str, Any], key: str) -> bool:
    """Handle identity gate ready for Phase186 acceptance."""
    try:
        path = pathlib.Path(str(config[key]))
        if path.stat().st_size <= 0 or path.stat().st_size > MAX_DOCUMENT_BYTES:
            return False
        value = json.loads(path.read_text(encoding="utf-8"))
    except (KeyError, OSError, UnicodeError, json.JSONDecodeError):
        return False
    return (
        isinstance(value, Mapping)
        and set(value) == IDENTITY_GATE_KEYS
        and type(value.get("schemaVersion")) is int
        and value.get("schemaVersion") == 1
        and value.get("runId") == config.get("runId")
        and value.get("caseId") == config.get("caseId")
        and value.get("tokenHash") == config.get("tokenHash")
        and value.get("head") == config.get("head")
        and value.get("ready") is True
    )


def _wait_for_exercise_gate(config: Mapping[str, Any]) -> None:
    """Wait for for exercise gate."""
    _wait_until(
        lambda: _identity_gate_ready(config, "exerciseGate"),
        protocol.ACTOR_UNITY_READY_TIMEOUT_SECONDS,
        "FAIL_TERMINAL",
        "post-reconnect exercise gate expired",
    )


def _wait_for_ros_exercise_window(config: Mapping[str, Any]) -> None:
    """Wait for for ros exercise window."""
    _wait_for_unity_ready(config)
    if str(config["caseId"]) in POST_RECONNECT_EXERCISE_CASES:
        _wait_for_exercise_gate(config)


def _slow_unity_baseline_ready(config: Mapping[str, Any]) -> bool:
    """Handle slow unity baseline ready for Phase186 acceptance."""
    prefix = "PHASE186_ACCEPTANCE_PROGRESS "
    for line in _read_log(pathlib.Path(str(config["unityLog"]))).splitlines():
        if not line.startswith(prefix):
            continue
        fields: dict[str, str] = {}
        for part in line[len(prefix) :].split():
            if "=" in part:
                key, value = part.split("=", 1)
                fields[key] = value
        if (
            fields.get("run") != str(config["runId"])
            or fields.get("case") != str(config["caseId"])
            or fields.get("generated") != "true"
        ):
            continue
        try:
            received = int(fields.get("received", "0"))
            applied = int(fields.get("applied", "0"))
        except ValueError:
            continue
        if received >= 1 and applied >= 1:
            return True
    return False


def _sequence_windows(case_id: str, offered: int) -> tuple[range, ...]:
    """Handle sequence windows for Phase186 acceptance."""
    if offered <= 0:
        raise ValueError("offered sequence count must be positive")
    if case_id == "slow-main-thread-640hz":
        return (range(1, 2), range(2, offered + 1))
    return (range(1, offered + 1),)


def _layout(config: Mapping[str, Any]) -> tuple[tuple[str, str], ...]:
    """Handle layout for Phase186 acceptance."""
    kinds = protocol.CASE_CONTRACT_KINDS[str(config["caseId"])]
    topics = tuple(str(value) for value in config["topics"])
    return tuple(zip(topics, kinds, strict=True))


def _is_publish(kind: str) -> bool:
    """Return whether publish."""
    return kind.endswith("publish") or kind.endswith("duplex")


def _is_subscribe(kind: str) -> bool:
    """Return whether subscribe."""
    return kind.endswith("subscribe") or kind.endswith("duplex")


def _bridge_endpoints_ready(node: Any, config: Mapping[str, Any]) -> bool:
    """Require exact Bridge-owned endpoints, never the peer's duplex endpoint."""

    for topic, kind in _layout(config):
        expected_type = (
            protocol.INTERFACE_TYPE
            if kind.startswith("custom_")
            else "foxglove_msgs/msg/Log"
        )
        publishers = node.get_publishers_info_by_topic(topic)
        subscriptions = node.get_subscriptions_info_by_topic(topic)
        if _is_publish(kind):
            matching_publishers = [
                info for info in publishers if info.topic_type == expected_type
            ]
            bridge_publishers = [
                info
                for info in matching_publishers
                if str(getattr(info, "node_name", "")) == BRIDGE_NODE_NAME
            ]
            required_publishers = (
                2 if config["caseId"] == "fanout-fairness-health" else 1
            )
            if not bridge_publishers or len(matching_publishers) < required_publishers:
                return False
        if _is_subscribe(kind) and not any(
            info.topic_type == expected_type
            and str(getattr(info, "node_name", "")) == BRIDGE_NODE_NAME
            for info in subscriptions
        ):
            return False
    return True


def _load_ros_types():
    """Load ros types."""
    try:
        from foxglove_msgs.msg import Log
        from unity2foxglove_foxrun_interfaces_v1.msg import (
            Phase181NestedState3281D0E21244,
            Phase181State48D288ED82F1,
            Phase181State48D288ED82F1Envelope,
        )
    except ImportError as exc:
        raise LiveActorFailure(
            "FAIL_RUNTIME_SELECTION", "exact standard/custom ROS message overlay is unavailable"
        ) from exc
    return (
        Log,
        Phase181NestedState3281D0E21244,
        Phase181State48D288ED82F1,
        Phase181State48D288ED82F1Envelope,
    )


def _message_type(kind: str, standard_type, envelope_type):
    """Handle message type for Phase186 acceptance."""
    return envelope_type if kind.startswith("custom_") else standard_type


def _standard_message(standard_type, node, config: Mapping[str, Any], sequence: int):
    """Handle standard message for Phase186 acceptance."""
    value = standard_type()
    value.timestamp = node.get_clock().now().to_msg()
    value.level = 2
    value.message = (
        "phase186:"
        + str(config["tokenHash"])[:12]
        + f":{sequence}:external-a"
    )
    value.name = "Phase186ExternalPeer"
    value.file = "phase186_bridge_live_peer.py"
    value.line = 186
    return value


def _custom_message(
    envelope_type,
    payload_type,
    nested_type,
    node,
    config: Mapping[str, Any],
    sequence: int,
):
    """Handle custom message for Phase186 acceptance."""
    envelope = envelope_type()
    envelope.foxrun_origin_id = "phase186-external-" + str(config["tokenHash"])[:16]
    envelope.foxrun_sequence = sequence
    envelope.foxrun_stamp = node.get_clock().now().to_msg()
    payload = payload_type()
    payload.bytes = [0x01, 0x86, sequence & 0xFF]
    payload.foxrun_has_bytes = True
    payload.count = sequence
    payload.kind = 1
    payload.message = (
        "phase186:"
        + str(config["tokenHash"])[:12]
        + f":{sequence}:external-a"
    )
    payload.foxrun_has_message = True
    nested = nested_type()
    nested.enabled = True
    nested.label = "external-a"
    nested.foxrun_has_label = True
    payload.nested = nested
    payload.foxrun_has_nested = True
    payload.optional_count = sequence
    payload.foxrun_has_optional_count = True
    payload.optional_text = "external-a"
    payload.foxrun_has_optional_text = True
    payload.values = [sequence, sequence + 1]
    payload.foxrun_has_values = True
    envelope.payload = payload
    return envelope


__all__ = [name for name in globals() if not name.startswith("__")]
