#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Current-run live actors for Phase186-H Bridge acceptance.

Every actor consumes the exact coordinator-owned run configuration, writes a
token-hash/SHA-bound readiness document, and exits only after producing live
evidence.  No actor can write the Unity completion gate or the terminal PASS.
"""

from __future__ import annotations

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


def _message_text(value: object, kind: str) -> str:
    """Handle message text for Phase186 acceptance."""
    if kind.startswith("custom_"):
        return str(getattr(getattr(value, "payload", None), "message", ""))
    return str(getattr(value, "message", ""))


def _direct_peer_sequence(
    value: object,
    kind: str,
    token_hash: str,
    offered: int,
) -> int | None:
    """Return the exact current-run sequence for a peer-authored sample."""

    text = _message_text(value, kind)
    prefix = "phase186:" + token_hash[:12] + ":"
    suffix = ":external-a"
    if not text.startswith(prefix) or not text.endswith(suffix):
        return None
    encoded = text[len(prefix) : -len(suffix)]
    if not encoded or any(character < "0" or character > "9" for character in encoded):
        return None
    if len(encoded) > len(str(offered)):
        return None
    sequence = int(encoded)
    if str(sequence) != encoded or sequence < 1 or sequence > offered:
        return None
    if kind.startswith("custom_"):
        envelope_sequence = getattr(value, "foxrun_sequence", None)
        if type(envelope_sequence) is not int or envelope_sequence != sequence:
            return None
    return sequence


def _without_direct_peer_samples(
    samples: Sequence[object],
    kind: str,
    token_hash: str,
    offered: int,
    *,
    consume_direct: bool,
) -> list[object]:
    """Consume one peer self-delivery per exact current-run sequence.

    Jazzy rclpy does not expose the publisher GID in subscription callback
    message metadata.  A second copy of the same peer-authored sequence is
    therefore retained as evidence that the Bridge mirrored external input.
    """

    if not consume_direct:
        return list(samples)
    consumed: set[int] = set()
    remaining: list[object] = []
    for value in samples:
        sequence = _direct_peer_sequence(value, kind, token_hash, offered)
        if sequence is not None and sequence not in consumed:
            consumed.add(sequence)
            continue
        remaining.append(value)
    return remaining


def _outbound_texts(
    topic: str,
    received: Mapping[str, Sequence[object]],
    kinds: Mapping[str, str],
    token_hash: str,
    offered: int,
    publishers: Mapping[str, object],
) -> list[str]:
    """Handle outbound texts for Phase186 acceptance."""
    kind = kinds[topic]
    values = _without_direct_peer_samples(
        received[topic],
        kind,
        token_hash,
        offered,
        consume_direct=topic in publishers,
    )
    return [_message_text(value, kind) for value in values]


def _outbound_topic_ready(
    case_id: str,
    kind: str,
    texts: Sequence[str],
    token_hash: str,
) -> bool:
    """Handle outbound topic ready for Phase186 acceptance."""
    if kind.endswith("duplex") or case_id == "fanout-fairness-health":
        return any("unity-local-b" in text for text in texts)
    prefix = "phase186:" + token_hash[:12] + ":"
    return any(text.startswith(prefix) for text in texts)


def _outbound_wait_detail(
    case_id: str,
    expected_outbound: set[str],
    received: Mapping[str, Sequence[object]],
    kinds: Mapping[str, str],
    token_hash: str,
    offered: int,
    publishers: Mapping[str, object],
) -> str:
    """Handle outbound wait detail for Phase186 acceptance."""
    missing: list[str] = []
    observed: dict[str, list[str]] = {}
    for topic in sorted(expected_outbound):
        texts = _outbound_texts(
            topic,
            received,
            kinds,
            token_hash,
            offered,
            publishers,
        )
        observed[topic] = [text[:160] for text in texts[-8:]]
        if not _outbound_topic_ready(case_id, kinds[topic], texts, token_hash):
            missing.append(topic)
    return (
        "Unity Bridge outbound sample did not reach the exact ROS peer; missing="
        + json.dumps(missing, separators=(",", ":"))
        + " observed="
        + json.dumps(observed, separators=(",", ":"), sort_keys=True)
    )


def _spin_until(
    rclpy_module,
    node,
    predicate,
    timeout_seconds: float,
    message: str | Callable[[], str],
) -> None:
    """Handle spin until for Phase186 acceptance."""
    deadline = time.monotonic() + timeout_seconds
    while time.monotonic() < deadline:
        if predicate():
            return
        rclpy_module.spin_once(node, timeout_sec=0.05)
    detail = message() if callable(message) else message
    raise LiveActorFailure("FAIL_PEER", detail)


def _settle_source_delivery(rclpy_module, node, timeout_seconds: float) -> None:
    """Keep source publishers alive while reliable samples reach the Bridge."""

    if timeout_seconds <= 0:
        raise ValueError("source delivery timeout must be positive")
    deadline = time.monotonic() + timeout_seconds
    while True:
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            return
        rclpy_module.spin_once(node, timeout_sec=min(0.05, remaining))


def run_ros_peer(config: Mapping[str, Any]) -> Mapping[str, Any]:
    """Run ros peer."""
    try:
        import rclpy
        from rclpy.qos import DurabilityPolicy, HistoryPolicy, QoSProfile, ReliabilityPolicy
    except ImportError as exc:
        raise LiveActorFailure("FAIL_RUNTIME_SELECTION", "rclpy is unavailable") from exc
    standard, nested, payload, envelope = _load_ros_types()
    qos = QoSProfile(
        history=HistoryPolicy.KEEP_LAST,
        depth=32,
        reliability=ReliabilityPolicy.RELIABLE,
        durability=DurabilityPolicy.VOLATILE,
    )
    rclpy.init(args=None)
    node = None
    publishers: dict[str, Any] = {}
    subscriptions: dict[str, Any] = {}
    received: dict[str, list[object]] = {}
    kinds: dict[str, str] = {}
    try:
        node = rclpy.create_node("phase186_peer_" + str(config["tokenHash"])[:12])
        for topic, kind in _layout(config):
            kinds[topic] = kind
            message_type = _message_type(kind, standard, envelope)
            if _is_subscribe(kind):
                publishers[topic] = node.create_publisher(message_type, topic, qos)
            if _is_publish(kind):
                received[topic] = []

                def capture(value, *, selected=topic):
                    """Handle capture for Phase186 acceptance."""
                    received[selected].append(value)
                    if len(received[selected]) > 4096:
                        del received[selected][:-4096]

                subscriptions[topic] = node.create_subscription(
                    message_type, topic, capture, qos
                )
        _write_actor_document(
            config,
            "ros-peer",
            "ready",
            {
                "publishers": sorted(publishers),
                "subscriptions": sorted(subscriptions),
                "interfaceDigest": protocol.INTERFACE_DIGEST,
            },
        )
        if "graph-observer" in set(config["requiredActors"]):
            _write_cohosted_graph_ready(config)
        _wait_for_ros_exercise_window(config)
        _spin_until(
            rclpy,
            node,
            lambda: _bridge_endpoints_ready(node, config),
            LIVE_ACTOR_OPERATION_TIMEOUT_SECONDS,
            "Bridge ROS graph endpoints did not match the live peer",
        )
        if "graph-observer" in set(config["requiredActors"]):
            _write_cohosted_graph_result(
                config,
                _observe_graph(rclpy, node, config),
            )

        slow_case = config["caseId"] == "slow-main-thread-640hz"
        offered = 1280 if slow_case else 8
        token_hash = str(config["tokenHash"])
        sequence_windows = _sequence_windows(str(config["caseId"]), offered)
        for window_index, sequences in enumerate(sequence_windows):
            started = time.perf_counter()
            for window_offset, sequence in enumerate(sequences, start=1):
                for topic, publisher in publishers.items():
                    kind = kinds[topic]
                    value = (
                        _custom_message(
                            envelope,
                            payload,
                            nested,
                            node,
                            config,
                            sequence,
                        )
                        if kind.startswith("custom_")
                        else _standard_message(standard, node, config, sequence)
                    )
                    publisher.publish(value)
                if slow_case and window_index == 1:
                    deadline = started + window_offset / 640.0
                    remaining = deadline - time.perf_counter()
                    if remaining > 0:
                        time.sleep(remaining)
                if sequence % 16 == 0:
                    rclpy.spin_once(node, timeout_sec=0.0)

            if slow_case and window_index == 0:
                rclpy.spin_once(node, timeout_sec=0.0)
                _wait_until(
                    lambda: _slow_unity_baseline_ready(config),
                    LIVE_ACTOR_OPERATION_TIMEOUT_SECONDS,
                    "FAIL_TERMINAL",
                    "Unity did not apply the identity-bound slow-case baseline",
                )

        if publishers:
            _settle_source_delivery(
                rclpy,
                node,
                SOURCE_DELIVERY_SETTLE_SECONDS,
            )

        expected_outbound = set(subscriptions)

        def outbound_ready() -> bool:
            """Handle outbound ready for Phase186 acceptance."""
            for topic in expected_outbound:
                kind = kinds[topic]
                texts = _outbound_texts(
                    topic,
                    received,
                    kinds,
                    token_hash,
                    offered,
                    publishers,
                )
                if not _outbound_topic_ready(
                    str(config["caseId"]), kind, texts, token_hash
                ):
                    return False
            return True

        if expected_outbound:
            _spin_until(
                rclpy,
                node,
                outbound_ready,
                LIVE_ACTOR_OPERATION_TIMEOUT_SECONDS,
                lambda: _outbound_wait_detail(
                    str(config["caseId"]),
                    expected_outbound,
                    received,
                    kinds,
                    token_hash,
                    offered,
                    publishers,
                ),
            )
            duplicate_deadline = time.monotonic() + 0.75
            while time.monotonic() < duplicate_deadline:
                rclpy.spin_once(node, timeout_sec=0.05)

        outbound: dict[str, list[str]] = {}
        same_origin_republished = 0
        for topic, samples in received.items():
            kind = kinds[topic]
            values = _without_direct_peer_samples(
                samples,
                kind,
                token_hash,
                offered,
                consume_direct=topic in publishers,
            )
            texts = [_message_text(value, kind) for value in values]
            outbound[topic] = texts[-32:]
            if kind.endswith("duplex"):
                local = [value for value in values if "unity-local-b" in _message_text(value, kind)]
                if len(local) != 1:
                    raise LiveActorFailure(
                        "FAIL_ORIGIN", "later local mutation was not observed exactly once"
                    )
                if any("external-a" in text for text in texts):
                    raise LiveActorFailure(
                        "FAIL_ORIGIN", "external input was causally mirrored back to ROS"
                    )
                publisher = publishers.get(topic)
                if publisher is not None:
                    publisher.publish(local[0])
                    same_origin_republished += 1
        return {
            "offered": offered,
            "nominalHz": 640 if offered == 1280 else None,
            "productionSeconds": round(time.perf_counter() - started, 6),
            "outbound": outbound,
            "sameOriginRepublished": same_origin_republished,
            "interfaceType": protocol.INTERFACE_TYPE,
            "interfaceDigest": protocol.INTERFACE_DIGEST,
        }
    finally:
        if node is not None:
            node.destroy_node()
        rclpy.shutdown()


def _endpoint_document(info: object) -> dict[str, Any]:
    """Handle endpoint document for Phase186 acceptance."""
    qos = getattr(info, "qos_profile", None)
    return {
        "nodeName": str(getattr(info, "node_name", "")),
        "nodeNamespace": str(getattr(info, "node_namespace", "")),
        "topicType": str(getattr(info, "topic_type", "")),
        "reliability": str(getattr(getattr(qos, "reliability", None), "name", "")),
        "durability": str(getattr(getattr(qos, "durability", None), "name", "")),
        "history": str(getattr(getattr(qos, "history", None), "name", "")),
        "depth": int(getattr(qos, "depth", 0) or 0),
    }


def _observe_graph(
    rclpy_module: Any,
    node: Any,
    config: Mapping[str, Any],
) -> Mapping[str, Any]:
    """Capture exact Bridge endpoint and QoS evidence through rclpy graph APIs."""

    observed: dict[str, Any] = {}

    def graph_ready() -> bool:
        """Handle graph ready for Phase186 acceptance."""
        observed.clear()
        for topic, kind in _layout(config):
            publishers = node.get_publishers_info_by_topic(topic)
            subscriptions = node.get_subscriptions_info_by_topic(topic)
            expected_type = (
                protocol.INTERFACE_TYPE
                if kind.startswith("custom_")
                else "foxglove_msgs/msg/Log"
            )
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
                if (
                    not bridge_publishers
                    or len(matching_publishers) < required_publishers
                ):
                    return False
            if _is_subscribe(kind) and not any(
                info.topic_type == expected_type
                and str(getattr(info, "node_name", "")) == BRIDGE_NODE_NAME
                for info in subscriptions
            ):
                return False
            observed[topic] = {
                "expectedType": expected_type,
                "publishers": [_endpoint_document(info) for info in publishers],
                "subscriptions": [_endpoint_document(info) for info in subscriptions],
            }
        return True

    _spin_until(
        rclpy_module,
        node,
        graph_ready,
        LIVE_ACTOR_OPERATION_TIMEOUT_SECONDS,
        "independent graph observer did not find every exact Bridge endpoint",
    )
    return {"source": "rclpy-graph-api", "topics": observed}


def run_graph_observer(config: Mapping[str, Any]) -> Mapping[str, Any]:
    """Run graph observer."""
    try:
        import rclpy
    except ImportError as exc:
        raise LiveActorFailure("FAIL_RUNTIME_SELECTION", "rclpy is unavailable") from exc
    rclpy.init(args=None)
    node = None
    try:
        node = rclpy.create_node("phase186_graph_" + str(config["tokenHash"])[:12])
        _write_actor_document(
            config,
            "graph-observer",
            "ready",
            {"state": "independent-graph-api-ready"},
        )
        _wait_for_unity_ready(config)
        return _observe_graph(rclpy, node, config)
    finally:
        if node is not None:
            node.destroy_node()
        rclpy.shutdown()


async def _run_foxglove_async(config: Mapping[str, Any]) -> Mapping[str, Any]:
    """Run foxglove async."""
    try:
        import websockets
    except ImportError as exc:
        raise LiveActorFailure("FAIL_CLIENT", "Python websockets is unavailable") from exc
    _write_actor_document(
        config,
        "foxglove-client",
        "ready",
        {"state": "loopback-client-ready", "port": config["foxglovePort"]},
    )
    await asyncio.to_thread(_wait_for_unity_ready, config)
    url = f"ws://{config['foxgloveHost']}:{config['foxglovePort']}"
    deadline = time.monotonic() + LIVE_ACTOR_OPERATION_TIMEOUT_SECONDS
    websocket = None
    while websocket is None:
        try:
            websocket = await websockets.connect(url, subprotocols=[FOXGLOVE_SUBPROTOCOL])
        except OSError:
            if time.monotonic() >= deadline:
                raise LiveActorFailure("FAIL_CLIENT", "Foxglove listener did not become ready")
            await asyncio.sleep(0.1)
    try:
        expected = set(str(topic) for topic in config["topics"])
        channels: dict[str, dict[str, Any]] = {}
        while set(channels) != expected and time.monotonic() < deadline:
            frame = await asyncio.wait_for(websocket.recv(), timeout=5.0)
            if not isinstance(frame, str):
                continue
            try:
                value = json.loads(frame)
            except json.JSONDecodeError:
                continue
            if value.get("op") != "advertise":
                continue
            for item in value.get("channels", []):
                if isinstance(item, Mapping) and item.get("topic") in expected:
                    channels[str(item["topic"])] = dict(item)
        if set(channels) != expected:
            raise LiveActorFailure("FAIL_CLIENT", "Foxglove channel set is incomplete")
        subscriptions = []
        ids: dict[int, str] = {}
        for index, topic in enumerate(sorted(channels), start=1):
            subscription_id = 186000 + index
            ids[subscription_id] = topic
            subscriptions.append(
                {"id": subscription_id, "channelId": int(channels[topic]["id"])}
            )
        await websocket.send(
            json.dumps({"op": "subscribe", "subscriptions": subscriptions}, separators=(",", ":"))
        )
        delivered: dict[str, int] = {topic: 0 for topic in expected}
        required_count = 2 if config["caseId"] == "fanout-fairness-health" else 1
        while (
            any(count < required_count for count in delivered.values())
            and time.monotonic() < deadline
        ):
            frame = await asyncio.wait_for(websocket.recv(), timeout=5.0)
            if not isinstance(frame, bytes) or len(frame) < 13 or frame[0] != FOXGLOVE_MESSAGE_OPCODE:
                continue
            topic = ids.get(struct.unpack_from("<I", frame, 1)[0])
            if topic is not None and frame[13:]:
                delivered[topic] += 1
        if any(count < required_count for count in delivered.values()):
            raise LiveActorFailure("FAIL_CLIENT", "Foxglove did not deliver every fanout topic")
        return {
            "url": url,
            "deliveredTopics": sorted(delivered),
            "messageCounts": dict(sorted(delivered.items())),
            "channels": {
                topic: {
                    "encoding": str(value.get("encoding", "")),
                    "schemaName": str(value.get("schemaName", "")),
                }
                for topic, value in channels.items()
            },
        }
    finally:
        await websocket.close()


def _read_frame(connection: socket.socket, initial: bytes = b"") -> bytes:
    """Read frame."""
    fixed = bytearray(initial)
    if len(fixed) > 16:
        raise LiveActorFailure("FAIL_BRIDGE", "sidecar response prefix is oversized")
    while len(fixed) < 16:
        chunk = connection.recv(16 - len(fixed))
        if not chunk:
            raise LiveActorFailure("FAIL_BRIDGE", "sidecar closed during frame")
        fixed.extend(chunk)
    if fixed[:4] != b"U2R2":
        raise LiveActorFailure("FAIL_BRIDGE", "sidecar response magic differs")
    version, flags, header_size, payload_size = struct.unpack("<HHII", fixed[4:16])
    if (
        version != 1
        or flags != 0
        or not 0 < header_size <= MAX_FRAME_HEADER_BYTES
        or payload_size > MAX_FRAME_PAYLOAD_BYTES
    ):
        raise LiveActorFailure("FAIL_BRIDGE", "sidecar response header differs")
    total = 16 + header_size + payload_size
    while len(fixed) < total:
        chunk = connection.recv(total - len(fixed))
        if not chunk:
            raise LiveActorFailure("FAIL_BRIDGE", "sidecar response is truncated")
        fixed.extend(chunk)
    return bytes(fixed)


def _read_optional_frame(connection: socket.socket) -> bytes | None:
    """Read one optional response without discarding a fragmented prefix."""

    try:
        first = connection.recv(1)
    except socket.timeout as exc:
        raise LiveActorFailure(
            "FAIL_BRIDGE", "hostile connection did not reject within the bound"
        ) from exc
    except OSError:
        return None
    if not first:
        return None
    return _read_frame(connection, first)


def _decode_frame(frame: bytes) -> tuple[Mapping[str, Any], bytes]:
    """Handle decode frame for Phase186 acceptance."""
    if len(frame) < 16 or frame[:4] != b"U2R2":
        raise LiveActorFailure("FAIL_BRIDGE", "sidecar response framing differs")
    _version, _flags, header_size, payload_size = struct.unpack("<HHII", frame[4:16])
    if len(frame) != 16 + header_size + payload_size:
        raise LiveActorFailure("FAIL_BRIDGE", "sidecar response length differs")
    try:
        header = json.loads(frame[16 : 16 + header_size].decode("utf-8"))
    except (UnicodeError, json.JSONDecodeError) as exc:
        raise LiveActorFailure("FAIL_BRIDGE", "sidecar response JSON differs") from exc
    if not isinstance(header, Mapping):
        raise LiveActorFailure("FAIL_BRIDGE", "sidecar response is not an object")
    return header, frame[16 + header_size : 16 + header_size + payload_size]


def _encode_frame(header: Mapping[str, Any], payload: bytes = b"") -> bytes:
    """Handle encode frame for Phase186 acceptance."""
    encoded = json.dumps(
        dict(header), separators=(",", ":"), ensure_ascii=True
    ).encode("ascii")
    return b"U2R2" + struct.pack("<HHII", 1, 0, len(encoded), len(payload)) + encoded + payload


def _fixture(config: Mapping[str, Any]) -> Mapping[str, Any]:
    """Handle fixture for Phase186 acceptance."""
    path = (
        pathlib.Path(str(config["repository"]))
        / "Tools/ros2_bridge/unity2foxglove_ros2_bridge/test/fixtures/u2r2_protocol_vectors.json"
    )
    value = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(value, Mapping):
        raise LiveActorFailure("FAIL_EVIDENCE", "U2R2 fixture is malformed")
    return value


def _health(config: Mapping[str, Any], request_id: str) -> Mapping[str, Any]:
    """Handle health for Phase186 acceptance."""
    header = {"op": "health_ping", "requestId": request_id, "protocolVersion": 1}
    frame = _encode_frame(header)
    with socket.create_connection(
        (str(config["bridgeHost"]), int(config["bridgePort"])), timeout=2.0
    ) as connection:
        connection.settimeout(3.0)
        connection.sendall(frame)
        response, payload = _decode_frame(_read_frame(connection))
    if (
        response.get("op") != "health_pong"
        or response.get("requestId") != request_id
        or response.get("status") != "ok"
        or payload
    ):
        raise LiveActorFailure("FAIL_BRIDGE", "health response is uncorrelated")
    return response


def run_wire_peer(config: Mapping[str, Any]) -> Mapping[str, Any]:
    """Run wire peer."""
    fixture = _fixture(config)
    _write_actor_document(config, "wire-peer", "ready", {"state": "fixture-loaded"})
    health = fixture["health"]
    request = bytes.fromhex(health["request"]["frameHex"])
    with socket.create_connection(
        (str(config["bridgeHost"]), int(config["bridgePort"])), timeout=5.0
    ) as connection:
        connection.settimeout(5.0)
        connection.sendall(request)
        health_response, payload = _decode_frame(_read_frame(connection))
    if health_response != health["response"]["header"] or payload:
        raise LiveActorFailure("FAIL_BRIDGE", "frozen v1 health vector drifted")
    prepare = fixture["preparePublisher"]
    publish = fixture["publish"]
    with socket.create_connection(
        (str(config["bridgeHost"]), int(config["bridgePort"])), timeout=5.0
    ) as connection:
        connection.settimeout(5.0)
        connection.sendall(bytes.fromhex(prepare["request"]["frameHex"]))
        response, payload = _decode_frame(_read_frame(connection))
        if response != prepare["response"]["header"] or payload:
            raise LiveActorFailure("FAIL_BRIDGE", "frozen v1 prepare vector drifted")
        connection.sendall(bytes.fromhex(publish["frame"]["frameHex"]))
        time.sleep(0.25)
    return {
        "health": health_response.get("status") == "ok",
        "publishTopic": publish["topic"],
        "publishSequence": publish["sequence"],
        "fixtureVersion": fixture["fixtureVersion"],
    }


def _expect_rejection(config: Mapping[str, Any], frame: bytes) -> None:
    """Handle expect rejection for Phase186 acceptance."""
    with socket.create_connection(
        (str(config["bridgeHost"]), int(config["bridgePort"])), timeout=2.0
    ) as connection:
        connection.settimeout(2.0)
        connection.sendall(frame)
        with contextlib.suppress(OSError):
            connection.shutdown(socket.SHUT_WR)
        response = _read_optional_frame(connection)
        if response is not None:
            header, _payload = _decode_frame(response)
            if header.get("status") != "error":
                raise LiveActorFailure("FAIL_BRIDGE", "hostile frame was accepted")


def _hostile_mutations() -> Mapping[str, bytes]:
    """Handle hostile mutations for Phase186 acceptance."""
    unknown = _encode_frame({"op": "phase186_unknown_op"})
    return {
        "bad-magic": b"X2R2" + struct.pack("<HHII", 1, 0, 2, 0) + b"{}",
        "bad-version": b"U2R2" + struct.pack("<HHII", 2, 0, 2, 0) + b"{}",
        "oversized-header": b"U2R2" + struct.pack("<HHII", 1, 0, 65_537, 0),
        "oversized-payload": b"U2R2" + struct.pack("<HHII", 1, 0, 2, 67_108_865) + b"{}",
        "invalid-utf8": b"U2R2" + struct.pack("<HHII", 1, 0, 1, 0) + b"\xff",
        "trailing-root": b"U2R2" + struct.pack("<HHII", 1, 0, 4, 0) + b"{}{}",
        "unknown-op": unknown,
        "truncated-fixed": b"U2R2" + b"\x01" * 10,
    }


def _is_busy_response(header: Mapping[str, Any]) -> bool:
    """Return whether busy response."""
    return (
        header.get("op") == "busy"
        and header.get("status") == "error"
        and header.get("errorCode") == "busy"
        and header.get("terminal") is True
    )


def run_hostile_peer(config: Mapping[str, Any]) -> Mapping[str, Any]:
    """Run hostile peer."""
    _write_actor_document(
        config,
        "hostile-peer",
        "ready",
        {"state": "hostile-peer-ready"},
    )
    _wait_for_unity_ready(config)
    mutations = _hostile_mutations()
    passed: list[str] = []
    for index, (name, frame) in enumerate(mutations.items(), start=1):
        _expect_rejection(config, frame)
        _health(config, f"{config['token']}-hostile-{index}")
        passed.append(name)
    hello = next(
        item for item in _fixture(config)["v2"]["operations"] if item["id"] == "hello_request"
    )
    with socket.create_connection(
        (str(config["bridgeHost"]), int(config["bridgePort"])), timeout=3.0
    ) as connection:
        connection.settimeout(3.0)
        connection.sendall(bytes.fromhex(hello["frameHex"]))
        busy, _payload = _decode_frame(_read_frame(connection))
    if not _is_busy_response(busy):
        raise LiveActorFailure("FAIL_BRIDGE", "second data client did not receive busy")
    _health(config, str(config["token"]) + "-post-busy")
    return {
        "rejectedFamilies": passed,
        "secondClientCode": "busy",
        "healthAfterHostile": True,
    }


def run_role(config: Mapping[str, Any], role: str) -> Mapping[str, Any]:
    """Run role."""
    if role not in set(config["requiredActors"]):
        raise LiveActorFailure("FAIL_PROTOCOL", "worker role is not required by this case")
    if role == "ros-peer":
        return run_ros_peer(config)
    if role == "graph-observer":
        return run_graph_observer(config)
    if role == "foxglove-client":
        return asyncio.run(_run_foxglove_async(config))
    if role == "wire-peer":
        return run_wire_peer(config)
    if role == "hostile-peer":
        return run_hostile_peer(config)
    raise LiveActorFailure("FAIL_PROTOCOL", "unknown live actor role")


def parse_args(argv: Sequence[str] | None = None) -> argparse.Namespace:
    """Parse args."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--role", choices=ROLES, required=True)
    parser.add_argument("--run-config", type=pathlib.Path, required=True)
    return parser.parse_args(argv)


def main(argv: Sequence[str] | None = None) -> int:
    """Run the command-line entry point."""
    args = parse_args(argv)
    config: Mapping[str, Any] | None = None
    try:
        config = _read_config(args.run_config)
        evidence = run_role(config, args.role)
        path = _write_actor_document(config, args.role, "result", evidence)
        print(
            f"PHASE186_ACTOR_PASS role={args.role} run={config['runId']} "
            f"tokenHash={config['tokenHash']} evidence={path}",
            flush=True,
        )
        return 0
    except protocol.ProtocolFailure as exc:
        if config is not None:
            _write_json_atomic(
                _actor_path(config, args.role, "failure"),
                {
                    "schemaVersion": 1,
                    "runId": config["runId"],
                    "caseId": config["caseId"],
                    "tokenHash": config["tokenHash"],
                    "head": config["head"],
                    "role": args.role,
                    "verdict": "FAIL",
                    "failureCode": exc.code,
                    "failureMessage": str(exc)[:512],
                },
            )
        print(str(exc), file=sys.stderr, flush=True)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
