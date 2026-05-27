#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Measure real Foxglove WebSocket MessageData cadence for one topic.
# Usage: python Scripts/smoke/topic_rate_probe.py --port 8765 --topic /tf --target-hz 40
# Inputs: A running Unity FoxgloveManager endpoint.
# Outputs: Topic channel metadata, message rate, interval jitter, and verdict.

"""Measure protocol-level topic publish rate from a running Unity session."""

from __future__ import annotations

import argparse
import asyncio
import json
import math
import ssl
import struct
import time
from dataclasses import dataclass
from statistics import mean
from urllib.parse import parse_qsl, urlencode, urlsplit, urlunsplit

import websockets


DEFAULT_HOST = "127.0.0.1"
DEFAULT_PORT = 8765
DEFAULT_TOPIC = "/tf"
DEFAULT_DURATION_SECONDS = 15.0
DEFAULT_SETTLE_SECONDS = 1.0
DEFAULT_ADVERTISE_TIMEOUT_SECONDS = 10.0
DEFAULT_IDLE_TIMEOUT_SECONDS = 3.0
DEFAULT_TARGET_HZ = 0.0
DEFAULT_MIN_RATIO = 0.95
DEFAULT_GAP_FACTOR = 1.5
DEFAULT_SUBSCRIPTION_ID = 72_040
FOXGLOVE_SUBPROTOCOL = "foxglove.sdk.v1"

MESSAGE_DATA_OPCODE = 1
TIME_OPCODE = 2
OPCODE_OFFSET = 0
SUBSCRIPTION_ID_START = 1
SUBSCRIPTION_ID_END = 5
LOG_TIME_START = 5
LOG_TIME_END = 13
MESSAGE_PAYLOAD_START = 13
MIN_MESSAGE_DATA_FRAME_BYTES = MESSAGE_PAYLOAD_START

EXIT_SUCCESS = 0
EXIT_RATE_LOW = 2
EXIT_TOPIC_NOT_FOUND = 3
EXIT_NO_MESSAGES = 4


class TopicNotFoundError(RuntimeError):
    """Raised when the requested topic is absent from advertise frames."""


@dataclass(frozen=True)
class ChannelInfo:
    """Foxglove advertise metadata for one channel."""

    channel_id: int
    topic: str
    encoding: str
    schema_name: str


@dataclass(frozen=True)
class Measurement:
    """Measured topic cadence and payload statistics."""

    topic: str
    channel_id: int
    encoding: str
    schema_name: str
    duration_seconds: float
    message_count: int
    total_binary_frames: int
    total_payload_bytes: int
    wall_hz: float
    log_hz: float
    average_payload_bytes: float
    wall_intervals_ms: list[float]
    log_intervals_ms: list[float]


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
        frame = await asyncio.wait_for(ws.recv(), timeout=remaining)
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


async def measure_topic(
    ws: websockets.WebSocketClientProtocol,
    channel: ChannelInfo,
    subscription_id: int,
    duration_seconds: float,
    settle_seconds: float,
    idle_timeout_seconds: float,
) -> Measurement:
    """Collect MessageData frames for one subscription and compute raw intervals."""
    if settle_seconds > 0:
        await drain_for_seconds(ws, settle_seconds)

    start = time.perf_counter()
    end = start + duration_seconds
    total_binary_frames = 0
    total_payload_bytes = 0
    receive_times: list[float] = []
    log_times_ns: list[int] = []

    while time.perf_counter() < end:
        timeout = min(idle_timeout_seconds, max(0.01, end - time.perf_counter()))
        try:
            frame = await asyncio.wait_for(ws.recv(), timeout=timeout)
        except asyncio.TimeoutError:
            continue

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

        sub_id = struct.unpack("<I", frame[SUBSCRIPTION_ID_START:SUBSCRIPTION_ID_END])[0]
        if sub_id != subscription_id:
            continue

        log_ns = struct.unpack("<Q", frame[LOG_TIME_START:LOG_TIME_END])[0]
        payload = frame[MESSAGE_PAYLOAD_START:]
        receive_times.append(time.perf_counter())
        log_times_ns.append(log_ns)
        total_payload_bytes += len(payload)

    actual_duration = max(0.0, time.perf_counter() - start)
    wall_hz = len(receive_times) / actual_duration if actual_duration > 0 else 0.0
    wall_intervals_ms = intervals_ms(receive_times)
    log_intervals_ms = intervals_ms([value / 1_000_000_000.0 for value in log_times_ns])
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
        wall_intervals_ms=wall_intervals_ms,
        log_intervals_ms=log_intervals_ms,
    )


async def drain_for_seconds(ws: websockets.WebSocketClientProtocol, seconds: float) -> None:
    """Drain startup frames so the measurement window starts cleanly."""
    end = time.perf_counter() + seconds
    while time.perf_counter() < end:
        try:
            await asyncio.wait_for(ws.recv(), timeout=max(0.01, end - time.perf_counter()))
        except asyncio.TimeoutError:
            break


def intervals_ms(values: list[float]) -> list[float]:
    """Return adjacent intervals in milliseconds."""
    return [(values[index] - values[index - 1]) * 1000.0 for index in range(1, len(values))]


def interval_rate_hz(log_times_ns: list[int]) -> float:
    """Estimate rate from first and last message log time."""
    if len(log_times_ns) < 2:
        return 0.0

    elapsed_ns = log_times_ns[-1] - log_times_ns[0]
    if elapsed_ns <= 0:
        return 0.0

    return (len(log_times_ns) - 1) / (elapsed_ns / 1_000_000_000.0)


def percentile(values: list[float], percent: float) -> float:
    """Return nearest-rank percentile for a non-empty list."""
    if not values:
        return 0.0

    ordered = sorted(values)
    index = min(len(ordered) - 1, max(0, math.ceil((percent / 100.0) * len(ordered)) - 1))
    return ordered[index]


def verdict(measurement: Measurement, target_hz: float, min_ratio: float, gap_factor: float) -> tuple[str, int]:
    """Classify the measurement against a target rate."""
    if measurement.message_count == 0:
        return "NO_MESSAGES", EXIT_NO_MESSAGES

    if target_hz <= 0:
        return "MEASURED", EXIT_SUCCESS

    minimum_hz = target_hz * min_ratio
    target_interval_ms = 1000.0 / target_hz
    wall_gap_count = sum(1 for value in measurement.wall_intervals_ms if value > target_interval_ms * gap_factor)
    log_gap_count = sum(1 for value in measurement.log_intervals_ms if value > target_interval_ms * gap_factor)

    if measurement.wall_hz < minimum_hz:
        return "LOW", EXIT_RATE_LOW

    if wall_gap_count > max(1, measurement.message_count // 50) or log_gap_count > max(1, measurement.message_count // 50):
        return "JITTERY", EXIT_RATE_LOW

    return "PASS", EXIT_SUCCESS


def format_interval_stats(name: str, values: list[float], target_hz: float, gap_factor: float) -> str:
    """Format interval summary for human-readable output."""
    if not values:
        return f"{name}: n/a"

    target_interval_ms = 1000.0 / target_hz if target_hz > 0 else 0.0
    gap_threshold_ms = target_interval_ms * gap_factor if target_hz > 0 else 0.0
    gap_count = sum(1 for value in values if target_hz > 0 and value > gap_threshold_ms)
    return (
        f"{name}: avg={mean(values):.2f} ms, "
        f"p50={percentile(values, 50):.2f} ms, "
        f"p95={percentile(values, 95):.2f} ms, "
        f"max={max(values):.2f} ms, "
        f"gaps>{gap_factor:.2f}x={gap_count}"
    )


def print_report(measurement: Measurement, args: argparse.Namespace, result: str) -> None:
    """Print the final measurement report."""
    target_text = f"{args.target_hz:.2f} Hz" if args.target_hz > 0 else "not set"
    ratio = measurement.wall_hz / args.target_hz if args.target_hz > 0 else 0.0
    ratio_text = f" ({ratio * 100.0:.1f}% of target)" if args.target_hz > 0 else ""

    print(f"Endpoint: {args.effective_url}")
    print(f"Topic: {measurement.topic}")
    print(
        "Channel: "
        f"id={measurement.channel_id}, "
        f"encoding={measurement.encoding or '(unknown)'}, "
        f"schema={measurement.schema_name or '(unknown)'}"
    )
    print(f"Target: {target_text}")
    print(f"Messages: {measurement.message_count}")
    print(f"Total binary frames seen: {measurement.total_binary_frames}")
    print(f"Duration: {measurement.duration_seconds:.3f} s")
    print(f"Wall rate: {measurement.wall_hz:.2f} Hz{ratio_text}")
    print(f"Log-time rate: {measurement.log_hz:.2f} Hz")
    print(
        "Payload: "
        f"total={measurement.total_payload_bytes} bytes, "
        f"avg={measurement.average_payload_bytes:.1f} bytes/message"
    )
    print(format_interval_stats("Wall intervals", measurement.wall_intervals_ms, args.target_hz, args.gap_factor))
    print(format_interval_stats("Log intervals", measurement.log_intervals_ms, args.target_hz, args.gap_factor))
    print(f"Verdict: {result}")


async def run(args: argparse.Namespace) -> int:
    """Connect, subscribe, measure, and print a topic cadence report."""
    url = build_url(args)
    args.effective_url = redact_token(url)
    ssl_context = build_ssl_context(url, args.insecure)

    try:
        async with websockets.connect(url, subprotocols=[FOXGLOVE_SUBPROTOCOL], ssl=ssl_context) as ws:
            channel = await wait_for_channel(ws, args.topic, args.advertise_timeout_seconds)
            await subscribe(ws, channel.channel_id, args.subscription_id)
            measurement = await measure_topic(
                ws,
                channel,
                args.subscription_id,
                args.duration,
                args.settle_seconds,
                args.idle_timeout_seconds,
            )
    except TopicNotFoundError as exc:
        print(f"Verdict: TOPIC_NOT_FOUND")
        print(str(exc))
        return EXIT_TOPIC_NOT_FOUND

    result, exit_code = verdict(measurement, args.target_hz, args.min_ratio, args.gap_factor)
    print_report(measurement, args, result)
    return exit_code


def redact_token(url: str) -> str:
    """Hide token query values in console output."""
    parts = urlsplit(url)
    query = parse_qsl(parts.query, keep_blank_values=True)
    redacted = [(key, "REDACTED" if key == "token" else value) for key, value in query]
    return urlunsplit((parts.scheme, parts.netloc, parts.path, urlencode(redacted), parts.fragment))


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
    parser.add_argument("--duration", type=float, default=DEFAULT_DURATION_SECONDS)
    parser.add_argument("--settle-seconds", type=float, default=DEFAULT_SETTLE_SECONDS)
    parser.add_argument("--target-hz", type=float, default=DEFAULT_TARGET_HZ)
    parser.add_argument("--min-ratio", type=float, default=DEFAULT_MIN_RATIO)
    parser.add_argument("--gap-factor", type=float, default=DEFAULT_GAP_FACTOR)
    parser.add_argument("--subscription-id", type=int, default=DEFAULT_SUBSCRIPTION_ID)
    parser.add_argument("--advertise-timeout-seconds", type=float, default=DEFAULT_ADVERTISE_TIMEOUT_SECONDS)
    parser.add_argument("--idle-timeout-seconds", type=float, default=DEFAULT_IDLE_TIMEOUT_SECONDS)
    return parser.parse_args()


def main() -> int:
    """Run the async probe from a synchronous CLI entry point."""
    return asyncio.run(run(parse_args()))


if __name__ == "__main__":
    raise SystemExit(main())
