"""Stable summary construction for Phase139 acceptance evidence."""

from __future__ import annotations

import time
from typing import Any

from .argument_contract import *
from .protocol_surface import *


def make_summary(
    *,
    args: argparse.Namespace,
    status: str,
    topics: dict[str, ObservedTopic],
    limitations: list[str],
    mcap: dict[str, Any] | None = None,
    ros2_native: dict[str, Any] | None = None,
) -> dict[str, Any]:
    """Assemble stable JSON evidence."""

    duration = float(getattr(args, "duration", 0.0) or 0.0)
    return {
        "phase": PHASE,
        "mode": "self-test" if getattr(args, "self_test", False) else args.mode,
        "scenario": args.scenario,
        "status": status,
        "generated_at_unix": round(time.time(), 3),
        "foxglove_websocket": {
            "url": getattr(args, "url", DEFAULT_WEBSOCKET_URL),
            "subprotocol": FOXGLOVE_SUBPROTOCOL,
        },
        "topics": {topic: observed.to_json(duration) for topic, observed in sorted(topics.items())},
        "mcap": mcap or {"checked": False, "status": "skipped"},
        "ros2_native": ros2_native or {"checked": False, "status": "skipped"},
        "limitations": limitations,
    }


def self_test_summary(args: argparse.Namespace) -> dict[str, Any]:
    """Generate deterministic evidence for the C# validation gate."""

    observed = {
        DEFAULT_TF_TOPIC: ObservedTopic(DEFAULT_TF_TOPIC, "required", "tf", channel_id=1, encoding="protobuf", schema_name="foxglove.FrameTransform", messages=3, payload_bytes=384),
        DEFAULT_IMU_TOPIC: ObservedTopic(DEFAULT_IMU_TOPIC, "optional", "imu", channel_id=2, encoding="protobuf", schema_name="unity2foxglove.Imu", messages=2, payload_bytes=256),
        DEFAULT_POINTCLOUD_TOPIC: ObservedTopic(DEFAULT_POINTCLOUD_TOPIC, "optional", "pointcloud", channel_id=3, encoding="ros2cdr", schema_name="sensor_msgs/msg/PointCloud2", messages=1, payload_bytes=950160),
    }
    now = time.monotonic()
    for index, topic in enumerate(observed.values()):
        topic.first_monotonic_time = now + index * 0.01
        topic.last_monotonic_time = topic.first_monotonic_time + 0.2
        topic.first_log_time = 1_780_000_000_000_000_000 + index
        topic.last_log_time = topic.first_log_time + 200_000_000
    return make_summary(args=args, status="pass", topics=observed, limitations=[])



__all__ = [name for name in globals() if not name.startswith("__")]
