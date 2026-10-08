#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Module: Scripts/smoke
# Purpose: ROS2-side acceptance helper for Phase 138S IMU native DDS output.

"""Validate the IMU native DDS topic from a running Unity/R2FU scene."""

from __future__ import annotations

import argparse
import json
import os
import pathlib
import re
import shutil
import subprocess
import sys

import _ros2_windows_env as ros2env

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(line_buffering=True)
if hasattr(sys.stderr, "reconfigure"):
    sys.stderr.reconfigure(line_buffering=True)


DEFAULT_TOPIC = "/imu/data"
IMU_MSG_TYPE = "sensor_msgs/msg/Imu"
NUMBER_RE = r"-?(?:(?:[0-9]+(?:\.[0-9]*)?)|(?:\.[0-9]+))(?:[eE][+-]?[0-9]+)?"



from pathlib import Path as _Phase192Path
_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "phase138s_imu_native_dds_acceptance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _Phase192Path, _PHASE192_FACADE_FILE
def parse_args(argv: list[str]) -> argparse.Namespace:
    """Build and parse CLI arguments for IMU DDS acceptance."""


    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--ros2-root",
        default=None,
        help=(
            "Windows ROS2 Jazzy root. Default on Windows: ros2-windows\\ros2_jazzy. "
            "On WSL/Linux, omit this to use the current ROS2 Python environment."
        ),
    )
    parser.add_argument(
        "--topic",
        default=DEFAULT_TOPIC,
        help="IMU topic to validate. Default: /imu/data",
    )
    parser.add_argument(
        "--expected-frame-id",
        default=None,
        help="Optional expected header.frame_id, for example os_imu or imu_link.",
    )
    parser.add_argument(
        "--wait-seconds",
        type=float,
        default=30.0,
        help="How long to wait for a publisher endpoint. Default: 30",
    )
    parser.add_argument(
        "--echo-spin-seconds",
        type=float,
        default=12.0,
        help="ROS2 spin time for bounded echo. Default: 12",
    )
    parser.add_argument(
        "--rmw",
        default=None,
        help="RMW implementation. Omit to preserve existing value or default to rmw_fastrtps_cpp.",
    )
    parser.add_argument(
        "--domain-id",
        default=None,
        help="ROS_DOMAIN_ID to use. Omit to use domain 0.",
    )
    parser.add_argument(
        "--discovery-range",
        choices=("LOCALHOST", "SUBNET", "OFF", "SYSTEM_DEFAULT"),
        default="SUBNET",
        help="ROS_AUTOMATIC_DISCOVERY_RANGE. Default: SUBNET to match Unity ROS2 For Unity discovery on Windows.",
    )
    parser.add_argument(
        "--node-name",
        default=None,
        help="Optional publisher node-name hint used during verbose topic-info validation.",
    )
    parser.add_argument(
        "--require-graph-info",
        action="store_true",
        help="Require topic-info publisher discovery before echo. By default graph info is diagnostic.",
    )
    parser.add_argument(
        "--require-nonzero-vector",
        action="store_true",
        help="Require angular_velocity or linear_acceleration to contain a non-zero component.",
    )
    parser.add_argument(
        "--print-json",
        action="store_true",
        help="Print structured JSON evidence in addition to text logs.",
    )
    parser.add_argument(
        "--no-print-json",
        dest="print_json",
        action="store_false",
        help="Disable structured JSON evidence output.",
    )
    parser.set_defaults(print_json=True)
    return parser.parse_args(argv)


__all__ = [name for name in globals() if not name.startswith("__")]
