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

import websockets


# Default local Foxglove endpoint and subprotocol used by the Unity SDK.
DEFAULT_HOST = "127.0.0.1"
DEFAULT_PORT = 8765
FOXGLOVE_SUBPROTOCOL = "foxglove.sdk.v1"

# The Unity session sends serverInfo and advertise before manual subscription.
DEFAULT_STARTUP_FRAMES_TO_DRAIN = 2

# Manual subscription target and stable client subscription ID.
TF_SUBSCRIPTION_ID = 100
TF_CHANNEL_ID = 2_147_483_650

# Receive loop defaults for this manual smoke probe.
DEFAULT_MAX_RECEIVE_FRAMES = 20
DEFAULT_RECEIVE_TIMEOUT_SECONDS = 3.0

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


def build_ws_url(host: str, port: int) -> str:
    """Build the WebSocket URL for the local Unity Foxglove server."""
    return f"ws://{host}:{port}"


def build_subscribe_payload() -> str:
    """Build the JSON subscribe operation for the fixed /tf channel."""
    return json.dumps(
        {
            "op": "subscribe",
            "subscriptions": [{"id": TF_SUBSCRIPTION_ID, "channelId": TF_CHANNEL_ID}],
        },
        separators=(",", ":"),
    )


async def run(args: argparse.Namespace) -> int:
    """Subscribe to /tf and print a bounded stream of decoded binary frames."""
    async with websockets.connect(build_ws_url(args.host, args.port), subprotocols=[FOXGLOVE_SUBPROTOCOL]) as ws:
        for _ in range(args.startup_frames_to_drain):
            await ws.recv()  # Drain serverInfo + advertise.

        await ws.send(build_subscribe_payload())

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
                    data = json.loads(payload)
                    child = data.get("child_frame_id", UNKNOWN_CHILD_FRAME)
                    tx = data.get("translation", {}).get("x", DEFAULT_TRANSLATION_X)
                    print(f"Msg #{index}: subId={sub_id} logTime={log_ns} child={child} tx={tx}")
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
    parser.add_argument("--startup-frames-to-drain", type=int, default=DEFAULT_STARTUP_FRAMES_TO_DRAIN)
    parser.add_argument("--max-frames", type=int, default=DEFAULT_MAX_RECEIVE_FRAMES)
    parser.add_argument("--timeout-seconds", type=float, default=DEFAULT_RECEIVE_TIMEOUT_SECONDS)
    return parser.parse_args()


def main() -> int:
    """Run the async smoke client from the synchronous CLI entry point."""
    return asyncio.run(run(parse_args()))


if __name__ == "__main__":
    raise SystemExit(main())
