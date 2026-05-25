#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Module: Scripts/smoke
# Purpose: Launch Phase132 RViz2 with the Windows Jazzy runtime environment.

"""Launch RViz2 for the standard message expansion manual acceptance.

This launcher intentionally skips one-shot ROS2 CLI graph and echo checks. Use
it when Unity is already in Play Mode and you want RViz2 to subscribe directly
to the live `/pose` and `/camera/image_raw` streams while the CLI helper remains
the authoritative six-topic acceptance gate.
"""

from __future__ import annotations

import argparse
import pathlib
import sys

import _ros2_windows_env as ros2env


DEFAULT_RVIZ_CONFIG = pathlib.Path(
    r"Packages\dev.unity2foxglove.ros2forunity\Samples~"
    r"\ROS2 Standard Message Expansion\rviz2_phase132_standard_messages.rviz"
)


def parse_args(argv: list[str]) -> argparse.Namespace:
    """Parse command-line options."""

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--ros2-root",
        default=str(ros2env.DEFAULT_ROS2_ROOT),
        help="Windows ROS2 Jazzy root. Default: C:\\ros2_jazzy\\ros2-windows",
    )
    parser.add_argument(
        "--rviz-config",
        default=str(DEFAULT_RVIZ_CONFIG),
        help="RViz2 config path. Relative paths resolve from the current workspace root.",
    )
    parser.add_argument(
        "--rmw",
        default=None,
        help="RMW implementation to use. Omit to preserve RMW_IMPLEMENTATION or default to rmw_fastrtps_cpp.",
    )
    parser.add_argument(
        "--domain-id",
        default=None,
        help="ROS_DOMAIN_ID to use for RViz2. Omit to use domain 0.",
    )
    parser.add_argument(
        "--discovery-range",
        choices=("LOCALHOST", "SUBNET", "OFF", "SYSTEM_DEFAULT"),
        default="LOCALHOST",
        help="ROS_AUTOMATIC_DISCOVERY_RANGE for same-machine Unity acceptance. Default: LOCALHOST.",
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Print the resolved inputs without launching RViz2.",
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
        default=45.0,
        help="Seconds to wait for a visible RViz2 window after launch.",
    )
    return parser.parse_args(argv)


def main(argv: list[str]) -> int:
    """Run the launcher."""

    args = parse_args(argv)
    workspace_root = ros2env.find_workspace_root()
    ros2_root = ros2env.resolve_existing_path(args.ros2_root, "ROS2 root", workspace_root)
    rviz_config = ros2env.resolve_existing_path(args.rviz_config, "RViz2 config", workspace_root)
    pixi_python, ros2_script = ros2env.validate_ros2_root(ros2_root)
    env = ros2env.build_ros_env(ros2_root, args.rmw, args.discovery_range, args.domain_id)

    print(f"[phase132-launch] ROS2 root: {ros2_root}")
    print(f"[phase132-launch] pixi Python: {pixi_python}")
    print(f"[phase132-launch] ros2-script.py: {ros2_script}")
    print(f"[phase132-launch] ROS_DISTRO: {env.get('ROS_DISTRO')}")
    print(f"[phase132-launch] RMW_IMPLEMENTATION: {env.get('RMW_IMPLEMENTATION')}")
    print(f"[phase132-launch] ROS_DOMAIN_ID: {env.get('ROS_DOMAIN_ID')}")
    print(f"[phase132-launch] ROS_AUTOMATIC_DISCOVERY_RANGE: {env.get('ROS_AUTOMATIC_DISCOVERY_RANGE', '<unset>')}")
    print(f"[phase132-launch] RViz2 config: {rviz_config}")
    print(f"[phase132-launch] RViz2 startup check seconds: {args.rviz_startup_check_seconds:.1f}")
    print(f"[phase132-launch] RViz2 window wait seconds: {args.rviz_window_wait_seconds:.1f}")

    if args.dry_run:
        print("[phase132-launch] Dry run only; RViz2 was not launched.")
        return 0

    ros2env.launch_rviz(
        ros2_root,
        rviz_config,
        env,
        "phase132-launch",
        startup_check_seconds=args.rviz_startup_check_seconds,
        window_wait_seconds=args.rviz_window_wait_seconds,
    )
    print("[phase132-launch] Confirm RViz2 displays PoseStamped /pose and optionally Image /camera/image_raw.")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main(sys.argv[1:]))
    except KeyboardInterrupt:
        raise SystemExit(130)
    except Exception as exc:
        print(f"[phase132-launch] FAIL: {exc}", file=sys.stderr)
        raise SystemExit(1)
