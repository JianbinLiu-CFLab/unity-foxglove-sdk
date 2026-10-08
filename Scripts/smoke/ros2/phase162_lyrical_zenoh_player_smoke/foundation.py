#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Module: Scripts/smoke
# Purpose: Phase162 Lyrical Zenoh RViz2 acceptance helper.

"""Run Lyrical Zenoh RViz2 PointCloud2 acceptance against Unity/R2FU.

Start Unity Play Mode first with ROS2 Native (R2FU), Lyrical Win64, Zenoh
communication mode, PointCloud2 Native output, and deskew enabled. Bare runs
launch RViz2 and validate the raw + deskewed PointCloud2 topics. Pass
--echo-only for the older one-frame CLI smoke.
"""

from __future__ import annotations

import argparse
import json
import os
import pathlib
import subprocess
import sys
import time
from datetime import datetime

from _ros2_windows_env import build_ros_env, default_ros2_root, validate_ros2_root
import phase138u_lidar_deskew_rviz2_acceptance as phase138u


DEFAULT_RMW = "rmw_zenoh_cpp"
DEFAULT_TOPIC = "/unity/point_cloud2"
DEFAULT_DESKEWED_TOPIC = "/unity/point_cloud2_deskewed"
DEFAULT_MESSAGE_TYPE = "sensor_msgs/msg/PointCloud2"
DEFAULT_FIXED_FRAME = "map"

from pathlib import Path as _Phase192Path
_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "phase162_lyrical_zenoh_player_smoke.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _Phase192Path, _PHASE192_FACADE_FILE
DEFAULT_EXPECTED_FRAME_ID = "os_lidar"
DEFAULT_RVIZ_HOLD_TIMEOUT_SECONDS = 1800.0
DEFAULT_ROUTER_READY_MARKER = "Started"


def workspace_root() -> pathlib.Path:
    """Return the repository root."""

    return phase138u.ros2env.find_workspace_root()


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    """Parse command-line arguments."""

    root = workspace_root()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--ros2-root", type=pathlib.Path, default=default_ros2_root("lyrical", root))
    parser.add_argument("--rmw-implementation", default=DEFAULT_RMW)
    parser.add_argument("--domain-id", default="0")
    parser.add_argument("--topic", default=DEFAULT_TOPIC)
    parser.add_argument("--deskewed-topic", default=DEFAULT_DESKEWED_TOPIC)
    parser.add_argument("--message-type", default=DEFAULT_MESSAGE_TYPE)
    parser.add_argument("--expected-frame-id", default=DEFAULT_EXPECTED_FRAME_ID)
    parser.add_argument("--fixed-frame", default=DEFAULT_FIXED_FRAME)
    parser.add_argument("--expected-text", default="")
    parser.add_argument("--timeout-seconds", type=float, default=60.0)
    parser.add_argument(
        "--rviz-hold-timeout-seconds",
        type=float,
        default=DEFAULT_RVIZ_HOLD_TIMEOUT_SECONDS,
        help="Maximum seconds to retain an owned Zenoh router for an open RViz2 window.",
    )
    parser.add_argument("--spin-seconds", type=float, default=12.0)
    parser.add_argument(
        "--discovery-range",
        choices=("LOCALHOST", "SUBNET", "OFF", "SYSTEM_DEFAULT"),
        default="SUBNET",
    )
    parser.add_argument("--zenoh-router", type=pathlib.Path, default=None)
    parser.add_argument(
        "--no-zenoh-router",
        action="store_true",
        help="Do not auto-start rmw_zenohd when using rmw_zenoh_cpp.",
    )
    parser.add_argument(
        "--release-router-on-exit",
        action="store_true",
        help="Stop an auto-started Zenoh router when this helper exits even if the helper-launched RViz2 process is still open.",
    )
    parser.add_argument("--router-ready-marker", default=DEFAULT_ROUTER_READY_MARKER)
    parser.add_argument("--unity-player", type=pathlib.Path, default=None)
    parser.add_argument("--player-log", type=pathlib.Path, default=None)
    parser.add_argument("--player-ready-marker", default="")
    parser.add_argument("--scripting-backend", choices=("mono", "il2cpp"), default="mono")
    parser.add_argument(
        "--echo-only",
        action="store_true",
        help="Run only the legacy one-frame ros2 topic echo smoke without launching RViz2.",
    )
    parser.add_argument("--no-rviz", dest="launch_rviz", action="store_false")
    parser.add_argument("--skip-topic-probe", action="store_true")
    parser.add_argument("--rviz-display-mode", choices=("both", "raw"), default="both")
    parser.add_argument(
        "--require-motion",
        action="store_true",
        help="Require raw-vs-deskewed motion evidence instead of accepting static DDS wiring.",
    )
    parser.add_argument(
        "--echo-output",
        type=pathlib.Path,
        default=root / "build" / "phase162-lyrical-zenoh-smoke" / "ros2-topic-echo.log",
    )
    parser.add_argument(
        "--summary-output",
        type=pathlib.Path,
        default=root / "build" / "phase162-lyrical-zenoh-smoke" / "summary.json",
    )
    parser.set_defaults(launch_rviz=True)
    parsed = parser.parse_args(argv)
    if parsed.echo_only and not parsed.expected_text.strip():
        parser.error("--echo-only requires a non-empty --expected-text content oracle")
    return parsed


__all__ = [name for name in globals() if not name.startswith("__")]
