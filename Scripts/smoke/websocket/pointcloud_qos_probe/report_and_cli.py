"""Measure protocol-level point-cloud QoS output from a Unity Foxglove endpoint."""

from __future__ import annotations

from .protocol_surface import *

def interval_rate_hz(log_times_ns: list[int]) -> float:
    """Estimate rate from first and last message log time."""
    if len(log_times_ns) < 2:
        return 0.0

    elapsed_ns = log_times_ns[-1] - log_times_ns[0]
    if elapsed_ns <= 0:
        return 0.0

    return (len(log_times_ns) - 1) / (elapsed_ns / 1_000_000_000.0)


def point_count_stats(samples: list[PointCloudPayloadInfo]) -> tuple[int, float, int]:
    """Return min, average, and max estimated point counts."""
    counts = [sample.estimated_point_count for sample in samples]
    if not counts:
        return 0, 0.0, 0

    avg_point_count = mean(counts)
    return min(counts), avg_point_count, max(counts)


def print_report(measurement: Measurement, args: argparse.Namespace, result: str) -> None:
    """Print the final measurement report."""
    decoded = measurement.decoded_samples
    point_stride_values = sorted({sample.point_stride for sample in decoded})
    min_points, avg_point_count, max_points = point_count_stats(decoded)
    average_data_bytes = mean([sample.data_bytes for sample in decoded]) if decoded else 0.0

    print(f"Endpoint: {args.effective_url}")
    print(f"Topic: {measurement.topic}")
    print(
        "Channel: "
        f"id={measurement.channel_id}, "
        f"encoding={measurement.encoding or '(unknown)'}, "
        f"schema={measurement.schema_name or '(unknown)'}"
    )
    print(f"Messages: {measurement.message_count}")
    print(f"Decoded PointCloud messages: {len(decoded)}")
    print(f"Total binary frames seen: {measurement.total_binary_frames}")
    print(f"Duration: {measurement.duration_seconds:.3f} s")
    print(f"Wall rate: {measurement.wall_hz:.2f} Hz")
    print(f"Log-time rate: {measurement.log_hz:.2f} Hz")
    print(
        "Payload: "
        f"total={measurement.total_payload_bytes} bytes, "
        f"avg={measurement.average_payload_bytes:.1f} bytes/message"
    )
    print(f"Point stride values: {point_stride_values or 'n/a'}")
    print(f"PointCloud.data: avg={average_data_bytes:.1f} bytes/message")
    print(
        "Estimated point count: "
        f"min={min_points}, avg={avg_point_count:.1f}, max={max_points}"
    )
    print(f"Verdict: {result}")


async def run(args: argparse.Namespace) -> int:
    """Connect, subscribe, measure, and print a point-cloud QoS report."""
    url = build_url(args)
    args.effective_url = redact_token(url)
    ssl_context = build_ssl_context(url, args.insecure)

    try:
        async with websockets.connect(url, subprotocols=[FOXGLOVE_SUBPROTOCOL], ssl=ssl_context) as ws:
            channel = await wait_for_channel(ws, args.topic, args.advertise_timeout_seconds)
            await subscribe(ws, channel.channel_id, args.subscription_id)
            measurement = await measure_pointcloud(
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
    elif measurement.decoded_samples:
        result = "PASS"
        exit_code = EXIT_SUCCESS
    else:
        result = "DECODE_FAILURE"
        exit_code = EXIT_DECODE_FAILURE

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
    parser.add_argument("--subscription-id", type=int, default=DEFAULT_SUBSCRIPTION_ID)
    parser.add_argument("--advertise-timeout-seconds", type=float, default=DEFAULT_ADVERTISE_TIMEOUT_SECONDS)
    parser.add_argument("--idle-timeout-seconds", type=float, default=DEFAULT_IDLE_TIMEOUT_SECONDS)
    return parser.parse_args()


def main() -> int:
    """Run the async probe from a synchronous CLI entry point."""
    return asyncio.run(run(parse_args()))



__all__ = [name for name in globals() if not name.startswith("__")]
