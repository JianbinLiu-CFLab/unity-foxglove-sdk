"""Protocol constants, endpoint validation, and observation models for Phase139."""

from __future__ import annotations

import ipaddress
import os
import ssl
from dataclasses import dataclass
from typing import Any
from urllib.parse import urlsplit


PHASE = "139"
FOXGLOVE_SUBPROTOCOL = "foxglove.sdk.v1"
MESSAGE_DATA_OPCODE = 1

DEFAULT_WEBSOCKET_URL = "ws://127.0.0.1:8765"
DEFAULT_WEBSOCKET_SCENARIO = "maze-websocket-default"
DEFAULT_ROS2_SCENARIO = "maze-ros2-native"

DEFAULT_TF_TOPIC = "/tf"
DEFAULT_IMU_TOPIC = "/imu/data"
DEFAULT_POINTCLOUD_TOPIC = "/unity/point_cloud2"
DEFAULT_DESKEWED_POINTCLOUD_TOPIC = "/unity/point_cloud2_deskewed"
DEFAULT_RAW_IMAGE_TOPIC = "/unity/sensor/camera/image"
DEFAULT_COMPRESSED_IMAGE_TOPIC = "/unity/sensor/camera/image/compressed"
DEFAULT_CAMERA_INFO_TOPIC = "/unity/sensor/camera/camera_info"


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


@dataclass(frozen=True)
class TopicExpectation:
    """Topic expected by a Phase139 scenario."""

    topic: str
    classification: str
    family: str
    min_messages: int = 1
    note: str = ""


@dataclass
class ObservedTopic:
    """Runtime observations for one subscribed topic."""

    topic: str
    classification: str
    family: str
    channel_id: int | None = None
    encoding: str | None = None
    schema_name: str | None = None
    messages: int = 0
    payload_bytes: int = 0
    first_monotonic_time: float | None = None
    last_monotonic_time: float | None = None
    first_log_time: int | None = None
    last_log_time: int | None = None
    note: str = ""

    def to_json(self, duration_seconds: float) -> dict[str, Any]:
        """Return stable JSON evidence for this topic."""

        approx_hz = 0.0
        if self.first_monotonic_time is not None and self.last_monotonic_time is not None:
            elapsed = max(self.last_monotonic_time - self.first_monotonic_time, 0.0)
            if elapsed > 0.0 and self.messages > 1:
                approx_hz = (self.messages - 1) / elapsed
            elif duration_seconds > 0.0:
                approx_hz = self.messages / duration_seconds

        return {
            "topic": self.topic,
            "classification": self.classification,
            "family": self.family,
            "observed": self.messages > 0,
            "messages": self.messages,
            "payload_bytes": self.payload_bytes,
            "approx_hz": round(approx_hz, 3),
            "channel_id": self.channel_id,
            "encoding": self.encoding,
            "schema_name": self.schema_name,
            "first_log_time": self.first_log_time,
            "last_log_time": self.last_log_time,
            "note": self.note,
        }



__all__ = [name for name in globals() if not name.startswith("__")]
