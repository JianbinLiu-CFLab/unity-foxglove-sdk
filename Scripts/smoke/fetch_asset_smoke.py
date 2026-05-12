#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Request an asset through the Foxglove fetchAsset WebSocket operation.
# Usage: python Scripts/smoke/fetch_asset_smoke.py --uri asset://demo/Scripts/FoxgloveDemoSetup.cs
# Inputs: Unity Play Mode with FoxgloveManager asset roots configured; optional host, port, uri, and output path.
# Outputs: Prints fetchAsset status and optionally writes the returned asset payload.

"""Manual Foxglove fetchAsset WebSocket smoke test."""

from __future__ import annotations

import argparse
import asyncio
import json
import struct
from pathlib import Path

import websockets


# Default local Foxglove endpoint and subprotocol used by the Unity SDK.
DEFAULT_HOST = "127.0.0.1"
DEFAULT_PORT = 8765
FOXGLOVE_SUBPROTOCOL = "foxglove.sdk.v1"

# Default fetchAsset request values for the Unity demo project.
DEFAULT_REQUEST_ID = 42
DEFAULT_ASSET_URI = "asset://demo/Scripts/FoxgloveDemoSetup.cs"
DEFAULT_OUTPUT = Path("build/smoke/fetched_demo.cs")
EXPECTED_PAYLOAD_MARKER = "FoxgloveDemoSetup"
DEFAULT_PREVIEW_CHARS = 120

# Startup drain and receive defaults.
DEFAULT_DRAIN_ATTEMPTS = 10
DEFAULT_DRAIN_TIMEOUT_SECONDS = 0.2
DEFAULT_RESPONSE_ATTEMPTS = 5
DEFAULT_RESPONSE_TIMEOUT_SECONDS = 5.0

# Binary fetchAsset response layout. ServerOpcode.FetchAssetResponse is 4.
FETCH_ASSET_RESPONSE_OPCODE = 4
STRUCT_UNPACK_VALUE_INDEX = 0
OPCODE_OFFSET = 0
REQUEST_ID_START = 1
REQUEST_ID_END = 5
STATUS_OFFSET = 5
ERROR_LENGTH_START = 6
ERROR_LENGTH_END = 10
PAYLOAD_START = 10
STATUS_OK = 0

# Process exit codes returned by this smoke script.
EXIT_SUCCESS = 0
EXIT_FAILURE = 1


def build_ws_url(host: str, port: int) -> str:
    """Build the WebSocket URL for the local Unity Foxglove server."""
    return f"ws://{host}:{port}"


def build_fetch_asset_request(request_id: int, uri: str) -> str:
    """Build a compact JSON fetchAsset request."""
    return json.dumps(
        {
            "op": "fetchAsset",
            "requestId": request_id,
            "uri": uri,
        },
        separators=(",", ":"),
    )


async def drain_startup_messages(ws, attempts: int, timeout_seconds: float) -> None:
    """Drain initial serverInfo/advertise messages before sending fetchAsset."""
    print("Draining initial messages...")
    for _ in range(attempts):
        try:
            msg = await asyncio.wait_for(ws.recv(), timeout=timeout_seconds)
        except asyncio.TimeoutError:
            print("  No more messages")
            break

        if isinstance(msg, str):
            op = ""
            try:
                op = json.loads(msg).get("op", "")
            except json.JSONDecodeError:
                op = ""
            print(f"  Drained text: op={op}")
        else:
            print(f"  Drained binary: {len(msg)} byte(s)")


def parse_fetch_asset_response(data: bytes) -> tuple[int, int, int, bytes]:
    """Parse a binary fetchAsset response into opcode, request ID, status, and payload."""
    opcode = data[OPCODE_OFFSET]
    request_id = struct.unpack("<I", data[REQUEST_ID_START:REQUEST_ID_END])[STRUCT_UNPACK_VALUE_INDEX]
    status = data[STATUS_OFFSET]
    error_length = struct.unpack("<I", data[ERROR_LENGTH_START:ERROR_LENGTH_END])[STRUCT_UNPACK_VALUE_INDEX]
    payload = data[PAYLOAD_START:]
    if status != STATUS_OK:
        payload = data[PAYLOAD_START : PAYLOAD_START + error_length]
    return opcode, request_id, status, payload


def write_payload(path: Path, payload: bytes) -> None:
    """Write the fetched payload to disk, creating parent directories as needed."""
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(payload)


async def run(args: argparse.Namespace) -> int:
    """Connect to Unity, send fetchAsset, and report the first binary response."""
    async with websockets.connect(build_ws_url(args.host, args.port), subprotocols=[FOXGLOVE_SUBPROTOCOL]) as ws:
        print("Connected.")
        await drain_startup_messages(ws, args.drain_attempts, args.drain_timeout_seconds)

        request = build_fetch_asset_request(args.request_id, args.uri)
        print(f"Sending fetchAsset: {request}")
        await ws.send(request)

        for _ in range(args.response_attempts):
            try:
                msg = await asyncio.wait_for(ws.recv(), timeout=args.response_timeout_seconds)
            except asyncio.TimeoutError:
                print("Timeout waiting for response")
                return EXIT_FAILURE

            if isinstance(msg, str):
                print(f"Text (draining): {msg[:args.preview_chars]}")
                continue

            opcode, request_id, status, payload = parse_fetch_asset_response(msg)
            print(f"Binary: opcode={opcode} requestId={request_id} status={status} payloadBytes={len(payload)}")
            if opcode != FETCH_ASSET_RESPONSE_OPCODE:
                print(f"[FAIL] Unexpected binary opcode: {opcode}")
                return EXIT_FAILURE
            if request_id != args.request_id:
                print(f"[FAIL] Unexpected requestId: {request_id}")
                return EXIT_FAILURE
            if status != STATUS_OK:
                print(f"[FAIL] Server error: {payload.decode('utf-8', errors='replace')}")
                return EXIT_FAILURE

            text = payload.decode("utf-8", errors="replace")
            if EXPECTED_PAYLOAD_MARKER in text:
                print(f"[PASS] fetchAsset SUCCESS - got {len(payload)} bytes, content matches")
            else:
                print(f"[CHECK] Got {len(payload)} bytes: {text[:args.preview_chars]}")

            if args.output:
                write_payload(Path(args.output), payload)
                print(f"Saved to: {args.output}")
            return EXIT_SUCCESS

    return EXIT_FAILURE


def parse_args() -> argparse.Namespace:
    """Parse CLI arguments for the fetchAsset smoke client."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--host", default=DEFAULT_HOST)
    parser.add_argument("--port", type=int, default=DEFAULT_PORT)
    parser.add_argument("--request-id", type=int, default=DEFAULT_REQUEST_ID)
    parser.add_argument("--uri", default=DEFAULT_ASSET_URI)
    parser.add_argument("--output", default=str(DEFAULT_OUTPUT))
    parser.add_argument("--drain-attempts", type=int, default=DEFAULT_DRAIN_ATTEMPTS)
    parser.add_argument("--drain-timeout-seconds", type=float, default=DEFAULT_DRAIN_TIMEOUT_SECONDS)
    parser.add_argument("--response-attempts", type=int, default=DEFAULT_RESPONSE_ATTEMPTS)
    parser.add_argument("--response-timeout-seconds", type=float, default=DEFAULT_RESPONSE_TIMEOUT_SECONDS)
    parser.add_argument("--preview-chars", type=int, default=DEFAULT_PREVIEW_CHARS)
    return parser.parse_args()


def main() -> int:
    """Run the async fetchAsset smoke client from the synchronous CLI entry point."""
    return asyncio.run(run(parse_args()))


if __name__ == "__main__":
    raise SystemExit(main())
