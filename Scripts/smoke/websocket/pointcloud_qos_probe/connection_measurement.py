"""Measure protocol-level point-cloud QoS output from a Unity Foxglove endpoint."""

from __future__ import annotations

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
            )
            channels[channel.topic] = channel

        if topic in channels:
            return channels[topic]

    known = ", ".join(sorted(channels)) if channels else "(none)"
    raise TopicNotFoundError(f"Topic {topic!r} was not advertised. Known topics: {known}")


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


async def measure_pointcloud(
    ws: websockets.WebSocketClientProtocol,
    channel: ChannelInfo,
    subscription_id: int,
    duration_seconds: float,
    settle_seconds: float,
    idle_timeout_seconds: float,
) -> Measurement:
    """Collect MessageData frames and decode point-cloud payload statistics."""
    if settle_seconds > 0:
        await drain_for_seconds(ws, settle_seconds)

    start = time.perf_counter()
    end = start + duration_seconds
    total_binary_frames = 0
    total_payload_bytes = 0
    receive_times: list[float] = []
    log_times_ns: list[int] = []
    decoded_samples: list[PointCloudPayloadInfo] = []

    while time.perf_counter() < end:
        timeout = min(idle_timeout_seconds, max(0.01, end - time.perf_counter()))
        try:
            frame = await asyncio.wait_for(ws.recv(), timeout=timeout)
        except asyncio.TimeoutError:
            continue

        if not isinstance(frame, bytes):
            continue

        if len(frame) < MIN_MESSAGE_DATA_FRAME_BYTES:
            continue

        opcode = frame[OPCODE_OFFSET]
        if opcode == TIME_OPCODE:
            continue

        if opcode != MESSAGE_DATA_OPCODE:
            continue

        total_binary_frames += 1
        sub_id = struct.unpack_from("<I", frame, SUBSCRIPTION_ID_START)[0]
        if sub_id != subscription_id:
            continue

        log_ns = struct.unpack_from("<Q", frame, LOG_TIME_START)[0]
        payload = frame[MESSAGE_PAYLOAD_START:]
        receive_times.append(time.perf_counter())
        log_times_ns.append(log_ns)
        total_payload_bytes += len(payload)

        info = decode_pointcloud_payload(payload, channel.encoding)
        if info is not None:
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
        duration_seconds=actual_duration,
        message_count=len(receive_times),
        total_binary_frames=total_binary_frames,
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
            break

__all__ = [name for name in globals() if not name.startswith("__")]
