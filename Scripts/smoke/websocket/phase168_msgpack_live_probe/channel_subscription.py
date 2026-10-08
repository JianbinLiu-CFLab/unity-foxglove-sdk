"""Channel discovery and subscription operations for Phase168."""

from __future__ import annotations

import asyncio
import json
import time

import websockets

from .protocol_surface import *


async def wait_for_channel(ws: websockets.WebSocketClientProtocol, topic: str, timeout_seconds: float) -> ChannelInfo:
    """Wait for advertise frames until the requested topic appears."""
    deadline = time.perf_counter() + timeout_seconds
    channels: dict[str, ChannelInfo] = {}

    while time.perf_counter() < deadline:
        remaining = max(0.01, deadline - time.perf_counter())
        try:
            frame = await asyncio.wait_for(ws.recv(), timeout=remaining)
        except asyncio.TimeoutError:
            break
        if not isinstance(frame, str):
            continue

        try:
            message = json.loads(frame)
        except json.JSONDecodeError:
            continue

        if message.get("op") != "advertise":
            continue

        for raw_channel in message.get("channels", []):
            channel = ChannelInfo(
                channel_id=int(raw_channel.get("id", 0)),
                topic=str(raw_channel.get("topic", "")),
                encoding=str(raw_channel.get("encoding", "")),
                schema_name=str(raw_channel.get("schemaName", "")),
                schema=str(raw_channel.get("schema", "")),
                schema_encoding=str(raw_channel.get("schemaEncoding", "")),
            )
            channels[channel.topic] = channel

        if topic in channels:
            return channels[topic]

    known = ", ".join(sorted(channels)) if channels else "(none)"
    raise TopicNotFoundError(f"Topic {topic!r} was not advertised. Known topics: {known}")


def validate_channel(channel: ChannelInfo) -> tuple[str, str]:
    """Return a verdict and detail string for channel metadata."""
    if channel.encoding.lower() != EXPECTED_ENCODING:
        return "WRONG_CHANNEL", f"Expected encoding={EXPECTED_ENCODING}, got {channel.encoding or '(empty)'}."

    if channel.schema_name or channel.schema or channel.schema_encoding:
        return (
            "WRONG_CHANNEL",
            "MsgPack smoke should be schemaless: "
            f"schemaName={channel.schema_name!r} schemaEncoding={channel.schema_encoding!r}.",
        )

    return "OK", ""


async def subscribe(ws: websockets.WebSocketClientProtocol, channel_id: int, subscription_id: int) -> None:
    """Subscribe to one advertised channel."""
    payload = json.dumps(
        {
            "op": "subscribe",
            "subscriptions": [{"id": subscription_id, "channelId": channel_id}],
        },
        separators=(",", ":"),
    )
    await ws.send(payload)



__all__ = [name for name in globals() if not name.startswith("__")]
