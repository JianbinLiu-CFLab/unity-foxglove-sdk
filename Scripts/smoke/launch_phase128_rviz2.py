#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Module: Scripts/smoke
# Purpose: Launch Phase128 RViz2 with the Windows Jazzy runtime environment.

"""Launch RViz2 for the Phase128 TF/LaserScan manual acceptance.

This launcher avoids project-owned PowerShell scripts while preserving the
Windows Jazzy DLL search path needed by RViz2. Keep Unity in Play Mode with
Phase128Rviz2TfLaserScanSmoke publishing before launching RViz2.
"""

from __future__ import annotations

import argparse
import pathlib
import sys

import _ros2_windows_env as ros2env

DEFAULT_ROS2_ROOT = ros2env.DEFAULT_ROS2_ROOT
DEFAULT_RVIZ_CONFIG = pathlib.Path(
    r"Packages\dev.unity2foxglove.ros2forunity\Samples~"
    r"\RViz2 Standard Visualization Acceptance\rviz2_phase128_tf_laserscan.rviz"
)


def parse_args(argv: list[str]) -> argparse.Namespace:
    """Parse command-line options for the Phase128 RViz2 launcher."""

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--ros2-root",
        default=str(DEFAULT_ROS2_ROOT),
        help="Windows ROS2 Jazzy root. Default: C:\\ros2_jazzy\\ros2-windows",
    )
    parser.add_argument(
        "--rviz-config",
        default=str(DEFAULT_RVIZ_CONFIG),
        help="RViz2 config path. Relative paths resolve from the current workspace root.",
    )
    parser.add_argument(
        "--discovery-range",
        choices=("LOCALHOST", "SUBNET", "OFF", "SYSTEM_DEFAULT"),
        default=None,
        help="Optional ROS_AUTOMATIC_DISCOVERY_RANGE override. Omit for the known-good unset Phase128 path.",
    )
    parser.add_argument(
        "--detached",
        action="store_true",
        help="Launch RViz2 and return immediately instead of waiting for RViz2 to close.",
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Print the resolved command and environment summary without launching RViz2.",
    )
    parser.add_argument(
        "--no-software-rendering",
        action="store_true",
        help="Compatibility no-op. Shared launcher always uses the known-good software rendering env.",
    )
    parser.add_argument(
        "--rmw",
        default=None,
        help="Override RMW_IMPLEMENTATION. Omit to preserve the shell value or shared default.",
    )
    parser.add_argument(
        "--domain-id",
        default=None,
        help="Override ROS_DOMAIN_ID. Omit to preserve the shared default.",
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


def print_summary(
    ros2_root: pathlib.Path,
    rviz_exe: pathlib.Path,
    rviz_config: pathlib.Path,
    env: dict[str, str],
) -> None:
    """Print the resolved launch inputs for acceptance evidence."""

    print(f"[phase128] ROS2 root: {ros2_root}")
    print(f"[phase128] RViz2 exe: {rviz_exe}")
    print(f"[phase128] RViz2 config: {rviz_config}")
    print(f"[phase128] ROS_DISTRO: {env.get('ROS_DISTRO')}")
    print(f"[phase128] RMW_IMPLEMENTATION: {env.get('RMW_IMPLEMENTATION')}")
    print(f"[phase128] ROS_DOMAIN_ID: {env.get('ROS_DOMAIN_ID')}")
    print(f"[phase128] ROS_AUTOMATIC_DISCOVERY_RANGE: {env.get('ROS_AUTOMATIC_DISCOVERY_RANGE', '<unset>')}")
    print(f'[phase128] Command: "{rviz_exe}" -d "{rviz_config}"')


def main(argv: list[str]) -> int:
    """Run the launcher and return a process-style exit code."""

    args = parse_args(argv)
    workspace_root = ros2env.find_workspace_root()
    ros2_root = ros2env.resolve_existing_path(args.ros2_root, "ROS2 root", workspace_root)
    rviz_config = ros2env.resolve_existing_path(args.rviz_config, "RViz2 config", workspace_root)
    rviz_exe = ros2_root / "bin" / "rviz2.exe"
    if not rviz_exe.exists():
        raise FileNotFoundError(f"rviz2.exe not found: {rviz_exe}")

    ros2env.validate_ros2_root(ros2_root)
    env = ros2env.build_ros_env(ros2_root, args.rmw, args.discovery_range, args.domain_id)
    print_summary(ros2_root, rviz_exe, rviz_config, env)
    if args.no_software_rendering:
        print("[phase128] --no-software-rendering is ignored by the shared launcher.")

    if args.dry_run:
        print("[phase128] Dry run only; RViz2 was not launched.")
        return 0

    process = ros2env.launch_rviz(
        ros2_root,
        rviz_config,
        env,
        "phase128",
        startup_check_seconds=args.rviz_startup_check_seconds,
        window_wait_seconds=args.rviz_window_wait_seconds,
    )
    if args.detached:
        return 0

    print("[phase128] Waiting for RViz2 to exit. Close RViz2 to return to the terminal.")
    return process.wait()


if __name__ == "__main__":
    try:
        raise SystemExit(main(sys.argv[1:]))
    except Exception as exc:
        print(f"[phase128] FAIL: {exc}", file=sys.stderr)
        raise SystemExit(1)
