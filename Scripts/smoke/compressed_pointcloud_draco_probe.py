#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Measure foxglove.CompressedPointCloud Draco payloads from Unity.
# Usage: python Scripts/smoke/compressed_pointcloud_draco_probe.py --port 8765 --duration 20
# Inputs: A running Unity FoxgloveManager endpoint with /unity/point_cloud_draco advertised.
# Outputs: Compressed point-cloud rate, payload size, Draco bytes, and format evidence.

"""Measure protocol-level Draco CompressedPointCloud output from Unity."""

from __future__ import annotations

import argparse
import asyncio
import json
import ssl
import struct
import time
from dataclasses import dataclass
from statistics import mean
from urllib.parse import parse_qsl, urlencode, urlsplit, urlunsplit

import websockets


DEFAULT_HOST = "127.0.0.1"
DEFAULT_PORT = 8765
DEFAULT_TOPIC = "/unity/point_cloud_draco"
DEFAULT_DURATION_SECONDS = 15.0
DEFAULT_SETTLE_SECONDS = 1.0
DEFAULT_ADVERTISE_TIMEOUT_SECONDS = 10.0
DEFAULT_IDLE_TIMEOUT_SECONDS = 3.0
DEFAULT_SUBSCRIPTION_ID = 88_000
FOXGLOVE_SUBPROTOCOL = "foxglove.sdk.v1"

EXPECTED_SCHEMA_NAME = "foxglove.CompressedPointCloud"
EXPECTED_ENCODING = "protobuf"
EXPECTED_SCHEMA_ENCODING = "protobuf"
RAW_POINTCLOUD_SCHEMA_NAME = "foxglove.PointCloud"

MESSAGE_DATA_OPCODE = 1
TIME_OPCODE = 2
OPCODE_OFFSET = 0
SUBSCRIPTION_ID_START = 1
SUBSCRIPTION_ID_END = 5
LOG_TIME_START = 5
LOG_TIME_END = 13
MESSAGE_PAYLOAD_START = 13
MIN_MESSAGE_DATA_FRAME_BYTES = MESSAGE_PAYLOAD_START

COMPRESSED_POINTCLOUD_DATA_FIELD = 4
COMPRESSED_POINTCLOUD_FORMAT_FIELD = 5
COMPRESSED_POINTCLOUD_DATA_TAG = 34
COMPRESSED_POINTCLOUD_FORMAT_TAG = 42

WIRE_VARINT = 0
WIRE_FIXED64 = 1
WIRE_LENGTH_DELIMITED = 2
WIRE_START_GROUP = 3
WIRE_END_GROUP = 4
WIRE_FIXED32 = 5

EXIT_SUCCESS = 0
EXIT_FAILURE = 1
EXIT_TOPIC_NOT_FOUND = 3
EXIT_NO_MESSAGES = 4
EXIT_WRONG_CHANNEL = 5


@dataclass(frozen=True)
class ChannelInfo:
    """Foxglove advertise metadata for one channel."""

    channel_id: int
    topic: str
    encoding: str
    schema_name: str
    schema_encoding: str


@dataclass(frozen=True)
class CompressedPointCloudPayloadInfo:
    """Decoded CompressedPointCloud payload details."""

    draco_data_bytes: int
    format: str


@dataclass(frozen=True)
class Measurement:
    """Measured compressed point-cloud summary."""

    topic: str
    channel_id: int
    encoding: str
    schema_name: str
    schema_encoding: str
    duration_seconds: float
    message_count: int
    total_binary_frames: int
    malformed_payload_count: int
    total_payload_bytes: int
    wall_hz: float
    log_hz: float
    average_payload_bytes: float
    decoded_samples: list[CompressedPointCloudPayloadInfo]


class TopicNotFoundError(RuntimeError):
    """Raised when the requested topic is not advertised."""


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
                schema_encoding=str(raw_channel.get("schemaEncoding", "")),
            )
            channels[channel.topic] = channel

        if topic in channels:
            return channels[topic]

    known = ", ".join(sorted(channels)) if channels else "(none)"
    raise TopicNotFoundError(f"Topic {topic!r} was not advertised. Known topics: {known}")


def validate_channel(channel: ChannelInfo) -> tuple[str, str]:
    """Return a verdict and diagnostic for channel metadata."""
    encoding = channel.encoding.lower()
    schema_encoding = channel.schema_encoding.lower()
    if channel.schema_name == RAW_POINTCLOUD_SCHEMA_NAME:
        return "WRONG_SCHEMA", "Requested channel is raw foxglove.PointCloud, not CompressedPointCloud."

    if channel.schema_name != EXPECTED_SCHEMA_NAME:
        return "WRONG_SCHEMA", f"Expected schemaName={EXPECTED_SCHEMA_NAME}, got {channel.schema_name or '(empty)'}."

    if encoding != EXPECTED_ENCODING:
        return "WRONG_ENCODING", f"Expected encoding = protobuf, got {channel.encoding or '(empty)'}."

    if schema_encoding != EXPECTED_SCHEMA_ENCODING:
        return "WRONG_ENCODING", f"Expected schemaEncoding = protobuf, got {channel.schema_encoding or '(empty)'}."

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
            break


def decode_compressed_pointcloud_payload(payload: bytes) -> CompressedPointCloudPayloadInfo | None:
    """Decode data and format from foxglove.CompressedPointCloud protobuf bytes."""
    index = 0
    draco_data_bytes = 0
    observed_format = ""

    while index < len(payload):
        try:
            index, tag = read_varint(payload, index)
        except ValueError:
            return None

        wire_type = tag & 0x07
        field_number = tag >> 3

        try:
            if tag == COMPRESSED_POINTCLOUD_DATA_TAG or (
                field_number == COMPRESSED_POINTCLOUD_DATA_FIELD and wire_type == WIRE_LENGTH_DELIMITED
            ):
                index, length = read_varint(payload, index)
                if length <= 0 or index + length > len(payload):
                    return None
                draco_data_bytes = length
                index += length
            elif tag == COMPRESSED_POINTCLOUD_FORMAT_TAG or (
                field_number == COMPRESSED_POINTCLOUD_FORMAT_FIELD and wire_type == WIRE_LENGTH_DELIMITED
            ):
                index, length = read_varint(payload, index)
                if length <= 0 or index + length > len(payload):
                    return None
                observed_format = payload[index : index + length].decode("utf-8")
                index += length
            else:
                index = skip_field(payload, index, wire_type)
        except (UnicodeDecodeError, ValueError):
            return None

    if draco_data_bytes <= 0 or not observed_format:
        return None

    return CompressedPointCloudPayloadInfo(
        draco_data_bytes=draco_data_bytes,
        format=observed_format,
    )


def read_varint(data: bytes, index: int) -> tuple[int, int]:
    """Read one protobuf varint from data starting at index."""
    shift = 0
    value = 0
    while index < len(data) and shift < 64:
        byte = data[index]
        index += 1
        value |= (byte & 0x7F) << shift
        if byte < 0x80:
            return index, value
        shift += 7

    raise ValueError("Malformed protobuf varint.")


def skip_field(data: bytes, index: int, wire_type: int) -> int:
    """Skip one protobuf field payload by wire type."""
    if wire_type == WIRE_VARINT:
        index, _ = read_varint(data, index)
        return index

    if wire_type == WIRE_FIXED64:
        return checked_index(data, index + 8)

    if wire_type == WIRE_LENGTH_DELIMITED:
        index, length = read_varint(data, index)
        return checked_index(data, index + length)

    if wire_type == WIRE_FIXED32:
        return checked_index(data, index + 4)

    if wire_type in (WIRE_START_GROUP, WIRE_END_GROUP):
        raise ValueError("Unsupported protobuf group wire type.")

    raise ValueError(f"Unsupported protobuf wire type {wire_type}.")


def checked_index(data: bytes, index: int) -> int:
    """Return index when it is inside the buffer, otherwise raise."""
    if index > len(data):
        raise ValueError("Protobuf field extends beyond payload.")
    return index


def interval_rate_hz(log_times_ns: list[int]) -> float:
    """Estimate rate from first and last message log time."""
    if len(log_times_ns) < 2:
        return 0.0

    elapsed_ns = log_times_ns[-1] - log_times_ns[0]
    if elapsed_ns <= 0:
        return 0.0

    return (len(log_times_ns) - 1) / (elapsed_ns / 1_000_000_000.0)


def observed_format_values(samples: list[CompressedPointCloudPayloadInfo]) -> list[str]:
    """Return sorted unique format values from decoded samples."""
    return sorted({sample.format for sample in samples})


def print_report(measurement: Measurement, args: argparse.Namespace, result: str) -> None:
    """Print the final measurement report."""
    decoded = measurement.decoded_samples
    average_draco_data_bytes = mean([sample.draco_data_bytes for sample in decoded]) if decoded else 0.0
    formats = observed_format_values(decoded)

    print(f"Endpoint: {args.effective_url}")
    print(f"Topic: {measurement.topic}")
    print(
        "Channel: "
        f"id={measurement.channel_id}, "
        f"encoding={measurement.encoding or '(unknown)'}, "
        f"schema={measurement.schema_name or '(unknown)'}, "
        f"schemaEncoding={measurement.schema_encoding or '(unknown)'}"
    )
    print(f"Messages: {measurement.message_count}")
    print(f"Decoded CompressedPointCloud messages: {len(decoded)}")
    print(f"Malformed CompressedPointCloud payloads: {measurement.malformed_payload_count}")
    print(f"Total binary frames seen: {measurement.total_binary_frames}")
    print(f"Duration: {measurement.duration_seconds:.3f} s")
    print(f"Wall rate: {measurement.wall_hz:.2f} Hz")
    print(f"Log-time rate: {measurement.log_hz:.2f} Hz")
    print(
        "Outer payload: "
        f"total={measurement.total_payload_bytes} bytes, "
        f"avg={measurement.average_payload_bytes:.1f} bytes/message"
    )
    print(f"CompressedPointCloud.data: avg={average_draco_data_bytes:.1f} bytes/message")
    print(f"Observed format values: {formats or 'n/a'}")
    print(f"Verdict: {result}")


async def run(args: argparse.Namespace) -> int:
    """Connect, subscribe, measure, and print a compressed point-cloud report."""
    url = build_url(args)
    args.effective_url = redact_token(url)
    ssl_context = build_ssl_context(url, args.insecure)

    try:
        async with websockets.connect(url, subprotocols=[FOXGLOVE_SUBPROTOCOL], ssl=ssl_context) as ws:
            channel = await wait_for_channel(ws, args.topic, args.advertise_timeout_seconds)
            channel_result, diagnostic = validate_channel(channel)
            if channel_result != "OK":
                print(f"Verdict: {channel_result}")
                print(
                    "Channel: "
                    f"id={channel.channel_id}, topic={channel.topic}, encoding={channel.encoding}, "
                    f"schemaName={channel.schema_name}, schemaEncoding={channel.schema_encoding}"
                )
                print(diagnostic)
                return EXIT_WRONG_CHANNEL

            await subscribe(ws, channel.channel_id, args.subscription_id)
            measurement = await measure_compressed_pointcloud(
                ws,
                channel,
                args.subscription_id,
                args.duration,
                args.settle_seconds,
                args.idle_timeout_seconds,
            )
    except TopicNotFoundError as exc:
        print("Verdict: TOPIC_NOT_FOUND")
        print(str(exc))
        return EXIT_TOPIC_NOT_FOUND

    if measurement.message_count == 0:
        result = "NO_MESSAGES"
        exit_code = EXIT_NO_MESSAGES
    elif (
        measurement.decoded_samples
        and measurement.malformed_payload_count == 0
        and all(sample.format == "draco" and sample.draco_data_bytes > 0 for sample in measurement.decoded_samples)
    ):
        result = "PASS"
        exit_code = EXIT_SUCCESS
    else:
        result = "MEASURED"
        exit_code = EXIT_SUCCESS

    print_report(measurement, args, result)
    return exit_code


def redact_token(url: str) -> str:
    """Hide token query values in console output."""
    parts = urlsplit(url)
    query = parse_qsl(parts.query, keep_blank_values=True)
    redacted = [(key, "REDACTED" if key == "token" else value) for key, value in query]
    return urlunsplit((parts.scheme, parts.netloc, parts.path, urlencode(redacted), parts.fragment))


def run_self_test() -> int:
    """Run offline decoder checks without a Unity session."""
    sample = (
        bytes([COMPRESSED_POINTCLOUD_DATA_TAG, 3])
        + b"\x01\x02\x03"
        + bytes([COMPRESSED_POINTCLOUD_FORMAT_TAG, 5])
        + b"draco"
    )
    decoded = decode_compressed_pointcloud_payload(sample)
    if decoded is None or decoded.draco_data_bytes != 3 or decoded.format != "draco":
        print("self-test failed: expected format=draco and data length 3")
        return EXIT_FAILURE

    malformed = decode_compressed_pointcloud_payload(bytes([COMPRESSED_POINTCLOUD_DATA_TAG, 8]) + b"\x01")
    if malformed is not None:
        print("self-test failed: malformed payload decoded unexpectedly")
        return EXIT_FAILURE

    print("self-test pass: format=draco data_bytes=3")
    return EXIT_SUCCESS


def parse_args() -> argparse.Namespace:
    """Parse command line arguments."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--url", help="Full ws:// or wss:// endpoint. Overrides host/port/wss.")
    parser.add_argument("--host", default=DEFAULT_HOST)
    parser.add_argument("--port", type=int, default=DEFAULT_PORT)
    parser.add_argument("--wss", action="store_true", help="Use wss:// when --url is not supplied.")
    parser.add_argument("--insecure", action="store_true", help="Disable TLS certificate validation for wss endpoints.")
    parser.add_argument("--token", help="Authentication token appended as a query parameter.")
    parser.add_argument("--topic", default=DEFAULT_TOPIC)
    parser.add_argument("--duration", type=float, default=DEFAULT_DURATION_SECONDS)
    parser.add_argument("--settle-seconds", type=float, default=DEFAULT_SETTLE_SECONDS)
    parser.add_argument("--advertise-timeout-seconds", type=float, default=DEFAULT_ADVERTISE_TIMEOUT_SECONDS)
    parser.add_argument("--idle-timeout-seconds", type=float, default=DEFAULT_IDLE_TIMEOUT_SECONDS)
    parser.add_argument("--subscription-id", type=int, default=DEFAULT_SUBSCRIPTION_ID)
    parser.add_argument("--self-test", action="store_true", help="Run offline decoder self-test and exit.")
    return parser.parse_args()


def main() -> int:
    """CLI entry point."""
    args = parse_args()
    if args.self_test:
        return run_self_test()

    return asyncio.run(run(args))


if __name__ == "__main__":
    raise SystemExit(main())
