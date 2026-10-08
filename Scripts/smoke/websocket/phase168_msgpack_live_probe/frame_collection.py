"""WebSocket frame collection for Phase168 MessagePack samples."""

from __future__ import annotations

import asyncio
import struct
import time

from .channel_subscription import *
from .msgpack_decoder import *
from .protocol_surface import *
from .sample_validation import *


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


__all__ = [name for name in globals() if not name.startswith("__")]
