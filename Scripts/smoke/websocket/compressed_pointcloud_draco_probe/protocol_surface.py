"""Protocol constants, channel metadata, and endpoint helpers for the Draco probe."""

from __future__ import annotations

import argparse
import asyncio
import ipaddress
import json
import ssl
import time
from dataclasses import dataclass
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
EXIT_DECODE_FAILURE = 6


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
