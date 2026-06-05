#!/usr/bin/env python3
"""Phase139 end-to-end smoke helper for Unity2Foxglove.

This script is intentionally a helper for local acceptance, not a replacement
for the operator's Unity/RViz/Foxglove judgment. The default path checks the
Maze demo's WebSocket surface and emits a JSON evidence summary that can be
attached to a Developer report. Optional ROS2 Native evidence is kept as a
separate profile because DDS/RViz acceptance depends on Unity Play Mode and the
local Windows ROS2 environment.

The deskewed PointCloud2 topic is visualization output and not a SLAM input.
FAST-LIO2/LIVO2 style consumers should use the raw rolling PointCloud2 topic
with per-point timing and IMU.

The ROS2 Native profile deliberately references the project Windows helper
module _ros2_windows_env and direct rclpy subscribers, but it does not import
ROS2 packages during --self-test so a clean checkout can validate the harness
shape without a ROS2 installation.
"""

from __future__ import annotations

import argparse
import asyncio
import json
import os
import pathlib
import ssl
import struct
import sys
import time
from dataclasses import dataclass
from typing import Any


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
    first_wall_time: float | None = None
    last_wall_time: float | None = None
    first_log_time: int | None = None
    last_log_time: int | None = None
    note: str = ""

    def to_json(self, duration_seconds: float) -> dict[str, Any]:
        """Return stable JSON evidence for this topic."""

        approx_hz = 0.0
        if self.first_wall_time is not None and self.last_wall_time is not None:
            elapsed = max(self.last_wall_time - self.first_wall_time, 0.0)
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


def build_parser() -> argparse.ArgumentParser:
    """Create the command-line parser."""

    parser = argparse.ArgumentParser(
        description="Phase139 end-to-end smoke helper for Unity2Foxglove.",
        formatter_class=argparse.ArgumentDefaultsHelpFormatter,
    )
    parser.add_argument(
        "--mode",
        choices=("websocket-core", "ros2-native", "both"),
        default="websocket-core",
        help="Smoke profile to run.",
    )
    parser.add_argument(
        "--scenario",
        choices=(DEFAULT_WEBSOCKET_SCENARIO, DEFAULT_ROS2_SCENARIO, "custom"),
        default=DEFAULT_WEBSOCKET_SCENARIO,
        help="Human-readable scene/profile label stored in JSON evidence.",
    )
    parser.add_argument("--url", default=DEFAULT_WEBSOCKET_URL, help="Foxglove WebSocket URL.")
    parser.add_argument("--duration", type=float, default=8.0, help="Message collection window.")
    parser.add_argument("--advertise-timeout", type=float, default=8.0, help="Seconds to wait for channel advertisements.")
    parser.add_argument("--idle-timeout", type=float, default=2.0, help="Seconds to wait between WebSocket messages.")
    parser.add_argument("--tf-topic", default=DEFAULT_TF_TOPIC, help="TF topic.")
    parser.add_argument("--imu-topic", default=DEFAULT_IMU_TOPIC, help="IMU topic.")
    parser.add_argument("--pointcloud-topic", default=DEFAULT_POINTCLOUD_TOPIC, help="Raw PointCloud2 topic.")
    parser.add_argument(
        "--deskewed-topic",
        default=DEFAULT_DESKEWED_POINTCLOUD_TOPIC,
        help="Visualization-only deskewed PointCloud2 topic.",
    )
    parser.add_argument("--raw-image-topic", default=DEFAULT_RAW_IMAGE_TOPIC, help="ROS2 raw Image topic.")
    parser.add_argument(
        "--compressed-image-topic",
        default=DEFAULT_COMPRESSED_IMAGE_TOPIC,
        help="Compressed camera image topic.",
    )
    parser.add_argument("--camera-info-topic", default=DEFAULT_CAMERA_INFO_TOPIC, help="CameraInfo topic.")
    parser.add_argument(
        "--required-topic",
        action="append",
        default=[],
        help="Additional required topic. May be supplied multiple times.",
    )
    parser.add_argument(
        "--optional-topic",
        action="append",
        default=[],
        help="Additional optional topic. May be supplied multiple times.",
    )
    parser.add_argument("--mcap", default="", help="Optional MCAP recording path to include in the evidence summary.")
    parser.add_argument("--json-out", default="", help="Optional path for JSON evidence output.")
    parser.add_argument("--self-test", action="store_true", help="Run deterministic harness self-test.")
    parser.add_argument(
        "--allow-ros2-skipped",
        action="store_true",
        help="Return success when ROS2 Native mode is requested but delegated to manual acceptance.",
    )
    return parser


def default_expectations(args: argparse.Namespace) -> list[TopicExpectation]:
    """Build the topic contract for the selected smoke profile."""

    expectations: list[TopicExpectation] = []
    if args.mode in ("websocket-core", "both"):
        expectations.extend(
            [
                TopicExpectation(args.tf_topic, "required", "tf", note="Frame tree should be visible."),
                TopicExpectation(args.imu_topic, "optional", "imu", note="Present when Virtual IMU is enabled."),
                TopicExpectation(
                    args.compressed_image_topic,
                    "optional",
                    "camera",
                    note="Present when camera JPEG output is enabled.",
                ),
                TopicExpectation(
                    args.pointcloud_topic,
                    "optional",
                    "pointcloud",
                    note="Present when PointCloud2 Native/WebSocket point cloud output is enabled.",
                ),
            ]
        )

    if args.mode in ("ros2-native", "both"):
        expectations.extend(
            [
                TopicExpectation(args.imu_topic, "required", "ros2-imu", note="Native DDS IMU acceptance topic."),
                TopicExpectation(args.pointcloud_topic, "required", "ros2-pointcloud", note="Raw rolling PointCloud2."),
                TopicExpectation(
                    args.deskewed_topic,
                    "optional",
                    "ros2-pointcloud",
                    note="Visualization-only deskewed PointCloud2, not a SLAM input.",
                ),
                TopicExpectation(args.raw_image_topic, "optional", "ros2-camera", note="Raw Image DDS topic."),
                TopicExpectation(args.camera_info_topic, "optional", "ros2-camera-info", note="Camera calibration DDS topic."),
                TopicExpectation(args.tf_topic, "optional", "ros2-tf", note="TF tree evidence."),
            ]
        )

    for topic in args.required_topic:
        expectations.append(TopicExpectation(topic, "required", "custom"))
    for topic in args.optional_topic:
        expectations.append(TopicExpectation(topic, "optional", "custom"))

    return dedupe_expectations(expectations)


def dedupe_expectations(expectations: list[TopicExpectation]) -> list[TopicExpectation]:
    """Merge duplicate topic expectations while preserving required status."""

    by_topic: dict[str, TopicExpectation] = {}
    for item in expectations:
        existing = by_topic.get(item.topic)
        if existing is None:
            by_topic[item.topic] = item
            continue
        classification = "required" if "required" in (existing.classification, item.classification) else "optional"
        family = existing.family if existing.family == item.family else f"{existing.family},{item.family}"
        note = existing.note or item.note
        by_topic[item.topic] = TopicExpectation(item.topic, classification, family, min(existing.min_messages, item.min_messages), note)
    return list(by_topic.values())


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
        topic.first_wall_time = now + index * 0.01
        topic.last_wall_time = topic.first_wall_time + 0.2
        topic.first_log_time = 1_780_000_000_000_000_000 + index
        topic.last_log_time = topic.first_log_time + 200_000_000
    return make_summary(args=args, status="pass", topics=observed, limitations=[])


async def run_websocket_core(args: argparse.Namespace) -> tuple[str, dict[str, ObservedTopic], list[str]]:
    """Collect WebSocket advertise and message evidence."""

    try:
        import websockets
    except ImportError as exc:
        raise RuntimeError("Python package 'websockets' is required for websocket-core mode.") from exc

    expectations = default_expectations(args)
    expected_by_topic = {item.topic: item for item in expectations}
    observed = {
        item.topic: ObservedTopic(item.topic, item.classification, item.family, note=item.note)
        for item in expectations
    }
    limitations: list[str] = []

    ssl_context = ssl.create_default_context() if args.url.startswith("wss://") else None
    async with websockets.connect(args.url, subprotocols=[FOXGLOVE_SUBPROTOCOL], ssl=ssl_context) as websocket:
        channels = await collect_advertisements(websocket, set(expected_by_topic), args.advertise_timeout, args.idle_timeout)
        for channel in channels.values():
            topic = channel.get("topic")
            if topic not in observed:
                continue
            observed_topic = observed[topic]
            observed_topic.channel_id = int(channel.get("id"))
            observed_topic.encoding = channel.get("encoding")
            observed_topic.schema_name = channel.get("schemaName")

        missing_required = [
            topic for topic, item in expected_by_topic.items()
            if item.classification == "required" and observed[topic].channel_id is None
        ]
        if missing_required:
            limitations.append("Missing required WebSocket channel advertisements: " + ", ".join(sorted(missing_required)))
            return "fail", observed, limitations

        subscription_to_topic: dict[int, str] = {}
        subscriptions: list[dict[str, int]] = []
        subscription_id = 1
        for topic, item in expected_by_topic.items():
            channel_id = observed[topic].channel_id
            if channel_id is None:
                limitations.append(f"Optional topic was not advertised: {topic}")
                continue
            subscriptions.append({"id": subscription_id, "channelId": channel_id})
            subscription_to_topic[subscription_id] = topic
            subscription_id += 1

        if subscriptions:
            await websocket.send(json.dumps({"op": "subscribe", "subscriptions": subscriptions}))
            await collect_messages(websocket, observed, subscription_to_topic, args.duration, args.idle_timeout)

    failing_topics = [
        topic for topic, item in expected_by_topic.items()
        if item.classification == "required" and observed[topic].messages < item.min_messages
    ]
    if failing_topics:
        limitations.append("Missing required WebSocket message samples: " + ", ".join(sorted(failing_topics)))
        return "fail", observed, limitations

    if any(item.classification == "optional" and observed[item.topic].messages == 0 for item in expectations):
        return "pass_with_limitations", observed, limitations
    return "pass", observed, limitations


async def collect_advertisements(
    websocket: Any,
    target_topics: set[str],
    advertise_timeout: float,
    idle_timeout: float,
) -> dict[int, dict[str, Any]]:
    """Collect channel advertisements until all targets are known or timeout expires."""

    channels: dict[int, dict[str, Any]] = {}
    deadline = time.monotonic() + advertise_timeout
    while time.monotonic() < deadline:
        try:
            frame = await asyncio.wait_for(websocket.recv(), timeout=min(idle_timeout, max(deadline - time.monotonic(), 0.1)))
        except asyncio.TimeoutError:
            continue
        if not isinstance(frame, str):
            continue
        try:
            message = json.loads(frame)
        except json.JSONDecodeError:
            continue
        if message.get("op") != "advertise":
            continue
        for channel in message.get("channels", []):
            channel_id = int(channel.get("id"))
            channels[channel_id] = channel
        advertised_topics = {channel.get("topic") for channel in channels.values()}
        if target_topics.issubset(advertised_topics):
            break
    return channels


async def collect_messages(
    websocket: Any,
    observed: dict[str, ObservedTopic],
    subscription_to_topic: dict[int, str],
    duration: float,
    idle_timeout: float,
) -> None:
    """Collect binary MessageData frames for the subscribed topics."""

    deadline = time.monotonic() + duration
    while time.monotonic() < deadline:
        try:
            frame = await asyncio.wait_for(websocket.recv(), timeout=min(idle_timeout, max(deadline - time.monotonic(), 0.1)))
        except asyncio.TimeoutError:
            continue
        if not isinstance(frame, (bytes, bytearray)):
            continue
        data = bytes(frame)
        if len(data) < 13 or data[0] != MESSAGE_DATA_OPCODE:
            continue
        subscription_id = struct.unpack_from("<I", data, 1)[0]
        topic = subscription_to_topic.get(subscription_id)
        if topic is None:
            continue
        log_time = struct.unpack_from("<Q", data, 5)[0]
        item = observed[topic]
        now = time.monotonic()
        item.messages += 1
        item.payload_bytes += max(len(data) - 13, 0)
        item.first_wall_time = now if item.first_wall_time is None else item.first_wall_time
        item.last_wall_time = now
        item.first_log_time = log_time if item.first_log_time is None else item.first_log_time
        item.last_log_time = log_time


def inspect_mcap(path_text: str) -> dict[str, Any]:
    """Return a small MCAP evidence summary when the operator supplies a file."""

    if not path_text:
        return {"checked": False, "status": "skipped"}
    path = pathlib.Path(path_text)
    if not path.exists():
        return {"checked": True, "status": "fail", "path": str(path), "error": "file does not exist"}
    size = path.stat().st_size
    if size == 0:
        return {"checked": True, "status": "fail", "path": str(path), "size_bytes": 0, "error": "empty file"}
    with path.open("rb") as handle:
        prefix = handle.read(8)
    looks_like_mcap = prefix.startswith(b"\x89MCAP")
    return {
        "checked": True,
        "status": "pass" if looks_like_mcap else "limited",
        "path": str(path),
        "size_bytes": size,
        "magic_prefix": prefix.hex(),
        "note": "Use the existing MCAP reader inspections for deep schema and attachment checks.",
    }


def ros2_native_summary(args: argparse.Namespace) -> dict[str, Any]:
    """Explain delegated ROS2 Native acceptance without launching ROS2 in self-test."""

    helper_commands = [
        f"python Scripts/smoke/phase138s_imu_native_dds_acceptance.py --topic {args.imu_topic} --expected-frame-id os_imu",
        f"python Scripts/smoke/phase138t_camera_raw_image_dds_acceptance.py --topic {args.raw_image_topic} --expected-frame-id os_camera --expected-width 640 --expected-height 480 --expected-encoding rgb8",
        f"python Scripts/smoke/phase138u_lidar_deskew_rviz2_acceptance.py --raw-topic {args.pointcloud_topic} --deskewed-topic {args.deskewed_topic}",
    ]
    return {
        "checked": False,
        "status": "delegated",
        "runtime_helpers": helper_commands,
        "implementation_notes": [
            "Use _ros2_windows_env for pinned Windows Jazzy paths.",
            "Use direct rclpy subscribers as the hard gate when ros2 topic list times out.",
        ],
    }


def write_json(path_text: str, summary: dict[str, Any]) -> None:
    """Write evidence JSON if requested."""

    if not path_text:
        return
    path = pathlib.Path(path_text)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(summary, indent=2, sort_keys=True) + "\n", encoding="utf-8")


def print_summary(summary: dict[str, Any]) -> None:
    """Print stable JSON to stdout."""

    print(json.dumps(summary, indent=2, sort_keys=True))


def determine_exit_code(summary: dict[str, Any], allow_ros2_skipped: bool) -> int:
    """Map the JSON status to a process exit code."""

    status = summary.get("status")
    if status in ("pass", "pass_with_limitations"):
        return 0
    if status == "limited" and allow_ros2_skipped:
        return 0
    return 1


def main(argv: list[str]) -> int:
    """Entry point."""

    parser = build_parser()
    args = parser.parse_args(argv)

    if args.self_test:
        summary = self_test_summary(args)
        write_json(args.json_out, summary)
        print_summary(summary)
        return 0

    mcap = inspect_mcap(args.mcap)
    ros2_native = ros2_native_summary(args)

    try:
        if args.mode == "ros2-native":
            summary = make_summary(
                args=args,
                status="limited",
                topics={},
                limitations=["ROS2 Native graph evidence is delegated to the Phase138S/T/U direct subscriber helpers."],
                mcap=mcap,
                ros2_native=ros2_native,
            )
        else:
            status, topics, limitations = asyncio.run(run_websocket_core(args))
            if args.mode == "both":
                limitations.append("ROS2 Native profile was summarized but not launched by this WebSocket pass.")
            summary = make_summary(
                args=args,
                status=status,
                topics=topics,
                limitations=limitations,
                mcap=mcap,
                ros2_native=ros2_native if args.mode == "both" else None,
            )
    except Exception as exc:
        summary = make_summary(
            args=args,
            status="fail",
            topics={},
            limitations=[str(exc)],
            mcap=mcap,
            ros2_native=ros2_native if args.mode in ("ros2-native", "both") else None,
        )

    write_json(args.json_out, summary)
    print_summary(summary)
    return determine_exit_code(summary, args.allow_ros2_skipped)


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
