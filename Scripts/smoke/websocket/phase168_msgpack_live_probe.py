#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Verify Phase168 live MsgPack bytes over the Foxglove WebSocket protocol.
# Usage: python Scripts/smoke/websocket/phase168_msgpack_live_probe.py --port 8765
# Inputs: Unity Play Mode with Phase168MsgPackSmoke explicitly allowing unsupported live publish.
# Outputs: Channel metadata, decoded MsgPack payload fields, and a PASS/FAIL verdict.

"""Protocol-level live smoke test for the Phase168 MsgPack raw channel."""

from __future__ import annotations

import argparse
import asyncio
import json
import math
import ssl
import struct
import time
from dataclasses import dataclass
from urllib.parse import parse_qsl, urlencode, urlsplit, urlunsplit

import websockets


DEFAULT_HOST = "127.0.0.1"
DEFAULT_PORT = 8765
DEFAULT_TOPIC = "/phase168/msgpack_smoke"
DEFAULT_ADVERTISE_TIMEOUT_SECONDS = 10.0
DEFAULT_IDLE_TIMEOUT_SECONDS = 15.0
DEFAULT_SETTLE_SECONDS = 0.25
DEFAULT_SUBSCRIPTION_ID = 168_000
FOXGLOVE_SUBPROTOCOL = "foxglove.sdk.v1"

EXPECTED_ENCODING = "msgpack"
EXPECTED_PHASE = 168

MESSAGE_DATA_OPCODE = 1
TIME_OPCODE = 2
OPCODE_OFFSET = 0
SUBSCRIPTION_ID_START = 1
LOG_TIME_START = 5
MESSAGE_PAYLOAD_START = 13
MIN_MESSAGE_DATA_FRAME_BYTES = MESSAGE_PAYLOAD_START

EXIT_SUCCESS = 0
EXIT_FAILURE = 1
EXIT_TOPIC_NOT_FOUND = 3
EXIT_WRONG_CHANNEL = 4
EXIT_NO_MESSAGES = 5
EXIT_DECODE_FAILURE = 6


@dataclass(frozen=True)
class ChannelInfo:
    """Foxglove advertise metadata for one channel."""

    channel_id: int
    topic: str
    encoding: str
    schema_name: str
    schema: str
    schema_encoding: str


@dataclass(frozen=True)
class MsgPackSample:
    """Decoded Phase168 sample payload."""

    phase: int
    seq: int
    time_sec: float
    source: str
    active: bool
    position: tuple[float, float, float]
    payload_bytes: int
    log_time_ns: int


class TopicNotFoundError(RuntimeError):
    """Raised when the requested topic is not advertised."""


class MsgPackDecodeError(RuntimeError):
    """Raised when a MessagePack payload is malformed or unexpected."""


def build_url(args: argparse.Namespace) -> str:
    """Build the websocket URL from explicit URL or host/port arguments."""
    if args.url:
        url = args.url
    else:
        scheme = "wss" if args.wss else "ws"
        url = f"{scheme}://{args.host}:{args.port}"

    if args.token:
        url = append_query_parameter(url, "token", args.token)

    return url


def append_query_parameter(url: str, key: str, value: str) -> str:
    """Append or replace one query parameter without disturbing the URL path."""
    parts = urlsplit(url)
    query = dict(parse_qsl(parts.query, keep_blank_values=True))
    query[key] = value
    return urlunsplit((parts.scheme, parts.netloc, parts.path, urlencode(query), parts.fragment))


def build_ssl_context(url: str, insecure: bool) -> ssl.SSLContext | None:
    """Return a TLS context for wss endpoints, optionally disabling validation."""
    if not url.lower().startswith("wss://"):
        return None

    if not insecure:
        return ssl.create_default_context()

    context = ssl.create_default_context()
    context.check_hostname = False
    context.verify_mode = ssl.CERT_NONE
    return context


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


async def drain_for_seconds(ws: websockets.WebSocketClientProtocol, seconds: float) -> None:
    """Drain startup frames so the probe waits for post-subscribe MessageData."""
    end = time.perf_counter() + seconds
    while time.perf_counter() < end:
        try:
            await asyncio.wait_for(ws.recv(), timeout=max(0.01, end - time.perf_counter()))
        except asyncio.TimeoutError:
            break


async def wait_for_msgpack_sample(
    ws: websockets.WebSocketClientProtocol,
    subscription_id: int,
    timeout_seconds: float,
) -> MsgPackSample:
    """Wait for one binary MessageData frame for the subscription and decode it."""
    deadline = time.perf_counter() + timeout_seconds
    while time.perf_counter() < deadline:
        remaining = max(0.01, deadline - time.perf_counter())
        try:
            frame = await asyncio.wait_for(ws.recv(), timeout=remaining)
        except asyncio.TimeoutError:
            break

        if not isinstance(frame, bytes):
            continue
        if len(frame) < MIN_MESSAGE_DATA_FRAME_BYTES:
            continue
        if frame[OPCODE_OFFSET] == TIME_OPCODE:
            continue
        if frame[OPCODE_OFFSET] != MESSAGE_DATA_OPCODE:
            continue

        sub_id = struct.unpack_from("<I", frame, SUBSCRIPTION_ID_START)[0]
        if sub_id != subscription_id:
            continue

        log_time_ns = struct.unpack_from("<Q", frame, LOG_TIME_START)[0]
        payload = frame[MESSAGE_PAYLOAD_START:]
        value = decode_complete_msgpack(payload)
        return validate_sample(value, len(payload), log_time_ns)

    raise TimeoutError(
        "No MsgPack MessageData received. In Unity, enable 'Allow Unsupported Live WebSocket Publish' "
        "on Phase168MsgPackSmoke and either enable continuous publish or run 'MsgPack Smoke/Publish Once'."
    )


def decode_complete_msgpack(data: bytes) -> object:
    """Decode one complete MessagePack value from a payload."""
    index, value = decode_msgpack_value(data, 0)
    if index != len(data):
        raise MsgPackDecodeError(f"Trailing bytes after MsgPack payload: {len(data) - index}.")
    return value


def decode_msgpack_value(data: bytes, index: int) -> tuple[int, object]:
    """Decode the subset of MessagePack emitted by Phase168MsgPackSmoke."""
    index = require_available(data, index, 1)
    marker = data[index]
    index += 1

    if marker <= 0x7F:
        return index, marker
    if 0x80 <= marker <= 0x8F:
        return decode_msgpack_map(data, index, marker & 0x0F)
    if 0x90 <= marker <= 0x9F:
        return decode_msgpack_array(data, index, marker & 0x0F)
    if 0xA0 <= marker <= 0xBF:
        return decode_msgpack_string(data, index, marker & 0x1F)
    if marker >= 0xE0:
        return index, marker - 0x100
    if marker == 0xC0:
        return index, None
    if marker == 0xC2:
        return index, False
    if marker == 0xC3:
        return index, True
    if marker == 0xCA:
        return read_struct(data, index, ">f")
    if marker == 0xCB:
        return read_struct(data, index, ">d")
    if marker == 0xCC:
        return read_struct(data, index, ">B")
    if marker == 0xCD:
        return read_struct(data, index, ">H")
    if marker == 0xCE:
        return read_struct(data, index, ">I")
    if marker == 0xCF:
        return read_struct(data, index, ">Q")
    if marker == 0xD0:
        return read_struct(data, index, ">b")
    if marker == 0xD1:
        return read_struct(data, index, ">h")
    if marker == 0xD2:
        return read_struct(data, index, ">i")
    if marker == 0xD3:
        return read_struct(data, index, ">q")
    if marker == 0xD9:
        index, length = read_struct(data, index, ">B")
        return decode_msgpack_string(data, index, int(length))
    if marker == 0xDA:
        index, length = read_struct(data, index, ">H")
        return decode_msgpack_string(data, index, int(length))
    if marker == 0xDB:
        index, length = read_struct(data, index, ">I")
        return decode_msgpack_string(data, index, int(length))
    if marker == 0xDC:
        index, count = read_struct(data, index, ">H")
        return decode_msgpack_array(data, index, int(count))
    if marker == 0xDD:
        index, count = read_struct(data, index, ">I")
        return decode_msgpack_array(data, index, int(count))
    if marker == 0xDE:
        index, count = read_struct(data, index, ">H")
        return decode_msgpack_map(data, index, int(count))
    if marker == 0xDF:
        index, count = read_struct(data, index, ">I")
        return decode_msgpack_map(data, index, int(count))

    raise MsgPackDecodeError(f"Unsupported MessagePack marker 0x{marker:02x}.")


def decode_msgpack_map(data: bytes, index: int, count: int) -> tuple[int, dict[object, object]]:
    """Decode a MessagePack map."""
    result: dict[object, object] = {}
    for _ in range(count):
        index, key = decode_msgpack_value(data, index)
        index, value = decode_msgpack_value(data, index)
        result[key] = value
    return index, result


def decode_msgpack_array(data: bytes, index: int, count: int) -> tuple[int, list[object]]:
    """Decode a MessagePack array."""
    result: list[object] = []
    for _ in range(count):
        index, value = decode_msgpack_value(data, index)
        result.append(value)
    return index, result


def decode_msgpack_string(data: bytes, index: int, length: int) -> tuple[int, str]:
    """Decode a UTF-8 MessagePack string."""
    index = require_available(data, index, length)
    try:
        value = data[index : index + length].decode("utf-8")
    except UnicodeDecodeError as exc:
        raise MsgPackDecodeError("MessagePack string is not valid UTF-8.") from exc
    return index + length, value


def read_struct(data: bytes, index: int, fmt: str) -> tuple[int, object]:
    """Read a fixed-width big-endian MessagePack scalar."""
    size = struct.calcsize(fmt)
    index = require_available(data, index, size)
    return index + size, struct.unpack_from(fmt, data, index)[0]


def require_available(data: bytes, index: int, count: int) -> int:
    """Validate that count bytes are available at index."""
    if index < 0 or count < 0 or index > len(data) - count:
        raise MsgPackDecodeError("MessagePack payload ended early.")
    return index


def validate_sample(value: object, payload_bytes: int, log_time_ns: int) -> MsgPackSample:
    """Validate decoded Phase168 fields and return a typed sample."""
    if not isinstance(value, dict):
        raise MsgPackDecodeError("Phase168 payload is not a MsgPack map.")

    phase = value.get("phase")
    seq = value.get("seq")
    time_sec = value.get("timeSec")
    source = value.get("source")
    active = value.get("active")
    position = value.get("position")

    if phase != EXPECTED_PHASE:
        raise MsgPackDecodeError(f"Expected phase == 168, got {phase!r}.")
    if not isinstance(seq, int) or seq < 1:
        raise MsgPackDecodeError(f"Expected positive integer seq, got {seq!r}.")
    if not isinstance(time_sec, (int, float)) or not math.isfinite(float(time_sec)):
        raise MsgPackDecodeError(f"Expected finite timeSec, got {time_sec!r}.")
    if not isinstance(source, str) or not source:
        raise MsgPackDecodeError(f"Expected non-empty source string, got {source!r}.")
    if not isinstance(active, bool):
        raise MsgPackDecodeError(f"Expected boolean active, got {active!r}.")
    if not isinstance(position, list) or len(position) != 3:
        raise MsgPackDecodeError(f"Expected 3-value position array, got {position!r}.")
    if not all(isinstance(item, (int, float)) and math.isfinite(float(item)) for item in position):
        raise MsgPackDecodeError(f"Expected finite numeric position values, got {position!r}.")

    return MsgPackSample(
        phase=int(phase),
        seq=int(seq),
        time_sec=float(time_sec),
        source=source,
        active=active,
        position=(float(position[0]), float(position[1]), float(position[2])),
        payload_bytes=payload_bytes,
        log_time_ns=log_time_ns,
    )


async def run(args: argparse.Namespace) -> int:
    """Connect, subscribe, decode one sample, and print a verdict."""
    url = build_url(args)
    args.effective_url = redact_token(url)
    ssl_context = build_ssl_context(url, args.insecure)

    try:
        async with websockets.connect(url, subprotocols=[FOXGLOVE_SUBPROTOCOL], ssl=ssl_context) as ws:
            channel = await wait_for_channel(ws, args.topic, args.advertise_timeout_seconds)
            verdict, detail = validate_channel(channel)
            print_channel(args.effective_url, channel)
            if verdict != "OK":
                print(f"Verdict: {verdict}")
                print(detail)
                return EXIT_WRONG_CHANNEL

            await subscribe(ws, channel.channel_id, args.subscription_id)
            if args.settle_seconds > 0:
                await drain_for_seconds(ws, args.settle_seconds)
            sample = await wait_for_msgpack_sample(ws, args.subscription_id, args.idle_timeout_seconds)
    except TopicNotFoundError as exc:
        print("Verdict: TOPIC_NOT_FOUND")
        print(str(exc))
        return EXIT_TOPIC_NOT_FOUND
    except TimeoutError as exc:
        print("Verdict: NO_MESSAGES")
        print(str(exc))
        return EXIT_NO_MESSAGES
    except MsgPackDecodeError as exc:
        print("Verdict: DECODE_FAILURE")
        print(str(exc))
        return EXIT_DECODE_FAILURE

    print_sample(sample)
    print("Verdict: PASS")
    return EXIT_SUCCESS


def print_channel(endpoint: str, channel: ChannelInfo) -> None:
    """Print channel metadata."""
    print(f"Endpoint: {endpoint}")
    print(f"Topic: {channel.topic}")
    print(
        "Channel: "
        f"id={channel.channel_id}, "
        f"encoding={channel.encoding or '(unknown)'}, "
        f"schemaName={channel.schema_name!r}, "
        f"schemaEncoding={channel.schema_encoding!r}"
    )


def print_sample(sample: MsgPackSample) -> None:
    """Print decoded sample details."""
    x, y, z = sample.position
    print(
        "Decoded sample: "
        f"phase={sample.phase}, seq={sample.seq}, active={sample.active}, "
        f"source={sample.source!r}, position=({x:.3f}, {y:.3f}, {z:.3f}), "
        f"timeSec={sample.time_sec:.6f}, logTimeNs={sample.log_time_ns}, "
        f"payloadBytes={sample.payload_bytes}"
    )


def redact_token(url: str) -> str:
    """Hide token query values in console output."""
    parts = urlsplit(url)
    query = parse_qsl(parts.query, keep_blank_values=True)
    redacted = [(key, "REDACTED" if key == "token" else value) for key, value in query]
    return urlunsplit((parts.scheme, parts.netloc, parts.path, urlencode(redacted), parts.fragment))


def run_self_test() -> int:
    """Run offline decoder checks without a Unity session."""
    sample = (
        b"\x86"
        b"\xa5phase\xcd\x00\xa8"
        b"\xa3seq\x01"
        b"\xa7timeSec\xcb@\x09!\xfbTD-\x18"
        b"\xa6source\xa5smoke"
        b"\xa6active\xc3"
        b"\xa8position\x93\xca?\x80\x00\x00\xca@\x00\x00\x00\xca@@\x00\x00"
    )
    decoded = validate_sample(decode_complete_msgpack(sample), len(sample), 123)
    if decoded.phase != 168 or decoded.seq != 1 or decoded.source != "smoke" or decoded.position != (1.0, 2.0, 3.0):
        print("self-test failed: decoded sample fields did not match")
        return EXIT_FAILURE

    try:
        decode_complete_msgpack(b"\x81\xa5phase")
    except MsgPackDecodeError:
        print("self-test pass: decoded Phase168 MsgPack sample and rejected malformed bytes")
        return EXIT_SUCCESS

    print("self-test failed: malformed bytes decoded unexpectedly")
    return EXIT_FAILURE


def parse_args() -> argparse.Namespace:
    """Parse command line arguments."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--url", help="Full ws:// or wss:// endpoint. Overrides host/port/wss.")
    parser.add_argument("--host", default=DEFAULT_HOST)
    parser.add_argument("--port", type=int, default=DEFAULT_PORT)
    parser.add_argument("--wss", action="store_true", help="Use wss:// when --url is omitted.")
    parser.add_argument("--insecure", action="store_true", help="Skip TLS certificate validation for local WSS smoke tests.")
    parser.add_argument("--token", default="", help="Shared token gate value. Appended as ?token=...")
    parser.add_argument("--topic", default=DEFAULT_TOPIC)
    parser.add_argument("--subscription-id", type=int, default=DEFAULT_SUBSCRIPTION_ID)
    parser.add_argument("--advertise-timeout-seconds", type=float, default=DEFAULT_ADVERTISE_TIMEOUT_SECONDS)
    parser.add_argument("--idle-timeout-seconds", type=float, default=DEFAULT_IDLE_TIMEOUT_SECONDS)
    parser.add_argument("--settle-seconds", type=float, default=DEFAULT_SETTLE_SECONDS)
    parser.add_argument("--self-test", action="store_true", help="Run offline MsgPack decoder self-test and exit.")
    return parser.parse_args()


def main() -> int:
    """CLI entry point."""
    args = parse_args()
    if args.self_test:
        return run_self_test()

    return asyncio.run(run(args))


if __name__ == "__main__":
    raise SystemExit(main())
