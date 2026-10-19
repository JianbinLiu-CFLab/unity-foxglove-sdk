"""Reporting and command-line execution for the Draco probe."""

from __future__ import annotations

import argparse
import asyncio
from statistics import mean
from urllib.parse import parse_qsl, urlencode, urlsplit, urlunsplit

import websockets

from .channel_subscription import *
from .measurement import *
from .payload_decoding import *
from .protocol_surface import *


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
    elif not measurement.decoded_samples:
        result = "DECODE_FAILURE"
        exit_code = EXIT_DECODE_FAILURE
    elif measurement.malformed_payload_count > 0 and not args.allow_malformed_payloads:
        result = "DECODE_FAILURE"
        exit_code = EXIT_DECODE_FAILURE
    elif all(sample.format == "draco" and sample.draco_data_bytes > 0 for sample in measurement.decoded_samples):
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
    parser.add_argument(
        "--allow-malformed-payloads",
        action="store_true",
        help="Keep exit code 0 for diagnostic runs even when some received payloads fail to decode.",
    )
    parser.add_argument("--self-test", action="store_true", help="Run offline decoder self-test and exit.")
    return parser.parse_args()


def main() -> int:
    """CLI entry point."""
    args = parse_args()
    if args.self_test:
        return run_self_test()

    return asyncio.run(run(args))




__all__ = [name for name in globals() if not name.startswith("__")]
