#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Launch the Phase160 Humble PointCloud2 + TF RViz2 acceptance view.

Start Unity Play Mode with the Humble Win64 R2FU runtime selected, ROS2 Native
enabled, and PointCloud2 Native publishing active. This helper uses the
repo-local ``ros2-windows/ros2_humble`` entrypoint for external probes only; do
not source that environment inside Unity.

The default operator path mirrors the Phase138U / Phase146B acceptance shape:
launch RViz2 immediately with the raw and deskewed PointCloud2 displays, then
manually confirm that the visible point cloud follows the moving vehicle. Pass
``--probe`` only when direct rclpy PointCloud2/TF evidence is also needed.
"""

from __future__ import annotations

import argparse
import json
import math
import pathlib
import re
import subprocess
import sys
import textwrap
from typing import Any

import _ros2_windows_env as ros2env

from pathlib import Path as _Phase192Path
_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "phase160_humble_lidar_deskew_acceptance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _Phase192Path, _PHASE192_FACADE_FILE

DEFAULT_RAW_TOPIC = "/unity/point_cloud2"
DEFAULT_DESKEWED_TOPIC = "/unity/point_cloud2_deskewed"
DEFAULT_TF_TOPIC = "/tf"
DEFAULT_FIXED_FRAME = "map"
DEFAULT_BASE_FRAME = "base_link"
DEFAULT_SENSOR_FRAME = "os_sensor"
DEFAULT_POINT_FRAME = "os_lidar"
RESULT_MARKER = "PHASE160_RESULT_JSON:"


class AcceptanceError(RuntimeError):
    """Raised when Phase160 Humble evidence does not satisfy the contract."""


def parse_args(argv: list[str]) -> argparse.Namespace:
    """Parse command-line arguments."""

    workspace_root = ros2env.find_workspace_root()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--ros2-root", default=str(ros2env.default_ros2_root("humble", workspace_root)))
    parser.add_argument("--raw-topic", default=DEFAULT_RAW_TOPIC)
    parser.add_argument("--deskewed-topic", default=DEFAULT_DESKEWED_TOPIC)
    parser.add_argument("--tf-topic", default=DEFAULT_TF_TOPIC)
    parser.add_argument("--fixed-frame", default=DEFAULT_FIXED_FRAME)
    parser.add_argument("--base-frame", default=DEFAULT_BASE_FRAME)
    parser.add_argument("--sensor-frame", default=DEFAULT_SENSOR_FRAME)
    parser.add_argument("--point-frame", default=DEFAULT_POINT_FRAME)
    parser.add_argument("--spin-seconds", type=float, default=20.0)
    parser.add_argument("--motion-threshold-m", type=float, default=0.02)
    parser.add_argument("--require-motion", action="store_true")
    parser.add_argument(
        "--probe",
        action="store_true",
        help="After launching RViz2, run direct rclpy PointCloud2/TF checks.",
    )
    parser.add_argument(
        "--required-clouds",
        choices=("raw", "deskewed", "both", "any"),
        default="raw",
        help=(
            "PointCloud2 streams required for pass. Phase160 defaults to raw runtime/DDS "
            "wiring; use 'both' only when motion-compensated deskew output is enabled."
        ),
    )
    parser.add_argument("--no-rviz", action="store_true")
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--self-test", action="store_true")
    parser.add_argument("--rmw", default="rmw_fastrtps_cpp")
    parser.add_argument("--domain-id", default=None)
    parser.add_argument(
        "--discovery-range",
        choices=("LOCALHOST", "SUBNET", "OFF", "SYSTEM_DEFAULT"),
        default="SUBNET",
    )
    parser.add_argument("--rviz-display-mode", choices=("both", "raw"), default="both")
    parser.add_argument("--rviz-startup-check-seconds", type=float, default=1.5)
    parser.add_argument("--rviz-window-wait-seconds", type=float, default=0.0)
    return parser.parse_args(argv)


def normalize_topic(topic: str) -> str:
    """Normalize a ROS topic."""

    value = (topic or "").strip()
    if not value:
        raise ValueError("topic must not be empty")
    return value if value.startswith("/") else "/" + value


def normalize_frame(frame: str) -> str:
    """Normalize a TF frame id."""

    value = (frame or "").strip().strip("/")
    if not value:
        raise ValueError("frame id must not be empty")
    return value


def suffix(value: str) -> str:
    """Return a filesystem-safe suffix."""

    return re.sub(r"[^A-Za-z0-9_.-]+", "_", value.strip("/")) or "phase160"


def pointcloud_display(name: str, topic: str, color: str, size: int) -> str:
    """Return one RViz2 PointCloud2 display YAML fragment."""

    # Match the R2FU sensor-data PointCloud2 publisher QoS. A Reliable RViz2
    # subscriber is incompatible with a Best Effort DDS publisher.
    return f"""    - Class: rviz_default_plugins/PointCloud2
      Enabled: true
      Name: {name} {topic}
      Topic:
        Depth: 1
        Durability Policy: Volatile
        History Policy: Keep Last
        Reliability Policy: Best Effort
        Value: {topic}
      Style: Points
      Size (Pixels): {size}
      Color: {color}
      Color Transformer: FlatColor
      Queue Size: 10
"""



__all__ = [name for name in globals() if not name.startswith("__")]
