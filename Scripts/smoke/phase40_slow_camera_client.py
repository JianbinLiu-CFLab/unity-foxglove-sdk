#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Hold a slow Foxglove camera subscription open for backpressure validation.
# Usage: python Scripts/smoke/phase40_slow_camera_client.py --hold-seconds 120
# Inputs: --host, --port, advertise timeout, fallback range, hold time, and --no-fallback.
# Outputs: Opens a slow subscription client for camera backpressure smoke testing.

"""Slow Foxglove camera client smoke for backpressure validation."""

from __future__ import annotations

import argparse
import base64
import json
import os
import re
import socket
import struct
import time


# Topic used by the Unity camera publisher under test.
CAMERA_TOPIC = "/unity/camera"

# Default Foxglove WebSocket endpoint for local Unity smoke tests.
DEFAULT_HOST = "127.0.0.1"
DEFAULT_PORT = 8765

# Process exit code for successful manual smoke setup.
EXIT_SUCCESS = 0

# WebSocket frame constants from RFC 6455.
WEBSOCKET_CONTINUATION_OPCODE = 0x0
WEBSOCKET_TEXT_OPCODE = 0x1
WEBSOCKET_OPCODE_MASK = 0x0F
WEBSOCKET_MASK_FLAG = 0x80
WEBSOCKET_PAYLOAD_LENGTH_MASK = 0x7F
WEBSOCKET_16BIT_LENGTH_MARKER = 126
WEBSOCKET_64BIT_LENGTH_MARKER = 127
WEBSOCKET_BASE_HEADER_BYTES = 2
WEBSOCKET_16BIT_LENGTH_BYTES = 2
WEBSOCKET_64BIT_LENGTH_BYTES = 8
WEBSOCKET_MASK_KEY_BYTES = 4
WEBSOCKET_SMALL_PAYLOAD_MAX_BYTES = 125
WEBSOCKET_UINT16_MAX_BYTES = 65_535
WEBSOCKET_CLIENT_TEXT_FIN_OPCODE = 0x81
WEBSOCKET_CLIENT_KEY_BYTES = 16
WEBSOCKET_PROTOCOL_VERSION = "13"
WEBSOCKET_ACCEPTED_STATUS_PREFIX = "HTTP/1.1 101"
WEBSOCKET_FIRST_HEADER_BYTE_INDEX = 0
WEBSOCKET_SECOND_HEADER_BYTE_INDEX = 1
STRUCT_UNPACK_VALUE_INDEX = 0
REGEX_CHANNEL_ID_GROUP = 1
NO_REMAINING_BYTES = 0
NO_FLAG_BITS_SET = 0

# Maximum frame payload accepted by this manual smoke client.
MAX_SMOKE_FRAME_PAYLOAD_BYTES = 2_147_483_647

# TCP_NODELAY option value that disables Nagle for lower-latency smoke behavior.
TCP_NODELAY_ENABLED = 1

# Subscription IDs are arbitrary client-side IDs; these ranges keep fallback IDs readable.
CAMERA_SUBSCRIPTION_ID = 9_040
FALLBACK_SUBSCRIPTION_ID_BASE = 904_000
FIRST_FALLBACK_CHANNEL_ID = 1
INCLUSIVE_RANGE_END_OFFSET = 1

# Default timeout/hold values for manual smoke runs.
DEFAULT_CONNECT_TIMEOUT_SECONDS = 10.0
DEFAULT_ADVERTISE_TIMEOUT_SECONDS = 10.0
DEFAULT_FALLBACK_MAX_CHANNEL_ID = 128
DEFAULT_HOLD_SECONDS = 0.0
SOCKET_POLL_TIMEOUT_SECONDS = 1.0
IDLE_HOLD_SLEEP_SECONDS = 10
NO_HOLD_SECONDS = 0.0

# Socket reads one byte at a time while waiting for the HTTP header terminator.
HTTP_HEADER_TERMINATOR = b"\r\n\r\n"
HANDSHAKE_READ_BYTES = 1
MAX_HANDSHAKE_RESPONSE_BYTES = 8192


def read_exact(sock: socket.socket, count: int) -> bytes:
    """Read exactly count bytes or raise when the socket closes early."""
    chunks: list[bytes] = []
    remaining = count
    while remaining > NO_REMAINING_BYTES:
        chunk = sock.recv(remaining)
        if not chunk:
            raise ConnectionError(f"Socket closed while reading {count} byte(s).")
        chunks.append(chunk)
        remaining -= len(chunk)
    return b"".join(chunks)


def read_server_frame(sock: socket.socket) -> tuple[int, bytes, str | None]:
    """Read one server WebSocket frame and decode text payloads when present."""
    header = read_exact(sock, WEBSOCKET_BASE_HEADER_BYTES)
    opcode = header[WEBSOCKET_FIRST_HEADER_BYTE_INDEX] & WEBSOCKET_OPCODE_MASK
    masked = (header[WEBSOCKET_SECOND_HEADER_BYTE_INDEX] & WEBSOCKET_MASK_FLAG) != NO_FLAG_BITS_SET
    length = header[WEBSOCKET_SECOND_HEADER_BYTE_INDEX] & WEBSOCKET_PAYLOAD_LENGTH_MASK

    if length == WEBSOCKET_16BIT_LENGTH_MARKER:
        length = struct.unpack("!H", read_exact(sock, WEBSOCKET_16BIT_LENGTH_BYTES))[STRUCT_UNPACK_VALUE_INDEX]
    elif length == WEBSOCKET_64BIT_LENGTH_MARKER:
        length = struct.unpack("!Q", read_exact(sock, WEBSOCKET_64BIT_LENGTH_BYTES))[STRUCT_UNPACK_VALUE_INDEX]

    if length > MAX_SMOKE_FRAME_PAYLOAD_BYTES:
        raise ValueError(f"Frame too large for this smoke script: {length}")

    mask = read_exact(sock, WEBSOCKET_MASK_KEY_BYTES) if masked else b""
    payload = bytearray(read_exact(sock, length))
    if masked:
        for index in range(len(payload)):
            payload[index] ^= mask[index % WEBSOCKET_MASK_KEY_BYTES]

    data = bytes(payload)
    text = data.decode("utf-8") if opcode in (WEBSOCKET_CONTINUATION_OPCODE, WEBSOCKET_TEXT_OPCODE) else None
    return opcode, data, text


def find_channel_id_in_advertise_text(text: str, topic: str) -> int | None:
    """Find a channel ID for a topic in accumulated advertise JSON text."""
    if not text:
        return None

    try:
        message = json.loads(text)
    except json.JSONDecodeError:
        message = None

    if isinstance(message, dict) and message.get("op") == "advertise":
        channel_id = find_channel_id_in_advertise_message(message, topic)
        if channel_id is not None:
            return channel_id

    escaped_topic = re.escape(topic)
    id_before_topic = re.search(r'\{[^{}]*"id"\s*:\s*(\d+)[^{}]*"topic"\s*:\s*"' + escaped_topic + r'"', text)
    if id_before_topic:
        return int(id_before_topic.group(REGEX_CHANNEL_ID_GROUP))

    topic_before_id = re.search(r'\{[^{}]*"topic"\s*:\s*"' + escaped_topic + r'"[^{}]*"id"\s*:\s*(\d+)', text)
    if topic_before_id:
        return int(topic_before_id.group(REGEX_CHANNEL_ID_GROUP))

    return None


def find_channel_id_in_advertise_message(message: dict, topic: str) -> int | None:
    """Find a channel ID by parsing an advertise JSON object."""
    for raw_channel in message.get("channels", []):
        if not isinstance(raw_channel, dict):
            continue
        if raw_channel.get("topic") != topic:
            continue
        try:
            return int(raw_channel.get("id"))
        except (TypeError, ValueError):
            return None
    return None


def send_masked_text_frame(sock: socket.socket, text: str) -> None:
    """Send a client-to-server masked text frame."""
    payload = text.encode("utf-8")
    mask = os.urandom(WEBSOCKET_MASK_KEY_BYTES)
    header = bytearray([WEBSOCKET_CLIENT_TEXT_FIN_OPCODE])

    if len(payload) <= WEBSOCKET_SMALL_PAYLOAD_MAX_BYTES:
        header.append(WEBSOCKET_MASK_FLAG | len(payload))
    elif len(payload) <= WEBSOCKET_UINT16_MAX_BYTES:
        header.append(WEBSOCKET_MASK_FLAG | WEBSOCKET_16BIT_LENGTH_MARKER)
        header.extend(struct.pack("!H", len(payload)))
    else:
        header.append(WEBSOCKET_MASK_FLAG | WEBSOCKET_64BIT_LENGTH_MARKER)
        header.extend(struct.pack("!Q", len(payload)))

    masked_payload = bytearray(payload)
    for index in range(len(masked_payload)):
        masked_payload[index] ^= mask[index % WEBSOCKET_MASK_KEY_BYTES]

    sock.sendall(bytes(header) + mask + bytes(masked_payload))


def read_handshake_response(sock: socket.socket) -> str:
    """Read the HTTP upgrade response through the header terminator."""
    response = bytearray()
    while HTTP_HEADER_TERMINATOR not in response:
        if len(response) >= MAX_HANDSHAKE_RESPONSE_BYTES:
            raise ValueError(f"Handshake response exceeded {MAX_HANDSHAKE_RESPONSE_BYTES} bytes.")
        byte = sock.recv(HANDSHAKE_READ_BYTES)
        if not byte:
            raise ConnectionError("Socket closed during handshake.")
        response.extend(byte)
    return response.decode("ascii", errors="replace")


def subscribe_payload(camera_channel_id: int | None, fallback_max_channel_id: int) -> tuple[str, str]:
    """Build a subscribe operation for the camera channel or fallback channel range."""
    if camera_channel_id is not None:
        subscriptions = [{"id": CAMERA_SUBSCRIPTION_ID, "channelId": camera_channel_id}]
        message = f"Subscribed to {CAMERA_TOPIC} on channel {camera_channel_id}."
    else:
        subscriptions = [
            {"id": FALLBACK_SUBSCRIPTION_ID_BASE + channel_id, "channelId": channel_id}
            for channel_id in range(
                FIRST_FALLBACK_CHANNEL_ID,
                fallback_max_channel_id + INCLUSIVE_RANGE_END_OFFSET,
            )
        ]
        message = f"Subscribed to channel range 1..{fallback_max_channel_id}."

    payload = json.dumps({"op": "subscribe", "subscriptions": subscriptions}, separators=(",", ":"))
    return payload, message


def run(args: argparse.Namespace) -> int:
    """Open the WebSocket connection, subscribe, then intentionally stop reading."""
    with socket.create_connection((args.host, args.port), timeout=args.connect_timeout_seconds) as sock:
        sock.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, TCP_NODELAY_ENABLED)
        key = base64.b64encode(os.urandom(WEBSOCKET_CLIENT_KEY_BYTES)).decode("ascii")
        request = (
            "GET / HTTP/1.1\r\n"
            f"Host: {args.host}:{args.port}\r\n"
            "Upgrade: websocket\r\n"
            "Connection: Upgrade\r\n"
            f"Sec-WebSocket-Key: {key}\r\n"
            f"Sec-WebSocket-Version: {WEBSOCKET_PROTOCOL_VERSION}\r\n"
            "Sec-WebSocket-Protocol: foxglove.sdk.v1\r\n"
            "\r\n"
        )
        sock.sendall(request.encode("ascii"))

        response = read_handshake_response(sock)
        if not response.startswith(WEBSOCKET_ACCEPTED_STATUS_PREFIX):
            raise RuntimeError(f"WebSocket handshake failed: {response}")

        print(f"Handshake accepted. Waiting for {CAMERA_TOPIC} advertise...")

        camera_channel_id: int | None = None
        advertise_text = ""
        deadline = time.monotonic() + args.advertise_timeout_seconds
        sock.settimeout(SOCKET_POLL_TIMEOUT_SECONDS)

        while time.monotonic() < deadline and camera_channel_id is None:
            try:
                opcode, _, text = read_server_frame(sock)
            except TimeoutError:
                continue
            except socket.timeout:
                continue

            if opcode not in (WEBSOCKET_CONTINUATION_OPCODE, WEBSOCKET_TEXT_OPCODE) or not text:
                continue

            try:
                message = json.loads(text)
            except json.JSONDecodeError:
                message = None

            if isinstance(message, dict):
                camera_channel_id = find_channel_id_in_advertise_message(message, CAMERA_TOPIC)
                if camera_channel_id is not None:
                    break

            advertise_text += text
            camera_channel_id = find_channel_id_in_advertise_text(advertise_text, CAMERA_TOPIC)

        if camera_channel_id is None and args.no_fallback:
            raise RuntimeError(
                f"Did not see {CAMERA_TOPIC} advertise within {args.advertise_timeout_seconds} seconds. "
                "Confirm the camera publisher is enabled and publishing."
            )

        if camera_channel_id is not None:
            print(f"Found {CAMERA_TOPIC} on channel {camera_channel_id}.")
        else:
            print(f"Did not see {CAMERA_TOPIC} advertise within {args.advertise_timeout_seconds} seconds.")
            print(f"Falling back to broad subscription for channel IDs 1..{args.fallback_max_channel_id}.")

        payload, subscribe_message = subscribe_payload(camera_channel_id, args.fallback_max_channel_id)
        send_masked_text_frame(sock, payload)
        print(subscribe_message)

        if args.hold_seconds > NO_HOLD_SECONDS:
            print(f"This client will now stop reading for {args.hold_seconds} second(s).")
            time.sleep(args.hold_seconds)
            print("Hold seconds elapsed; closing smoke client.")
        else:
            print("This client will now stop reading. Press Ctrl+C to stop.")
            while True:
                time.sleep(IDLE_HOLD_SLEEP_SECONDS)

    return EXIT_SUCCESS


def main() -> int:
    """Parse CLI arguments and run the slow camera smoke client."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--host", default=DEFAULT_HOST)
    parser.add_argument("--port", type=int, default=DEFAULT_PORT)
    parser.add_argument("--connect-timeout-seconds", type=float, default=DEFAULT_CONNECT_TIMEOUT_SECONDS)
    parser.add_argument("--advertise-timeout-seconds", type=float, default=DEFAULT_ADVERTISE_TIMEOUT_SECONDS)
    parser.add_argument("--fallback-max-channel-id", type=int, default=DEFAULT_FALLBACK_MAX_CHANNEL_ID)
    parser.add_argument("--hold-seconds", type=float, default=DEFAULT_HOLD_SECONDS)
    parser.add_argument("--no-fallback", action="store_true")
    args = parser.parse_args()
    return run(args)


if __name__ == "__main__":
    raise SystemExit(main())
