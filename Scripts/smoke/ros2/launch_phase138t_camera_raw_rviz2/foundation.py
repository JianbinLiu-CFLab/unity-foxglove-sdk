#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Launch RViz2 to view the Phase138T raw camera Image DDS stream.

Start Unity Play Mode first with:

- FoxgloveManager: ROS2 Native (R2FU)
- FoxgloveCameraPublisher: Publish Raw Image DDS enabled
- Raw image topic: /unity/sensor/camera/image
- Optional PointCloud2 topic: /unity/point_cloud2
- TF anchors enabled, or use this helper's visual /tf map -> os_sensor fallback

Then run:

    python Scripts/smoke/ros2/launch_phase138t_camera_raw_rviz2.py

The helper writes a runtime RViz2 config under build/rviz2 and launches RViz2
using the pinned Windows ROS2 Jazzy environment. It shows raw Image, PointCloud2,
and TF together; no image republisher is needed.
"""

from __future__ import annotations

import argparse
import pathlib
import re
import subprocess
import sys
import time

import _ros2_windows_env as ros2env

from pathlib import Path as _Phase192Path
_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "launch_phase138t_camera_raw_rviz2.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _Phase192Path, _PHASE192_FACADE_FILE

DEFAULT_TOPIC = "/unity/sensor/camera/image"
DEFAULT_POINTS_TOPIC = "/unity/point_cloud2"
DEFAULT_FIXED_FRAME = "map"
DEFAULT_CAMERA_PARENT_FRAME = "os_sensor"


def parse_args(argv: list[str]) -> argparse.Namespace:
    """Parse launch arguments for the raw camera Image RViz2 helper."""

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--ros2-root",
        default=str(ros2env.DEFAULT_ROS2_ROOT),
        help="Windows ROS2 Jazzy root. Default: ros2-windows\\ros2_jazzy",
    )
    parser.add_argument(
        "--topic",
        default=DEFAULT_TOPIC,
        help=f"Raw sensor_msgs/msg/Image topic to display. Default: {DEFAULT_TOPIC}",
    )
    parser.add_argument(
        "--fixed-frame",
        default=DEFAULT_FIXED_FRAME,
        help=f"RViz2 fixed frame. Default: {DEFAULT_FIXED_FRAME}",
    )
    parser.add_argument(
        "--points-topic",
        default=DEFAULT_POINTS_TOPIC,
        help=f"PointCloud2 topic to display. Default: {DEFAULT_POINTS_TOPIC}",
    )
    parser.add_argument(
        "--camera-parent-frame",
        default=DEFAULT_CAMERA_PARENT_FRAME,
        help=(
            "Camera TF parent frame used by the visual static TF fallback. "
            f"Default: {DEFAULT_CAMERA_PARENT_FRAME}"
        ),
    )
    parser.add_argument(
        "--no-pointcloud",
        action="store_true",
        help="Do not add the PointCloud2 display.",
    )
    parser.add_argument(
        "--no-tf",
        action="store_true",
        help="Do not add the TF display.",
    )
    parser.add_argument(
        "--no-camera-static-tf",
        "--no-camera-tf-fallback",
        dest="no_camera_static_tf",
        action="store_true",
        help="Do not launch the visual /tf fallback from fixed frame to camera parent frame.",
    )
    parser.add_argument(
        "--rmw",
        default=None,
        help="RMW implementation override. Omit to preserve RMW_IMPLEMENTATION or default to rmw_fastrtps_cpp.",
    )
    parser.add_argument(
        "--domain-id",
        default=None,
        help="ROS_DOMAIN_ID override. Omit for the helper default.",
    )
    parser.add_argument(
        "--discovery-range",
        choices=("LOCALHOST", "SUBNET", "OFF", "SYSTEM_DEFAULT"),
        default="SUBNET",
        help="Override ROS_AUTOMATIC_DISCOVERY_RANGE. Default: SUBNET.",
    )
    parser.add_argument(
        "--skip-topic-probe",
        action="store_true",
        help="Launch RViz2 without first running diagnostic ros2 topic probes.",
    )
    parser.add_argument(
        "--strict-topic-probe",
        action="store_true",
        help="Fail before launching RViz2 if the optional topic probe does not see a publisher.",
    )
    parser.add_argument(
        "--topic-probe-timeout",
        type=float,
        default=5.0,
        help="Seconds to wait for each optional topic probe. Default: 5.0.",
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Write the RViz2 config and print diagnostics without launching RViz2.",
    )
    parser.add_argument(
        "--rviz-startup-check-seconds",
        type=float,
        default=1.5,
        help="Seconds to wait for an immediate RViz2 process exit after launch.",
    )
    parser.add_argument(
        "--rviz-window-wait-seconds",
        type=float,
        default=20.0,
        help="Seconds to wait for a visible RViz2 window after launch.",
    )
    parser.add_argument(
        "--no-cleanup-stale",
        action="store_true",
        help="Do not stop stale Phase138T RViz/helper processes from previous script runs.",
    )
    parser.add_argument("--camera-tf-fallback-child", action="store_true", help=argparse.SUPPRESS)
    return parser.parse_args(argv)


def normalize_topic(topic: str) -> str:
    """Normalize a ROS topic into a leading-slash absolute topic name."""

    value = (topic or "").strip()
    if not value:
        raise ValueError("raw image topic must not be empty.")
    return value if value.startswith("/") else "/" + value


def normalize_frame(frame: str) -> str:
    """Normalize and validate the RViz2 fixed frame."""

    value = (frame or "").strip().strip("/")
    if not value:
        raise ValueError("fixed frame must not be empty.")
    return value


def sanitize_config_suffix(value: str) -> str:
    """Create a filesystem-safe suffix from a ROS topic or frame value."""

    suffix = re.sub(r"[^A-Za-z0-9_.-]+", "_", value.strip("/"))
    return suffix or "camera_raw_image"

__all__ = [name for name in globals() if not name.startswith("__")]
