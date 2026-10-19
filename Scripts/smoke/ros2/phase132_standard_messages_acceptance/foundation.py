#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Module: Scripts/smoke
# Purpose: ROS2 CLI acceptance helper for the Phase132 standard message sample.

"""Validate the ROS2 standard message expansion sample.

Start Unity manually first. Import the ROS2 Standard Message Expansion sample,
add Phase132StandardMessagesSmoke to a scene object, then enter Play Mode. This
helper uses the pinned Windows ROS2 Jazzy Python entry point to check the six
standard ROS2 topics and representative echo payloads, then launches RViz2 by
default for a lightweight visual sanity check.
"""

from __future__ import annotations

import argparse
import json
import pathlib
import re
import subprocess
import sys
import time

import _ros2_windows_env as ros2env


DEFAULT_RVIZ_CONFIG = pathlib.Path(
    r"Packages\dev.unity2foxglove.ros2forunity\Samples~"
    r"\ROS2 Standard Message Expansion\rviz2_phase132_standard_messages.rviz"
)
NODE_NAME = "unity2foxglove_phase132_standard_messages"
TOPICS = [
    ("/camera/camera_info", "sensor_msgs/msg/CameraInfo"),
    ("/camera/image_raw", "sensor_msgs/msg/Image"),
    ("/imu/data", "sensor_msgs/msg/Imu"),
    ("/odom", "nav_msgs/msg/Odometry"),
    ("/pose", "geometry_msgs/msg/PoseStamped"),
    ("/fix", "sensor_msgs/msg/NavSatFix"),
]
PHASE132_RCLPY_PROBE_PATH = pathlib.Path(__file__).resolve().parents[1] / "_phase132_rclpy_probe.py"
YAML_NUMBER_RE = r"-?(?:(?:[0-9]+(?:\.[0-9]*)?)|(?:\.[0-9]+))(?:[eE][+-]?[0-9]+)?"



from pathlib import Path as _Phase192Path
_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "phase132_standard_messages_acceptance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _Phase192Path, _PHASE192_FACADE_FILE


__all__ = [name for name in globals() if not name.startswith("__")]
