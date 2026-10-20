"""WebSocket advertisement and MessageData collection for Phase139."""

from __future__ import annotations

import asyncio
import json
import struct
import time
from typing import Any

from .argument_contract import *
from .protocol_surface import *
from .summary_models import *


async def run_websocket_core(args: argparse.Namespace) -> tuple[str, dict[str, ObservedTopic], list[str]]:
    """Collect WebSocket advertise and message evidence."""

    try:
        import websockets
    except ImportError as exc:
        raise RuntimeError("Python package 'websockets' is required for websocket-core mode.") from exc

    expectations = default_expectations(args)
    expected_by_topic = {item.topic: item for item in expectations}
    observed = {
        item.topic: ObservedTopic(item.topic, item.classification, item.family, note=item.note)
        for item in expectations
    }
    limitations: list[str] = []

    ssl_context = build_ssl_context(args.url, args.insecure)
    async with websockets.connect(args.url, subprotocols=[FOXGLOVE_SUBPROTOCOL], ssl=ssl_context) as websocket:
        channels = await collect_advertisements(websocket, set(expected_by_topic), args.advertise_timeout, args.idle_timeout)
        for channel in channels.values():
            topic = channel.get("topic")
            if topic not in observed:
                continue
            observed_topic = observed[topic]
            observed_topic.channel_id = int(channel.get("id"))
            observed_topic.encoding = channel.get("encoding")
            observed_topic.schema_name = channel.get("schemaName")

        missing_required = [
            topic for topic, item in expected_by_topic.items()
            if item.classification == "required" and observed[topic].channel_id is None
        ]
        if missing_required:
            limitations.append("Missing required WebSocket channel advertisements: " + ", ".join(sorted(missing_required)))
            return "fail", observed, limitations

        subscription_to_topic: dict[int, str] = {}
        subscriptions: list[dict[str, int]] = []
        subscription_id = 1
        for topic, item in expected_by_topic.items():
            channel_id = observed[topic].channel_id
            if channel_id is None:
                limitations.append(f"Optional topic was not advertised: {topic}")
                continue
            subscriptions.append({"id": subscription_id, "channelId": channel_id})
            subscription_to_topic[subscription_id] = topic
            subscription_id += 1

        if subscriptions:
            await websocket.send(json.dumps({"op": "subscribe", "subscriptions": subscriptions}))
            await collect_messages(websocket, observed, subscription_to_topic, args.duration, args.idle_timeout)

    failing_topics = [
        topic for topic, item in expected_by_topic.items()
        if item.classification == "required" and observed[topic].messages < item.min_messages
    ]
    if failing_topics:
        limitations.append("Missing required WebSocket message samples: " + ", ".join(sorted(failing_topics)))
        return "fail", observed, limitations

    metadata_topics = [
        topic for topic, item in expected_by_topic.items()
        if item.classification == "required"
        and (not observed[topic].encoding or not observed[topic].schema_name)
    ]
    if metadata_topics:
        limitations.append("Missing required WebSocket channel metadata: " + ", ".join(sorted(metadata_topics)))
        return "fail", observed, limitations

    empty_payload_topics = [
        topic for topic, item in expected_by_topic.items()
        if item.classification == "required"
        and observed[topic].messages > 0
        and observed[topic].payload_bytes == 0
    ]
    if empty_payload_topics:
        limitations.append("Required WebSocket topics returned empty payloads: " + ", ".join(sorted(empty_payload_topics)))
        return "fail", observed, limitations

    if any(item.classification == "optional" and observed[item.topic].messages == 0 for item in expectations):
        return "pass_with_limitations", observed, limitations
    return "pass", observed, limitations


async def collect_advertisements(
    websocket: Any,
    target_topics: set[str],
    advertise_timeout: float,
    idle_timeout: float,
) -> dict[int, dict[str, Any]]:
    """Collect channel advertisements until all targets are known or timeout expires."""

    channels: dict[int, dict[str, Any]] = {}
    advertised_topics: set[str] = set()
    deadline = time.monotonic() + advertise_timeout
    while time.monotonic() < deadline:
        try:
            frame = await asyncio.wait_for(websocket.recv(), timeout=min(idle_timeout, max(deadline - time.monotonic(), 0.1)))
        except asyncio.TimeoutError:
            continue
        if not isinstance(frame, str):
            continue
        try:
            message = json.loads(frame)
        except json.JSONDecodeError:
            continue
        if message.get("op") != "advertise":
            continue
        for channel in message.get("channels", []):
            channel_id = try_parse_channel_id(channel)
            if channel_id is None:
                continue
            channels[channel_id] = channel
            advertised_topics.add(channel.get("topic"))
        if target_topics.issubset(advertised_topics):
            break
    return channels


def try_parse_channel_id(channel: Any) -> int | None:
    """Return a valid advertised channel id, or None for malformed advertisements."""

    if not isinstance(channel, dict):
        return None
    try:
        return int(channel["id"])
    except (KeyError, TypeError, ValueError):
        return None


async def collect_messages(
    websocket: Any,
    observed: dict[str, ObservedTopic],
    subscription_to_topic: dict[int, str],
    duration: float,
    idle_timeout: float,
) -> None:
    """Collect binary MessageData frames for the subscribed topics."""

    deadline = time.monotonic() + duration
    while time.monotonic() < deadline:
        try:
            frame = await asyncio.wait_for(websocket.recv(), timeout=min(idle_timeout, max(deadline - time.monotonic(), 0.1)))
        except asyncio.TimeoutError:
            continue
        if not isinstance(frame, (bytes, bytearray)):
            continue
        data = frame if isinstance(frame, bytes) else bytes(frame)
        if len(data) < 13 or data[0] != MESSAGE_DATA_OPCODE:
            continue
        subscription_id = struct.unpack_from("<I", data, 1)[0]
        topic = subscription_to_topic.get(subscription_id)
        if topic is None:
            continue
        log_time = struct.unpack_from("<Q", data, 5)[0]
        item = observed[topic]
        now = time.monotonic()
        item.messages += 1
        item.payload_bytes += max(len(data) - 13, 0)
        item.first_monotonic_time = now if item.first_monotonic_time is None else item.first_monotonic_time
        item.last_monotonic_time = now
        item.first_log_time = log_time if item.first_log_time is None else item.first_log_time
        item.last_log_time = log_time



__all__ = [name for name in globals() if not name.startswith("__")]
