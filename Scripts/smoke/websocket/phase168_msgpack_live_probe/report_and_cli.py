"""Reporting and command-line execution for Phase168."""

from __future__ import annotations

import argparse
import asyncio
from urllib.parse import parse_qsl, urlencode, urlsplit, urlunsplit

import websockets

from .channel_subscription import *
from .frame_collection import *
from .msgpack_decoder import *
from .protocol_surface import *
from .sample_validation import *


async def run(args: argparse.Namespace) -> int:
    """Connect, subscribe, decode one sample, and print a verdict."""
    url = build_url(args)
    args.effective_url = redact_token(url)
    ssl_context = build_ssl_context(url, args.insecure)

    try:
        async with websockets.connect(url, subprotocols=[FOXGLOVE_SUBPROTOCOL], ssl=ssl_context) as ws:
            channel = await wait_for_channel(ws, args.topic, args.advertise_timeout_seconds)
            verdict, detail = validate_channel(channel)
            print_channel(args.effective_url, channel)
            if verdict != "OK":
                print(f"Verdict: {verdict}")
                print(detail)
                return EXIT_WRONG_CHANNEL

            await subscribe(ws, channel.channel_id, args.subscription_id)
            if args.settle_seconds > 0:
                await drain_for_seconds(ws, args.settle_seconds)
            sample = await wait_for_msgpack_sample(ws, args.subscription_id, args.idle_timeout_seconds)
    except TopicNotFoundError as exc:
        print("Verdict: TOPIC_NOT_FOUND")
        print(str(exc))
        return EXIT_TOPIC_NOT_FOUND
    except TimeoutError as exc:
        print("Verdict: NO_MESSAGES")
        print(str(exc))
        return EXIT_NO_MESSAGES
    except MsgPackDecodeError as exc:
        print("Verdict: DECODE_FAILURE")
        print(str(exc))
        return EXIT_DECODE_FAILURE

    print_sample(sample)
    print("Verdict: PASS")
    return EXIT_SUCCESS


def print_channel(endpoint: str, channel: ChannelInfo) -> None:
    """Print channel metadata."""
    print(f"Endpoint: {endpoint}")
    print(f"Topic: {channel.topic}")
    print(
        "Channel: "
        f"id={channel.channel_id}, "
        f"encoding={channel.encoding or '(unknown)'}, "
        f"schemaName={channel.schema_name!r}, "
        f"schemaEncoding={channel.schema_encoding!r}"
    )


def print_sample(sample: MsgPackSample) -> None:
    """Print decoded sample details."""
    x, y, z = sample.position
    print(
        "Decoded sample: "
        f"phase={sample.phase}, seq={sample.seq}, active={sample.active}, "
        f"source={sample.source!r}, position=({x:.3f}, {y:.3f}, {z:.3f}), "
        f"timeSec={sample.time_sec:.6f}, logTimeNs={sample.log_time_ns}, "
        f"payloadBytes={sample.payload_bytes}"
    )


def redact_token(url: str) -> str:
    """Hide token query values in console output."""
    parts = urlsplit(url)
    query = parse_qsl(parts.query, keep_blank_values=True)
    redacted = [(key, "REDACTED" if key == "token" else value) for key, value in query]
    return urlunsplit((parts.scheme, parts.netloc, parts.path, urlencode(redacted), parts.fragment))


def run_self_test() -> int:
    """Run offline decoder checks without a Unity session."""
    sample = (
        b"\x86"
        b"\xa5phase\xcd\x00\xa8"
        b"\xa3seq\x01"
        b"\xa7timeSec\xcb@\x09!\xfbTD-\x18"
        b"\xa6source\xa5smoke"
        b"\xa6active\xc3"
        b"\xa8position\x93\xca?\x80\x00\x00\xca@\x00\x00\x00\xca@@\x00\x00"
    )
    decoded = validate_sample(decode_complete_msgpack(sample), len(sample), 123)
    if decoded.phase != 168 or decoded.seq != 1 or decoded.source != "smoke" or decoded.position != (1.0, 2.0, 3.0):
        print("self-test failed: decoded sample fields did not match")
        return EXIT_FAILURE

    try:
        decode_complete_msgpack(b"\x81\xa5phase")
    except MsgPackDecodeError:
        print("self-test pass: decoded Phase168 MsgPack sample and rejected malformed bytes")
        return EXIT_SUCCESS

    print("self-test failed: malformed bytes decoded unexpectedly")
    return EXIT_FAILURE


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
    parser.add_argument("--subscription-id", type=int, default=DEFAULT_SUBSCRIPTION_ID)
    parser.add_argument("--advertise-timeout-seconds", type=float, default=DEFAULT_ADVERTISE_TIMEOUT_SECONDS)
    parser.add_argument("--idle-timeout-seconds", type=float, default=DEFAULT_IDLE_TIMEOUT_SECONDS)
    parser.add_argument("--settle-seconds", type=float, default=DEFAULT_SETTLE_SECONDS)
    parser.add_argument("--self-test", action="store_true", help="Run offline MsgPack decoder self-test and exit.")
    return parser.parse_args()


def main() -> int:
    """CLI entry point."""
    args = parse_args()
    if args.self_test:
        return run_self_test()

    return asyncio.run(run(args))




__all__ = [name for name in globals() if not name.startswith("__")]
