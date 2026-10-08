"""Protocol constants, channel metadata, and endpoint helpers for Phase168."""

from __future__ import annotations

import argparse
import asyncio
import ipaddress
import json
import ssl
import time
import struct
from dataclasses import dataclass
from urllib.parse import parse_qsl, urlencode, urlsplit, urlunsplit

import websockets


DEFAULT_HOST = "127.0.0.1"
DEFAULT_PORT = 8765
DEFAULT_TOPIC = "/phase168/msgpack_smoke"
DEFAULT_ADVERTISE_TIMEOUT_SECONDS = 10.0
DEFAULT_IDLE_TIMEOUT_SECONDS = 15.0
DEFAULT_SETTLE_SECONDS = 0.25
DEFAULT_SUBSCRIPTION_ID = 168_000
FOXGLOVE_SUBPROTOCOL = "foxglove.sdk.v1"

EXPECTED_ENCODING = "msgpack"
EXPECTED_PHASE = 168
MAX_MSGPACK_DEPTH = 34
MAX_MSGPACK_CONTAINER_ITEMS = 16_384

MESSAGE_DATA_OPCODE = 1
TIME_OPCODE = 2
OPCODE_OFFSET = 0
SUBSCRIPTION_ID_START = 1
LOG_TIME_START = 5
MESSAGE_PAYLOAD_START = 13
MIN_MESSAGE_DATA_FRAME_BYTES = MESSAGE_PAYLOAD_START

EXIT_SUCCESS = 0
EXIT_FAILURE = 1
EXIT_TOPIC_NOT_FOUND = 3
EXIT_WRONG_CHANNEL = 4
EXIT_NO_MESSAGES = 5
EXIT_DECODE_FAILURE = 6


@dataclass(frozen=True)
class ChannelInfo:
    """Foxglove advertise metadata for one channel."""

    channel_id: int
    topic: str
    encoding: str
    schema_name: str
    schema: str
    schema_encoding: str


@dataclass(frozen=True)
class MsgPackSample:
    """Decoded Phase168 sample payload."""

    phase: int
    seq: int
    time_sec: float
    source: str
    active: bool
    position: tuple[float, float, float]
    payload_bytes: int
    log_time_ns: int


class TopicNotFoundError(RuntimeError):
    """Raised when the requested topic is not advertised."""


class MsgPackDecodeError(RuntimeError):
    """Raised when a MessagePack payload is malformed or unexpected."""


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

    if not is_loopback_url(url):
        raise ValueError("--insecure is only allowed for loopback WSS endpoints")

    context = ssl.create_default_context()
    context.check_hostname = False
    context.verify_mode = ssl.CERT_NONE
    return context


def is_loopback_url(url: str) -> bool:
    """Return whether a URL targets localhost or a loopback IP literal."""

    host = urlsplit(url).hostname
    if host is None:
        return False
    if host.lower() == "localhost":
        return True
    try:
        return ipaddress.ip_address(host).is_loopback
    except ValueError:
        return False



__all__ = [name for name in globals() if not name.startswith("__")]
