"""Phase139 command-line contract and topic expectation assembly."""

from __future__ import annotations

import argparse

from .protocol_surface import *


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
    parser.add_argument("--insecure", action="store_true", help="Skip TLS certificate validation for local WSS smoke tests.")
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



__all__ = [name for name in globals() if not name.startswith("__")]
