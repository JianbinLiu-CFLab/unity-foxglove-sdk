#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Subscribe to /tf and inspect binary MessageData frames from a running Unity session.
# Usage: python Scripts/smoke/tf_websocket_smoke.py --port 8765
# Inputs: Unity Play Mode with a FoxgloveManager running; optional host, port, and receive limits.
# Outputs: Prints decoded /tf MessageData and Time frames.

"""Manual Foxglove WebSocket smoke test for /tf binary frames."""

from __future__ import annotations

import argparse
import asyncio
import json
import struct
import time
from dataclasses import dataclass

import websockets


# Default local Foxglove endpoint and subprotocol used by the Unity SDK.
DEFAULT_HOST = "127.0.0.1"
DEFAULT_PORT = 8765
FOXGLOVE_SUBPROTOCOL = "foxglove.sdk.v1"

# Manual subscription target and stable client subscription ID.
DEFAULT_TOPIC = "/tf"
TF_SUBSCRIPTION_ID = 100

# Receive loop defaults for this manual smoke probe.
DEFAULT_MAX_RECEIVE_FRAMES = 20
DEFAULT_RECEIVE_TIMEOUT_SECONDS = 3.0
DEFAULT_ADVERTISE_TIMEOUT_SECONDS = 10.0
DEFAULT_SETTLE_SECONDS = 0.25

# Foxglove binary frame opcodes and byte layout offsets.
MESSAGE_DATA_OPCODE = 1
TIME_OPCODE = 2
STRUCT_UNPACK_VALUE_INDEX = 0
OPCODE_OFFSET = 0
SUBSCRIPTION_ID_START = 1
SUBSCRIPTION_ID_END = 5
LOG_TIME_START = 5
LOG_TIME_END = 13
MESSAGE_PAYLOAD_START = 13
MIN_MESSAGE_DATA_FRAME_BYTES = MESSAGE_PAYLOAD_START
TIME_VALUE_START = 1
TIME_VALUE_END = 9

# Fallback values used when optional JSON fields are absent.
UNKNOWN_CHILD_FRAME = "?"
DEFAULT_TRANSLATION_X = 0

# Process exit code for a completed manual smoke run.
EXIT_SUCCESS = 0
EXIT_TOPIC_NOT_FOUND = 3


@dataclass(frozen=True)
class ChannelInfo:
    """Foxglove advertise metadata for one channel."""

    channel_id: int
    topic: str
    encoding: str


def build_ws_url(host: str, port: int) -> str:
    """Build the WebSocket URL for the local Unity Foxglove server."""
    return f"ws://{host}:{port}"


def build_subscribe_payload(channel_id: int, subscription_id: int) -> str:
    """Build the JSON subscribe operation for the discovered /tf channel."""
    return json.dumps(
        {
            "op": "subscribe",
            "subscriptions": [{"id": subscription_id, "channelId": channel_id}],
        },
        separators=(",", ":"),
    )


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
            )
            channels[channel.topic] = channel

        if topic in channels:
            return channels[topic]

    known = ", ".join(sorted(channels)) if channels else "(none)"
    raise TopicNotFoundError(f"Topic {topic!r} was not advertised. Known topics: {known}")


async def drain_for_seconds(ws: websockets.WebSocketClientProtocol, seconds: float) -> None:
    """Drain startup frames for a bounded interval before measuring."""
    end = time.perf_counter() + seconds
    while time.perf_counter() < end:
        try:
            await asyncio.wait_for(ws.recv(), timeout=max(0.01, end - time.perf_counter()))
        except asyncio.TimeoutError:
            break


def describe_message_payload(payload: bytes, encoding: str) -> str:
    """Return a compact payload description without assuming JSON encoding."""
    if encoding.lower() == "json":
        try:
            data = json.loads(payload)
        except json.JSONDecodeError:
            return f"json decode failed payloadBytes={len(payload)}"

        child = data.get("child_frame_id", UNKNOWN_CHILD_FRAME)
        tx = data.get("translation", {}).get("x", DEFAULT_TRANSLATION_X)
        return f"child={child} tx={tx}"

    return f"encoding={encoding or '(unknown)'} payloadBytes={len(payload)}"


async def run(args: argparse.Namespace) -> int:
    """Subscribe to /tf and print a bounded stream of decoded binary frames."""
    async with websockets.connect(build_ws_url(args.host, args.port), subprotocols=[FOXGLOVE_SUBPROTOCOL]) as ws:
        try:
            channel = await wait_for_channel(ws, args.topic, args.advertise_timeout_seconds)
        except TopicNotFoundError as exc:
            print("Verdict: TOPIC_NOT_FOUND")
            print(str(exc))
            return EXIT_TOPIC_NOT_FOUND

        print(f"Subscribing to {channel.topic} channelId={channel.channel_id} encoding={channel.encoding or '(unknown)'}")
        await ws.send(build_subscribe_payload(channel.channel_id, args.subscription_id))
        await drain_for_seconds(ws, args.settle_seconds)

        for index in range(args.max_frames):
            try:
                msg = await asyncio.wait_for(ws.recv(), timeout=args.timeout_seconds)
                if (
                    isinstance(msg, bytes)
                    and len(msg) >= MIN_MESSAGE_DATA_FRAME_BYTES
                    and msg[OPCODE_OFFSET] == MESSAGE_DATA_OPCODE
                ):
                    sub_id = struct.unpack("<I", msg[SUBSCRIPTION_ID_START:SUBSCRIPTION_ID_END])[
                        STRUCT_UNPACK_VALUE_INDEX
                    ]
                    log_ns = struct.unpack("<Q", msg[LOG_TIME_START:LOG_TIME_END])[STRUCT_UNPACK_VALUE_INDEX]
                    payload = msg[MESSAGE_PAYLOAD_START:]
                    details = describe_message_payload(payload, channel.encoding)
                    print(f"Msg #{index}: subId={sub_id} logTime={log_ns} {details}")
                elif isinstance(msg, bytes) and msg[OPCODE_OFFSET] == TIME_OPCODE:
                    t = struct.unpack("<Q", msg[TIME_VALUE_START:TIME_VALUE_END])[STRUCT_UNPACK_VALUE_INDEX]
                    print(f"Time: {t}")
            except asyncio.TimeoutError:
                print("timeout")
                break

    return EXIT_SUCCESS


def parse_args() -> argparse.Namespace:
    """Parse CLI arguments for the /tf WebSocket smoke client."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--host", default=DEFAULT_HOST)
    parser.add_argument("--port", type=int, default=DEFAULT_PORT)
    parser.add_argument("--topic", default=DEFAULT_TOPIC)
    parser.add_argument("--subscription-id", type=int, default=TF_SUBSCRIPTION_ID)
    parser.add_argument("--advertise-timeout-seconds", type=float, default=DEFAULT_ADVERTISE_TIMEOUT_SECONDS)
    parser.add_argument("--settle-seconds", type=float, default=DEFAULT_SETTLE_SECONDS)
    parser.add_argument("--max-frames", type=int, default=DEFAULT_MAX_RECEIVE_FRAMES)
    parser.add_argument("--timeout-seconds", type=float, default=DEFAULT_RECEIVE_TIMEOUT_SECONDS)
    return parser.parse_args()


def main() -> int:
    """Run the async smoke client from the synchronous CLI entry point."""
    return asyncio.run(run(parse_args()))


class TopicNotFoundError(RuntimeError):
    """Raised when the requested topic is absent from advertise frames."""


if __name__ == "__main__":
    raise SystemExit(main())
