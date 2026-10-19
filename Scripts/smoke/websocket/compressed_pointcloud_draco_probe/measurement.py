"""Frame measurement and startup draining for the Draco probe."""

from __future__ import annotations

import asyncio
import struct
import time

import websockets

from .channel_subscription import *
from .payload_decoding import *
from .protocol_surface import *


async def measure_compressed_pointcloud(
    ws: websockets.WebSocketClientProtocol,
    channel: ChannelInfo,
    subscription_id: int,
    duration_seconds: float,
    settle_seconds: float,
    idle_timeout_seconds: float,
) -> Measurement:
    """Collect MessageData frames and decode compressed point-cloud payload statistics."""
    if settle_seconds > 0:
        await drain_for_seconds(ws, settle_seconds)

    start = time.perf_counter()
    end = start + duration_seconds
    total_binary_frames = 0
    malformed_payload_count = 0
    total_payload_bytes = 0
    receive_times: list[float] = []
    log_times_ns: list[int] = []
    decoded_samples: list[CompressedPointCloudPayloadInfo] = []

    while time.perf_counter() < end:
        timeout = min(idle_timeout_seconds, max(0.01, end - time.perf_counter()))
        try:
            frame = await asyncio.wait_for(ws.recv(), timeout=timeout)
        except asyncio.TimeoutError:
            continue
        except websockets.exceptions.ConnectionClosed:
            break

        if not isinstance(frame, bytes):
            continue

        total_binary_frames += 1
        if len(frame) < MIN_MESSAGE_DATA_FRAME_BYTES:
            continue

        opcode = frame[OPCODE_OFFSET]
        if opcode == TIME_OPCODE:
            continue
        if opcode != MESSAGE_DATA_OPCODE:
            continue

        sub_id = struct.unpack_from("<I", frame, SUBSCRIPTION_ID_START)[0]
        if sub_id != subscription_id:
            continue

        log_ns = struct.unpack_from("<Q", frame, LOG_TIME_START)[0]
        payload = frame[MESSAGE_PAYLOAD_START:]
        receive_times.append(time.perf_counter())
        log_times_ns.append(log_ns)
        total_payload_bytes += len(payload)

        info = decode_compressed_pointcloud_payload(payload)
        if info is None:
            malformed_payload_count += 1
        else:
            decoded_samples.append(info)

    actual_duration = max(0.0, time.perf_counter() - start)
    wall_hz = len(receive_times) / actual_duration if actual_duration > 0 else 0.0
    log_hz = interval_rate_hz(log_times_ns)
    average_payload_bytes = total_payload_bytes / len(receive_times) if receive_times else 0.0

    return Measurement(
        topic=channel.topic,
        channel_id=channel.channel_id,
        encoding=channel.encoding,
        schema_name=channel.schema_name,
        schema_encoding=channel.schema_encoding,
        duration_seconds=actual_duration,
        message_count=len(receive_times),
        total_binary_frames=total_binary_frames,
        malformed_payload_count=malformed_payload_count,
        total_payload_bytes=total_payload_bytes,
        wall_hz=wall_hz,
        log_hz=log_hz,
        average_payload_bytes=average_payload_bytes,
        decoded_samples=decoded_samples,
    )


async def drain_for_seconds(ws: websockets.WebSocketClientProtocol, seconds: float) -> None:
    """Drain startup frames so the measurement window starts cleanly."""
    end = time.perf_counter() + seconds
    while time.perf_counter() < end:
        try:
            await asyncio.wait_for(ws.recv(), timeout=max(0.01, end - time.perf_counter()))
        except asyncio.TimeoutError:
            continue
        except websockets.exceptions.ConnectionClosed:
            break



__all__ = [name for name in globals() if not name.startswith("__")]
