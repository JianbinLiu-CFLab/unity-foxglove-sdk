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
from collections.abc import Mapping, Sequence
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


class LiveActorFailure(protocol.ProtocolFailure):
    """Stable live-actor failure."""


def repository_root() -> pathlib.Path:
    for candidate in (SCRIPT_DIRECTORY, *SCRIPT_DIRECTORY.parents):
        if (candidate / "Packages").is_dir() and (candidate / "Scripts").is_dir():
            return candidate
    raise LiveActorFailure("FAIL_PREFLIGHT", "repository root could not be located")


def _read_config(path: pathlib.Path) -> Mapping[str, Any]:
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
    return pathlib.Path(str(config["outputRoot"])) / "actors" / f"{role}-{kind}.json"


def _write_actor_document(
    config: Mapping[str, Any],
    role: str,
    kind: str,
    evidence: Mapping[str, Any],
) -> pathlib.Path:
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
    try:
        size = path.stat().st_size
        with path.open("rb") as stream:
            if size > MAX_DOCUMENT_BYTES:
                stream.seek(size - MAX_DOCUMENT_BYTES)
            return stream.read(MAX_DOCUMENT_BYTES).decode("utf-8", errors="replace")
    except OSError:
        return ""


def _has_unity_marker(config: Mapping[str, Any], prefix: str) -> bool:
    identity = (
        f"run={config['runId']} case={config['caseId']} "
        f"tokenHash={config['tokenHash']} head={config['head']}"
    )
    return any(
        line.startswith(prefix + " ") and identity in line
        for line in _read_log(pathlib.Path(str(config["unityLog"]))).splitlines()
    )


def _wait_until(predicate, timeout_seconds: float, code: str, message: str) -> None:
    deadline = time.monotonic() + timeout_seconds
    while time.monotonic() < deadline:
        if predicate():
            return
        time.sleep(0.05)
    raise LiveActorFailure(code, message)


def _wait_for_unity_ready(config: Mapping[str, Any]) -> None:
    _wait_until(
        lambda: _has_unity_marker(config, "PHASE186_ACCEPTANCE_READY"),
        protocol.ACTOR_UNITY_READY_TIMEOUT_SECONDS,
        "FAIL_TERMINAL",
        "current-run Unity readiness marker expired",
    )


def _layout(config: Mapping[str, Any]) -> tuple[tuple[str, str], ...]:
    kinds = protocol.CASE_CONTRACT_KINDS[str(config["caseId"])]
    topics = tuple(str(value) for value in config["topics"])
    return tuple(zip(topics, kinds, strict=True))


def _is_publish(kind: str) -> bool:
    return kind.endswith("publish") or kind.endswith("duplex")


def _is_subscribe(kind: str) -> bool:
    return kind.endswith("subscribe") or kind.endswith("duplex")


def _load_ros_types():
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
    return envelope_type if kind.startswith("custom_") else standard_type


def _standard_message(standard_type, node, config: Mapping[str, Any], sequence: int):
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


def _spin_until(rclpy_module, node, predicate, timeout_seconds: float, message: str) -> None:
    deadline = time.monotonic() + timeout_seconds
    while time.monotonic() < deadline:
        if predicate():
            return
        rclpy_module.spin_once(node, timeout_sec=0.05)
    raise LiveActorFailure("FAIL_PEER", message)


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
    node = rclpy.create_node("phase186_peer_" + str(config["tokenHash"])[:12])
    publishers: dict[str, Any] = {}
    subscriptions: dict[str, Any] = {}
    received: dict[str, list[object]] = {}
    kinds: dict[str, str] = {}
    try:
        for topic, kind in _layout(config):
            kinds[topic] = kind
            message_type = _message_type(kind, standard, envelope)
            if _is_subscribe(kind):
                publishers[topic] = node.create_publisher(message_type, topic, qos)
            if _is_publish(kind):
                received[topic] = []

                def capture(value, *, selected=topic):
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
        _wait_for_unity_ready(config)
        _spin_until(
            rclpy,
            node,
            lambda: all(pub.get_subscription_count() > 0 for pub in publishers.values())
            and all(sub.get_publisher_count() > 0 for sub in subscriptions.values()),
            90.0,
            "Bridge ROS graph endpoints did not match the live peer",
        )
        if "graph-observer" in set(config["requiredActors"]):
            _write_cohosted_graph_result(
                config,
                _observe_graph(rclpy, node, config),
            )

        offered = 1280 if config["caseId"] == "slow-main-thread-640hz" else 8
        token_hash = str(config["tokenHash"])
        started = time.perf_counter()
        for sequence in range(1, offered + 1):
            for topic, publisher in publishers.items():
                kind = kinds[topic]
                value = (
                    _custom_message(envelope, payload, nested, node, config, sequence)
                    if kind.startswith("custom_")
                    else _standard_message(standard, node, config, sequence)
                )
                publisher.publish(value)
            if offered > 100:
                deadline = started + sequence / 640.0
                remaining = deadline - time.perf_counter()
                if remaining > 0:
                    time.sleep(remaining)
            if sequence % 16 == 0:
                rclpy.spin_once(node, timeout_sec=0.0)

        if publishers:
            _settle_source_delivery(
                rclpy,
                node,
                SOURCE_DELIVERY_SETTLE_SECONDS,
            )

        expected_outbound = set(subscriptions)

        def outbound_ready() -> bool:
            for topic in expected_outbound:
                kind = kinds[topic]
                values = _without_direct_peer_samples(
                    received[topic],
                    kind,
                    token_hash,
                    offered,
                    consume_direct=topic in publishers,
                )
                texts = [_message_text(value, kind) for value in values]
                if kind.endswith("duplex"):
                    if not any("unity-local-b" in text for text in texts):
                        return False
                elif config["caseId"] == "fanout-fairness-health":
                    if not any("unity-local-b" in text for text in texts):
                        return False
                elif not any(
                    text.startswith("phase186:" + str(config["tokenHash"])[:12] + ":")
                    for text in texts
                ):
                    return False
            return True

        if expected_outbound:
            _spin_until(
                rclpy,
                node,
                outbound_ready,
                90.0,
                "Unity Bridge outbound sample did not reach the exact ROS peer",
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
        node.destroy_node()
        rclpy.shutdown()


def _endpoint_document(info: object) -> dict[str, Any]:
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
        90.0,
        "independent graph observer did not find every exact Bridge endpoint",
    )
    return {"source": "rclpy-graph-api", "topics": observed}


def run_graph_observer(config: Mapping[str, Any]) -> Mapping[str, Any]:
    try:
        import rclpy
    except ImportError as exc:
        raise LiveActorFailure("FAIL_RUNTIME_SELECTION", "rclpy is unavailable") from exc
    rclpy.init(args=None)
    node = rclpy.create_node("phase186_graph_" + str(config["tokenHash"])[:12])
    try:
        _write_actor_document(
            config,
            "graph-observer",
            "ready",
            {"state": "independent-graph-api-ready"},
        )
        _wait_for_unity_ready(config)
        return _observe_graph(rclpy, node, config)
    finally:
        node.destroy_node()
        rclpy.shutdown()


async def _run_foxglove_async(config: Mapping[str, Any]) -> Mapping[str, Any]:
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
    deadline = time.monotonic() + 90.0
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
    encoded = json.dumps(
        dict(header), separators=(",", ":"), ensure_ascii=True
    ).encode("ascii")
    return b"U2R2" + struct.pack("<HHII", 1, 0, len(encoded), len(payload)) + encoded + payload


def _fixture(config: Mapping[str, Any]) -> Mapping[str, Any]:
    path = (
        pathlib.Path(str(config["repository"]))
        / "Tools/ros2_bridge/unity2foxglove_ros2_bridge/test/fixtures/u2r2_protocol_vectors.json"
    )
    value = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(value, Mapping):
        raise LiveActorFailure("FAIL_EVIDENCE", "U2R2 fixture is malformed")
    return value


def _health(config: Mapping[str, Any], request_id: str) -> Mapping[str, Any]:
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


def run_hostile_peer(config: Mapping[str, Any]) -> Mapping[str, Any]:
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
    if busy.get("status") != "error" or busy.get("code") != "busy":
        raise LiveActorFailure("FAIL_BRIDGE", "second data client did not receive busy")
    _health(config, str(config["token"]) + "-post-busy")
    return {
        "rejectedFamilies": passed,
        "secondClientCode": "busy",
        "healthAfterHostile": True,
    }


def run_role(config: Mapping[str, Any], role: str) -> Mapping[str, Any]:
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
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--role", choices=ROLES, required=True)
    parser.add_argument("--run-config", type=pathlib.Path, required=True)
    return parser.parse_args(argv)


def main(argv: Sequence[str] | None = None) -> int:
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
