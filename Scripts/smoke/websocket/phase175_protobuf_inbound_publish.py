#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Verify Phase175 FoxRun Protobuf inbound publication over the Foxglove WebSocket protocol.
# Usage: python Scripts/smoke/websocket/phase175_protobuf_inbound_publish.py --port 8765 --value 10
# Inputs: Unity Play Mode with Phase175ProtobufManualAcceptance enabled.
# Outputs: Protobuf channel metadata, echoed field-1 float value, and a PASS/FAIL verdict.

"""Protocol-level live smoke test for Phase175 Protobuf FoxRun inbound dispatch."""

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
DEFAULT_TOPIC = "/phase175/protobuf/shared-state"
DEFAULT_VALUE = 10.0
DEFAULT_CLIENT_CHANNEL_ID = 175_001
DEFAULT_SUBSCRIPTION_ID = 175_002
DEFAULT_ADVERTISE_TIMEOUT_SECONDS = 10.0
DEFAULT_RESULT_TIMEOUT_SECONDS = 5.0
DEFAULT_SETTLE_SECONDS = 0.25
FOXGLOVE_SUBPROTOCOL = "foxglove.sdk.v1"

EXPECTED_ENCODING = "protobuf"
MESSAGE_DATA_OPCODE = 1
CLIENT_MESSAGE_DATA_OPCODE = 1
OPCODE_OFFSET = 0
SUBSCRIPTION_ID_START = 1
MESSAGE_PAYLOAD_START = 13
MIN_MESSAGE_DATA_FRAME_BYTES = MESSAGE_PAYLOAD_START
FLOAT_FIELD_TAG = 0x0D
FLOAT_FIELD_BYTES = 5

EXIT_SUCCESS = 0
EXIT_FAILURE = 1
EXIT_TOPIC_NOT_FOUND = 3
EXIT_WRONG_CHANNEL = 4
EXIT_NO_ECHO = 5
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


class TopicNotFoundError(RuntimeError):
    """Raised when Unity does not advertise the requested topic."""


class ChannelValidationError(RuntimeError):
    """Raised when the advertised server channel is not a Protobuf contract."""


class ProtobufPayloadError(RuntimeError):
    """Raised when the expected field-1 fixed32 float payload is malformed."""


def build_url(args: argparse.Namespace) -> str:
    """Build the WebSocket URL from explicit URL or host/port arguments."""
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
    """Return a TLS context for WSS endpoints, optionally disabling validation."""
    if not url.lower().startswith("wss://"):
        return None

    if not insecure:
        return ssl.create_default_context()

    context = ssl.create_default_context()
    context.check_hostname = False
    context.verify_mode = ssl.CERT_NONE
    return context


async def wait_for_channel(ws: websockets.WebSocketClientProtocol, topic: str, timeout_seconds: float) -> ChannelInfo:
    """Wait for server advertise frames until the requested topic appears."""
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


def validate_channel(channel: ChannelInfo) -> None:
    """Require a fully-described Protobuf server contract before publishing."""
    if channel.encoding.lower() != EXPECTED_ENCODING:
        raise ChannelValidationError(
            f"Expected server encoding={EXPECTED_ENCODING}, got {channel.encoding or '(empty)'}.")
    if channel.schema_encoding.lower() != EXPECTED_ENCODING:
        raise ChannelValidationError(
            f"Expected server schemaEncoding={EXPECTED_ENCODING}, got {channel.schema_encoding or '(empty)'}.")
    if not channel.schema_name or not channel.schema:
        raise ChannelValidationError("Expected a named FileDescriptorSet Protobuf schema on the server channel.")


def build_client_advertise(topic: str, client_channel_id: int) -> str:
    """Build a client advertise command with an explicit Protobuf message encoding."""
    if not topic:
        raise ValueError("topic must not be empty")
    if client_channel_id <= 0:
        raise ValueError("client_channel_id must be positive")
    return json.dumps(
        {
            "op": "advertise",
            "channels": [{"id": client_channel_id, "topic": topic, "encoding": EXPECTED_ENCODING}],
        },
        separators=(",", ":"),
    )


def encode_float_field(value: float) -> bytes:
    """Encode Protobuf field 1 as a fixed32 float: tag 0x0d plus little-endian float32."""
    if not math.isfinite(value):
        raise ValueError("value must be finite")
    return bytes([FLOAT_FIELD_TAG]) + struct.pack("<f", value)


def decode_float_field(payload: bytes) -> float:
    """Decode exactly one field-1 fixed32 float payload emitted by this probe."""
    if len(payload) != FLOAT_FIELD_BYTES:
        raise ProtobufPayloadError(f"Expected {FLOAT_FIELD_BYTES} bytes, got {len(payload)}.")
    if payload[0] != FLOAT_FIELD_TAG:
        raise ProtobufPayloadError(f"Expected field-1 fixed32 tag 0x{FLOAT_FIELD_TAG:02x}, got 0x{payload[0]:02x}.")
    value = struct.unpack_from("<f", payload, 1)[0]
    if not math.isfinite(value):
        raise ProtobufPayloadError("Decoded float is not finite.")
    return value


def build_client_message_frame(client_channel_id: int, payload: bytes) -> bytes:
    """Build a client MessageData binary frame: opcode, channel id, then raw Protobuf bytes."""
    if client_channel_id <= 0:
        raise ValueError("client_channel_id must be positive")
    if not payload:
        raise ValueError("payload must not be empty")
    return bytes([CLIENT_MESSAGE_DATA_OPCODE]) + struct.pack("<I", client_channel_id) + payload


async def subscribe(ws: websockets.WebSocketClientProtocol, channel_id: int, subscription_id: int) -> None:
    """Subscribe to Unity's outbound copy of the shared state."""
    await ws.send(
        json.dumps(
            {"op": "subscribe", "subscriptions": [{"id": subscription_id, "channelId": channel_id}]},
            separators=(",", ":"),
        )
    )


async def drain_for_seconds(ws: websockets.WebSocketClientProtocol, seconds: float) -> None:
    """Discard startup frames before the client publishes its explicit test value."""
    deadline = time.perf_counter() + seconds
    while time.perf_counter() < deadline:
        try:
            await asyncio.wait_for(ws.recv(), timeout=max(0.01, deadline - time.perf_counter()))
        except asyncio.TimeoutError:
            break


async def wait_for_echo(
    ws: websockets.WebSocketClientProtocol,
    subscription_id: int,
    expected_value: float,
    timeout_seconds: float,
) -> float:
    """Wait for Unity's outbound Protobuf message carrying the exact accepted float value."""
    deadline = time.perf_counter() + timeout_seconds
    while time.perf_counter() < deadline:
        remaining = max(0.01, deadline - time.perf_counter())
        try:
            frame = await asyncio.wait_for(ws.recv(), timeout=remaining)
        except asyncio.TimeoutError:
            break
        if not isinstance(frame, bytes) or len(frame) < MIN_MESSAGE_DATA_FRAME_BYTES:
            continue
        if frame[OPCODE_OFFSET] != MESSAGE_DATA_OPCODE:
            continue
        if struct.unpack_from("<I", frame, SUBSCRIPTION_ID_START)[0] != subscription_id:
            continue

        received_value = decode_float_field(frame[MESSAGE_PAYLOAD_START:])
        if math.isclose(received_value, expected_value, rel_tol=1e-6, abs_tol=1e-6):
            return received_value

    raise TimeoutError(
        f"No outbound Protobuf echo for {expected_value:g}. Confirm Phase175ProtobufManualAcceptance is enabled in Unity Play Mode.")


async def run(args: argparse.Namespace) -> int:
    """Verify the server contract, publish a binary float, and confirm Unity applied it."""
    url = build_url(args)
    effective_url = redact_token(url)
    ssl_context = build_ssl_context(url, args.insecure)

    try:
        async with websockets.connect(url, subprotocols=[FOXGLOVE_SUBPROTOCOL], ssl=ssl_context) as ws:
            channel = await wait_for_channel(ws, args.topic, args.advertise_timeout_seconds)
            validate_channel(channel)
            await subscribe(ws, channel.channel_id, args.subscription_id)
            if args.settle_seconds > 0:
                await drain_for_seconds(ws, args.settle_seconds)

            payload = encode_float_field(args.value)
            await ws.send(build_client_advertise(args.topic, args.client_channel_id))
            await ws.send(build_client_message_frame(args.client_channel_id, payload))
            echoed_value = await wait_for_echo(ws, args.subscription_id, args.value, args.result_timeout_seconds)
    except TopicNotFoundError as exc:
        print("Verdict: TOPIC_NOT_FOUND")
        print(str(exc))
        return EXIT_TOPIC_NOT_FOUND
    except ChannelValidationError as exc:
        print("Verdict: WRONG_CHANNEL")
        print(str(exc))
        return EXIT_WRONG_CHANNEL
    except ProtobufPayloadError as exc:
        print("Verdict: DECODE_FAILURE")
        print(str(exc))
        return EXIT_DECODE_FAILURE
    except TimeoutError as exc:
        print("Verdict: NO_ECHO")
        print(str(exc))
        return EXIT_NO_ECHO
    except OSError as exc:
        print("Verdict: CONNECTION_FAILURE")
        print(str(exc))
        return EXIT_FAILURE

    print(f"Endpoint: {effective_url}")
    print(
        "Server channel: "
        f"id={channel.channel_id}, encoding={channel.encoding}, "
        f"schemaName={channel.schema_name!r}, schemaEncoding={channel.schema_encoding!r}"
    )
    print(f"Client advertise: id={args.client_channel_id}, encoding={EXPECTED_ENCODING}")
    print(f"Sent: field=1 fixed32-float value={args.value:g} payloadBytes={len(payload)}")
    print(f"Received: field=1 fixed32-float value={echoed_value:g}")
    print("Verdict: PASS")
    return EXIT_SUCCESS


def redact_token(url: str) -> str:
    """Hide token query values in console output."""
    parts = urlsplit(url)
    query = parse_qsl(parts.query, keep_blank_values=True)
    redacted = [(key, "REDACTED" if key == "token" else value) for key, value in query]
    return urlunsplit((parts.scheme, parts.netloc, parts.path, urlencode(redacted), parts.fragment))


def run_self_test() -> int:
    """Verify the protocol framing helpers without requiring a Unity session."""
    payload = encode_float_field(5.0)
    expected_frame = bytes([CLIENT_MESSAGE_DATA_OPCODE]) + struct.pack("<I", DEFAULT_CLIENT_CHANNEL_ID) + payload
    if payload != b"\x0d\x00\x00\xa0\x40" or decode_float_field(payload) != 5.0:
        print("self-test failed: field-1 float codec did not round-trip")
        return EXIT_FAILURE
    if build_client_message_frame(DEFAULT_CLIENT_CHANNEL_ID, payload) != expected_frame:
        print("self-test failed: client MessageData frame did not match")
        return EXIT_FAILURE

    try:
        decode_float_field(b"\x08\x00\x00\xa0\x40")
    except ProtobufPayloadError:
        print("self-test pass: encoded field-1 float and rejected a wrong Protobuf tag")
        return EXIT_SUCCESS

    print("self-test failed: wrong Protobuf tag decoded unexpectedly")
    return EXIT_FAILURE


def parse_args() -> argparse.Namespace:
    """Parse command-line arguments."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--url", help="Full ws:// or wss:// endpoint. Overrides host/port/wss.")
    parser.add_argument("--host", default=DEFAULT_HOST)
    parser.add_argument("--port", type=int, default=DEFAULT_PORT)
    parser.add_argument("--wss", action="store_true", help="Use wss:// when --url is omitted.")
    parser.add_argument("--insecure", action="store_true", help="Skip TLS certificate validation for local WSS smoke tests.")
    parser.add_argument("--token", default="", help="Shared token gate value. Appended as ?token=...")
    parser.add_argument("--topic", default=DEFAULT_TOPIC)
    parser.add_argument("--value", type=float, default=DEFAULT_VALUE)
    parser.add_argument("--client-channel-id", type=int, default=DEFAULT_CLIENT_CHANNEL_ID)
    parser.add_argument("--subscription-id", type=int, default=DEFAULT_SUBSCRIPTION_ID)
    parser.add_argument("--advertise-timeout-seconds", type=float, default=DEFAULT_ADVERTISE_TIMEOUT_SECONDS)
    parser.add_argument("--result-timeout-seconds", type=float, default=DEFAULT_RESULT_TIMEOUT_SECONDS)
    parser.add_argument("--settle-seconds", type=float, default=DEFAULT_SETTLE_SECONDS)
    parser.add_argument("--self-test", action="store_true", help="Run offline Protobuf framing checks and exit.")
    return parser.parse_args()


def main() -> int:
    """CLI entry point."""
    args = parse_args()
    if args.self_test:
        return run_self_test()
    if not math.isfinite(args.value):
        raise SystemExit("--value must be finite")
    return asyncio.run(run(args))


if __name__ == "__main__":
    raise SystemExit(main())
