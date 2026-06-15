#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Manual Phase 144 WebSocket fragmentation probe for a running Unity
# Foxglove WebSocket server.

from __future__ import annotations

import argparse
import base64
import json
import os
import socket
import ssl
import struct
import sys
import time
from dataclasses import dataclass
from typing import Iterable
from urllib.parse import urlparse


OP_CONTINUATION = 0x0
OP_TEXT = 0x1
OP_BINARY = 0x2
OP_CLOSE = 0x8
OP_PING = 0x9
OP_PONG = 0xA

DEFAULT_URL = "ws://127.0.0.1:8765"
DEFAULT_TIMEOUT_SECONDS = 5.0
MAX_READ_FRAMES = 64
FOXGLOVE_SUBPROTOCOL = "foxglove.sdk.v1"


@dataclass
class Frame:
    """Decoded WebSocket frame header and payload."""

    fin: bool
    opcode: int
    payload: bytes


class ProbeFailure(RuntimeError):
    """Raised when the probe observes an unexpected result."""

    pass


class RawWebSocket:
    """Minimal raw WebSocket client used by the fragmentation probe."""

    def __init__(self, url: str, timeout: float, token: str | None, insecure: bool):
        """Create a raw WebSocket client for frame-level probing."""

        self.url = url
        self.timeout = timeout
        self.token = token
        self.insecure = insecure
        self.sock: socket.socket | ssl.SSLSocket | None = None

    def __enter__(self) -> "RawWebSocket":
        """Open the socket and complete the Foxglove WebSocket handshake."""

        parsed = urlparse(self.url)
        if parsed.scheme not in ("ws", "wss"):
            raise ProbeFailure(f"Unsupported URL scheme '{parsed.scheme}'. Use ws:// or wss://.")

        host = parsed.hostname or "127.0.0.1"
        port = parsed.port or (443 if parsed.scheme == "wss" else 80)
        path = parsed.path or "/"
        query = parsed.query
        if self.token:
            separator = "&" if query else ""
            query = f"{query}{separator}token={self.token}"
        if query:
            path += "?" + query

        raw = socket.create_connection((host, port), timeout=self.timeout)
        raw.settimeout(self.timeout)
        if parsed.scheme == "wss":
            context = ssl.create_default_context()
            if self.insecure:
                context.check_hostname = False
                context.verify_mode = ssl.CERT_NONE
            self.sock = context.wrap_socket(raw, server_hostname=host)
        else:
            self.sock = raw

        key = base64.b64encode(os.urandom(16)).decode("ascii")
        headers = [
            f"GET {path} HTTP/1.1",
            f"Host: {host}:{port}",
            "Upgrade: websocket",
            "Connection: Upgrade",
            "Sec-WebSocket-Version: 13",
            f"Sec-WebSocket-Key: {key}",
            f"Sec-WebSocket-Protocol: {FOXGLOVE_SUBPROTOCOL}",
            "Origin: https://app.foxglove.dev",
        ]
        request = "\r\n".join(headers) + "\r\n\r\n"
        self.sock.sendall(request.encode("ascii"))

        response = self._read_http_response()
        if not response.startswith("HTTP/1.1 101"):
            raise ProbeFailure("WebSocket upgrade failed:\n" + response)
        return self

    def __exit__(self, exc_type, exc, tb) -> None:
        """Close the socket when leaving the context manager."""

        try:
            self.close()
        except OSError:
            pass

    def _read_http_response(self) -> str:
        """Read the HTTP upgrade response headers."""

        assert self.sock is not None
        data = bytearray()
        while b"\r\n\r\n" not in data:
            chunk = self.sock.recv(1)
            if not chunk:
                break
            data.extend(chunk)
            if len(data) > 8192:
                break
        return data.decode("iso-8859-1", errors="replace")

    def close(self) -> None:
        """Close the underlying socket without raising on shutdown races."""

        if self.sock is None:
            return
        try:
            self.send_frame(OP_CLOSE, b"", fin=True)
        except OSError:
            pass
        try:
            self.sock.close()
        finally:
            self.sock = None

    def send_text_fragments(self, text: str, split_points: Iterable[int]) -> None:
        """Send one text message split across several WebSocket fragments."""

        data = text.encode("utf-8")
        points = [0]
        points.extend(point for point in split_points if 0 < point < len(data))
        points.append(len(data))
        parts = [data[points[i] : points[i + 1]] for i in range(len(points) - 1)]
        if not parts:
            parts = [b""]

        self.send_frame(OP_TEXT, parts[0], fin=len(parts) == 1)
        for index, part in enumerate(parts[1:], start=1):
            self.send_frame(OP_CONTINUATION, part, fin=index == len(parts) - 1)

    def send_frame(self, opcode: int, payload: bytes, fin: bool) -> None:
        """Send a single client-to-server WebSocket frame."""

        assert self.sock is not None
        payload = payload or b""
        first = (0x80 if fin else 0x00) | opcode
        length = len(payload)
        header = bytearray([first])
        if length <= 125:
            header.append(0x80 | length)
        elif length <= 0xFFFF:
            header.append(0x80 | 126)
            header.extend(struct.pack("!H", length))
        else:
            header.append(0x80 | 127)
            header.extend(struct.pack("!Q", length))

        mask = os.urandom(4)
        header.extend(mask)
        masked = bytes(payload[i] ^ mask[i % 4] for i in range(length))
        self.sock.sendall(bytes(header) + masked)

    def read_frame(self) -> Frame | None:
        """Read one server-to-client WebSocket frame."""

        assert self.sock is not None
        try:
            header = self._recv_exact(2)
        except (TimeoutError, socket.timeout):
            return None
        if not header:
            return None

        first, second = header
        fin = (first & 0x80) != 0
        opcode = first & 0x0F
        masked = (second & 0x80) != 0
        length = second & 0x7F
        if length == 126:
            length = struct.unpack("!H", self._recv_exact(2))[0]
        elif length == 127:
            length = struct.unpack("!Q", self._recv_exact(8))[0]

        mask = self._recv_exact(4) if masked else b""
        payload = self._recv_exact(length) if length else b""
        if masked:
            payload = bytes(payload[i] ^ mask[i % 4] for i in range(length))
        return Frame(fin=fin, opcode=opcode, payload=payload)

    def _recv_exact(self, count: int) -> bytes:
        """Receive exactly the requested number of bytes or fail."""

        assert self.sock is not None
        data = bytearray()
        while len(data) < count:
            chunk = self.sock.recv(count - len(data))
            if not chunk:
                raise ProbeFailure("Socket closed while reading a frame.")
            data.extend(chunk)
        return bytes(data)


def parse_args() -> argparse.Namespace:
    """Parse command-line arguments for the fragmentation smoke probe."""

    parser = argparse.ArgumentParser(
        description="Probe Unity2Foxglove Phase144 WebSocket fragmented-frame behavior."
    )
    parser.add_argument("--url", default=DEFAULT_URL, help=f"WebSocket URL. Default: {DEFAULT_URL}")
    parser.add_argument("--timeout", type=float, default=DEFAULT_TIMEOUT_SECONDS, help="Socket timeout in seconds.")
    parser.add_argument("--token", default=None, help="Optional bearer token for secured WebSocket servers.")
    parser.add_argument("--insecure", action="store_true", help="Allow insecure WSS certificates.")
    parser.add_argument(
        "--mode",
        choices=("all", "positive", "negative"),
        default="all",
        help="Probe mode. Default: all.",
    )
    return parser.parse_args()


def read_initial_frames(ws: RawWebSocket, max_frames: int = MAX_READ_FRAMES) -> int | None:
    """Read initial server frames and return the first advertised channel id."""

    deadline = time.monotonic() + ws.timeout
    while time.monotonic() < deadline and max_frames > 0:
        max_frames -= 1
        frame = ws.read_frame()
        if frame is None:
            break
        if frame.opcode == OP_CLOSE:
            raise ProbeFailure("Server closed during initial frame read.")
        if frame.opcode == OP_PING:
            ws.send_frame(OP_PONG, frame.payload, fin=True)
            continue
        if frame.opcode != OP_TEXT:
            continue

        try:
            message = json.loads(frame.payload.decode("utf-8"))
        except (UnicodeDecodeError, json.JSONDecodeError):
            continue
        if message.get("op") != "advertise":
            continue
        for channel in message.get("channels", []):
            value = channel.get("id")
            if isinstance(value, int):
                return value
    return None


def expect_no_close(ws: RawWebSocket, seconds: float) -> None:
    """Verify the server keeps the connection open for a short window."""

    deadline = time.monotonic() + seconds
    while time.monotonic() < deadline:
        frame = ws.read_frame()
        if frame is None:
            return
        if frame.opcode == OP_CLOSE:
            raise ProbeFailure("Server closed after a valid fragmented client message.")
        if frame.opcode == OP_PING:
            ws.send_frame(OP_PONG, frame.payload, fin=True)


def expect_close(ws: RawWebSocket, label: str) -> None:
    """Verify the server rejects the current connection."""

    deadline = time.monotonic() + ws.timeout
    while time.monotonic() < deadline:
        frame = ws.read_frame()
        if frame is None:
            continue
        if frame.opcode == OP_CLOSE:
            return
    raise ProbeFailure(f"{label}: server did not close the malformed connection.")


def positive_probe(args: argparse.Namespace) -> None:
    """Verify a valid fragmented subscribe message remains accepted."""

    print("[INFO] Positive probe: fragmented subscribe text message")
    with RawWebSocket(args.url, args.timeout, args.token, args.insecure) as ws:
        channel_id = read_initial_frames(ws)
        if channel_id is None:
            print("[WARN] No advertise channel observed before timeout; using channelId=1 fallback.")
            channel_id = 1
        else:
            print(f"[INFO] Using advertised channelId={channel_id}.")

        subscribe = json.dumps(
            {"op": "subscribe", "subscriptions": [{"id": 144, "channelId": channel_id}]},
            separators=(",", ":"),
        )
        first = max(1, len(subscribe) // 3)
        second = max(first + 1, 2 * len(subscribe) // 3)
        ws.send_text_fragments(subscribe, (first, second))
        expect_no_close(ws, min(1.0, args.timeout))
        print("[PASS] Valid fragmented text message did not close the connection.")


def negative_orphan_continuation(args: argparse.Namespace) -> None:
    """Verify an orphan continuation frame is rejected."""

    print("[INFO] Negative probe: orphan continuation frame")
    with RawWebSocket(args.url, args.timeout, args.token, args.insecure) as ws:
        ws.send_frame(OP_CONTINUATION, b"tail", fin=True)
        expect_close(ws, "orphan continuation")
        print("[PASS] Orphan continuation was rejected.")


def negative_nested_data_frame(args: argparse.Namespace) -> None:
    """Verify a new data frame during fragmentation is rejected."""

    print("[INFO] Negative probe: new data frame while fragmented message is open")
    with RawWebSocket(args.url, args.timeout, args.token, args.insecure) as ws:
        ws.send_frame(OP_TEXT, b'{"op":"', fin=False)
        ws.send_frame(OP_BINARY, b"\x01\x02", fin=True)
        expect_close(ws, "nested data frame")
        print("[PASS] Nested data frame was rejected.")


def negative_oversized_fragments(args: argparse.Namespace) -> None:
    """Verify oversized fragmented messages are rejected."""

    print("[INFO] Negative probe: fragmented binary aggregate above 4 MiB")
    with RawWebSocket(args.url, args.timeout, args.token, args.insecure) as ws:
        chunk = b"x" * 65535
        ws.send_frame(OP_BINARY, chunk, fin=False)
        for index in range(64):
            ws.send_frame(OP_CONTINUATION, chunk, fin=index == 63)
        expect_close(ws, "oversized fragmented binary")
        print("[PASS] Oversized fragmented binary message was rejected.")


def main() -> int:
    """Run all Phase 144 fragmentation probes."""

    args = parse_args()
    try:
        if args.mode in ("all", "positive"):
            positive_probe(args)
        if args.mode in ("all", "negative"):
            negative_orphan_continuation(args)
            negative_nested_data_frame(args)
            negative_oversized_fragments(args)
    except (OSError, ProbeFailure) as exc:
        print(f"[FAIL] {exc}", file=sys.stderr)
        return 1

    print("[PASS] Phase144 fragmented WebSocket probe completed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
