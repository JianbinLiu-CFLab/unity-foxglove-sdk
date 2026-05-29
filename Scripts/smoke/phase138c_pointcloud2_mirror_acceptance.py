#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Module: Scripts/smoke
# Purpose: Manual ROS2 and RViz2 acceptance helper for Phase138C Virtual LiDAR PointCloud2 mirror.

"""Validate Phase138C Virtual LiDAR ROS2 PointCloud2 mirror topic.

Start Unity manually first, import the Virtual LiDAR PointCloud2 Digital Twin
sample, add Phase138VirtualLidarPointCloud2Smoke to a GameObject with a
VirtualLidar, and enter Play Mode. This helper then uses the pinned Windows
ROS2 Jazzy Python entry point to check the external ROS2 graph and launch
RViz2 by default.
"""

from __future__ import annotations

import argparse
import pathlib
import re
import sys

import _ros2_windows_env as ros2env


DEFAULT_ROS2_ROOT = ros2env.DEFAULT_ROS2_ROOT
DEFAULT_RVIZ_CONFIG = pathlib.Path(
    r"Packages\dev.unity2foxglove.ros2forunity\Samples~"
    r"\Virtual LiDAR PointCloud2 Digital Twin\rviz2_phase138c_pointcloud2.rviz"
)
NODE_NAME = "phase138_virtual_lidar"
POINTS_TOPIC = "/points"
POINTS_MSG_TYPE = "sensor_msgs/msg/PointCloud2"
EXPECTED_FRAME_ID = "os_lidar"


def parse_args(argv: list[str]) -> argparse.Namespace:
    """Parse command-line arguments."""

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--ros2-root",
        default=str(DEFAULT_ROS2_ROOT),
        help="Windows ROS2 Jazzy root. Default: C:\\ros2_jazzy\\ros2-windows",
    )
    parser.add_argument(
        "--rviz-config",
        default=str(DEFAULT_RVIZ_CONFIG),
        help="RViz2 config path to launch or print in evidence.",
    )
    parser.add_argument(
        "--wait-seconds",
        type=float,
        default=90.0,
        help="How long to wait for Unity's Phase138C node and publisher.",
    )
    parser.add_argument(
        "--echo-spin-seconds",
        type=float,
        default=20.0,
        help="ROS2 spin time for the bounded echo.",
    )
    parser.add_argument(
        "--rmw",
        default=None,
        help="RMW implementation to use. Omit to preserve RMW_IMPLEMENTATION or default to rmw_fastrtps_cpp.",
    )
    parser.add_argument(
        "--discovery-range",
        choices=("LOCALHOST", "SUBNET", "OFF", "SYSTEM_DEFAULT"),
        default=None,
        help=(
            "Override ROS_AUTOMATIC_DISCOVERY_RANGE. Omit to preserve the shell value "
            "or the ROS2 default. Use LOCALHOST for same-machine Unity acceptance."
        ),
    )
    launch_group = parser.add_mutually_exclusive_group()
    launch_group.add_argument(
        "--launch-rviz",
        dest="launch_rviz",
        action="store_true",
        help="Launch RViz2 with --rviz-config after publisher checks. This is the default.",
    )
    launch_group.add_argument(
        "--no-launch-rviz",
        dest="launch_rviz",
        action="store_false",
        help="Run ROS2 graph and echo checks without launching RViz2.",
    )
    parser.set_defaults(launch_rviz=True)
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


def validate_pointcloud2_echo(output: str) -> None:
    """Validate PointCloud2 echo contains the expected frame_id, fields, and non-empty payload."""

    required = [
        f"frame_id: {EXPECTED_FRAME_ID}",
        "height: 1",
        "fields:",
        "data:",
    ]
    missing = [token for token in required if token not in output]
    if missing:
        raise RuntimeError(f"PointCloud2 echo missing required token(s): {', '.join(missing)}\n{output}")

    width_match = re.search(r"width:\s*([1-9][0-9]*)", output)
    if not width_match:
        raise RuntimeError(f"PointCloud2 echo did not contain a positive width.\n{output}")
    width = int(width_match.group(1))

    point_step_match = re.search(r"point_step:\s*([1-9][0-9]*)", output)
    row_step_match = re.search(r"row_step:\s*([1-9][0-9]*)", output)
    if not point_step_match or not row_step_match:
        raise RuntimeError(f"PointCloud2 echo missing point_step or row_step.\n{output}")
    point_step = int(point_step_match.group(1))
    row_step = int(row_step_match.group(1))

    if row_step != point_step * width:
        raise RuntimeError(
            f"PointCloud2 echo row_step ({row_step}) != point_step ({point_step}) * width ({width}).\n{output}"
        )

    if "name: x" not in output or "name: y" not in output or "name: z" not in output:
        raise RuntimeError(f"PointCloud2 echo did not contain x/y/z fields.\n{output}")


def launch_rviz_before_echo(
    should_launch: bool,
    ros2_root: pathlib.Path,
    rviz_config: pathlib.Path,
    env: dict[str, str],
    startup_check_seconds: float,
    window_wait_seconds: float,
) -> None:
    """Launch RViz2 as soon as publisher endpoints are visible."""

    if should_launch:
        ros2env.launch_rviz(
            ros2_root,
            rviz_config,
            env,
            "phase138c",
            startup_check_seconds=startup_check_seconds,
            window_wait_seconds=window_wait_seconds,
        )
    else:
        ros2env.log_event("phase138c", "RViz2 launch skipped because --no-launch-rviz was supplied.")


def main(argv: list[str]) -> int:
    """Script entry point."""

    args = parse_args(argv)
    workspace_root = ros2env.find_workspace_root()
    ros2_root = ros2env.resolve_existing_path(args.ros2_root, "ROS2 root", workspace_root)
    rviz_config = ros2env.resolve_existing_path(args.rviz_config, "RViz2 config", workspace_root)
    pixi_python, ros2_script = ros2env.validate_ros2_root(ros2_root)
    env = ros2env.build_ros_env(ros2_root, args.rmw, args.discovery_range)

    print(f"[phase138c] ROS2 root: {ros2_root}")
    print(f"[phase138c] pixi Python: {pixi_python}")
    print(f"[phase138c] ros2-script.py: {ros2_script}")
    print(f"[phase138c] ROS_DISTRO: {env.get('ROS_DISTRO')}")
    print(f"[phase138c] RMW_IMPLEMENTATION: {env.get('RMW_IMPLEMENTATION')}")
    print(f"[phase138c] ROS_DOMAIN_ID: {env.get('ROS_DOMAIN_ID')}")
    print(f"[phase138c] ROS_AUTOMATIC_DISCOVERY_RANGE: {env.get('ROS_AUTOMATIC_DISCOVERY_RANGE', '<unset>')}")
    print(f"[phase138c] RViz2 config: {rviz_config}")

    launch_rviz_before_echo(
        args.launch_rviz,
        ros2_root,
        rviz_config,
        env,
        args.rviz_startup_check_seconds,
        args.rviz_window_wait_seconds,
    )

    print("--- node list (diagnostic) ---")
    nodes = ros2env.probe_node_list(pixi_python, ros2_script, env)
    print(nodes.rstrip() or "<empty>")
    if NODE_NAME not in nodes:
        print(f"[phase138c] node list did not include {NODE_NAME}; continuing with publisher endpoint and echo checks.")

    print(f"--- topic info -v {POINTS_TOPIC} ---")
    points_info = ros2env.wait_for_publisher(
        pixi_python,
        ros2_script,
        env,
        POINTS_TOPIC,
        args.wait_seconds,
        expected_type=POINTS_MSG_TYPE,
        node_name=NODE_NAME,
    )
    print(points_info.rstrip())

    print(f"--- echo {POINTS_TOPIC} ---")
    points_echo = ros2env.echo_once(
        pixi_python, ros2_script, env, POINTS_TOPIC, POINTS_MSG_TYPE, args.echo_spin_seconds
    )
    print(points_echo.rstrip())
    validate_pointcloud2_echo(points_echo)

    print("[phase138c] GREEN: /points external ROS2 acceptance checks completed.")
    print("[phase138c] Confirm RViz2 displays PointCloud2 /points at Fixed Frame os_lidar before marking manual PASS.")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main(sys.argv[1:]))
    except KeyboardInterrupt:
        raise SystemExit(130)
    except Exception as exc:
        print(f"[phase138c] FAIL: {exc}", file=sys.stderr)
        raise SystemExit(1)
