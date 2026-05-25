#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Module: Scripts/smoke
# Purpose: Manual ROS2 and RViz2 acceptance helper for Phase129 TF/PointCloud2.

"""Validate Phase129 RViz2 PointCloud2 standard visualization topics.

Start Unity manually first, import the RViz2 PointCloud2 Acceptance sample, add
Phase129Rviz2PointCloud2Smoke to a scene object, and enter Play Mode. This
helper then uses the pinned Windows ROS2 Jazzy Python entry point to check the
external ROS2 graph and, optionally, launch RViz2.
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
    r"\RViz2 PointCloud2 Acceptance\rviz2_phase129_pointcloud2.rviz"
)
NODE_NAME = "unity2foxglove_phase129_pointcloud2"
TF_TOPIC = "/tf"
POINTS_TOPIC = "/points"
TF_MSG_TYPE = "tf2_msgs/msg/TFMessage"
POINTS_MSG_TYPE = "sensor_msgs/msg/PointCloud2"
EXPECTED_POINT_COUNT = 1000
EXPECTED_POINT_STEP = 16
EXPECTED_ROW_STEP = EXPECTED_POINT_COUNT * EXPECTED_POINT_STEP


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
        help="How long to wait for Unity's Phase129 node and publishers.",
    )
    parser.add_argument(
        "--echo-spin-seconds",
        type=float,
        default=20.0,
        help="ROS2 spin time for each bounded echo.",
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
            "or the ROS2 default."
        ),
    )
    parser.add_argument(
        "--launch-rviz",
        action="store_true",
        help="Launch RViz2 with --rviz-config after CLI checks pass.",
    )
    return parser.parse_args(argv)


def has_phase129_publisher(topic_info: str) -> bool:
    """Return whether verbose topic info contains a Phase129 publisher endpoint."""

    return ros2env.topic_info_has_publisher(topic_info, NODE_NAME)


def validate_tf_echo(output: str) -> None:
    """Validate TF echo contains the required frame tree."""

    missing = [token for token in ("map", "base_link", "point_cloud_sensor") if token not in output]
    if missing:
        raise RuntimeError(f"TF echo missing required frame token(s): {', '.join(missing)}\n{output}")


def validate_pointcloud2_echo(output: str) -> None:
    """Validate PointCloud2 echo contains point_cloud_sensor frame and non-empty payload metadata."""

    required = ["frame_id: point_cloud_sensor", "height: 1", "fields:", "data:"]
    missing = [token for token in required if token not in output]
    if missing:
        raise RuntimeError(f"PointCloud2 echo missing required token(s): {', '.join(missing)}\n{output}")

    width_match = re.search(r"width:\s*([1-9][0-9]*)", output)
    if not width_match:
        raise RuntimeError(f"PointCloud2 echo did not contain a positive width.\n{output}")
    if int(width_match.group(1)) != EXPECTED_POINT_COUNT:
        raise RuntimeError(f"PointCloud2 echo width was not {EXPECTED_POINT_COUNT}.\n{output}")

    for label, expected in (("point_step", EXPECTED_POINT_STEP), ("row_step", EXPECTED_ROW_STEP)):
        match = re.search(rf"{label}:\s*([1-9][0-9]*)", output)
        if not match or int(match.group(1)) != expected:
            raise RuntimeError(f"PointCloud2 echo {label} was not {expected}.\n{output}")

    if "name: x" not in output or "name: y" not in output or "name: z" not in output:
        raise RuntimeError(f"PointCloud2 echo did not contain x/y/z fields.\n{output}")


def main(argv: list[str]) -> int:
    """Script entry point."""

    args = parse_args(argv)
    workspace_root = ros2env.find_workspace_root()
    ros2_root = ros2env.resolve_existing_path(args.ros2_root, "ROS2 root", workspace_root)
    rviz_config = ros2env.resolve_existing_path(args.rviz_config, "RViz2 config", workspace_root)
    pixi_python, ros2_script = ros2env.validate_ros2_root(ros2_root)
    env = ros2env.build_ros_env(ros2_root, args.rmw, args.discovery_range)

    print(f"[phase129] ROS2 root: {ros2_root}")
    print(f"[phase129] pixi Python: {pixi_python}")
    print(f"[phase129] ros2-script.py: {ros2_script}")
    print(f"[phase129] ROS_DISTRO: {env.get('ROS_DISTRO')}")
    print(f"[phase129] RMW_IMPLEMENTATION: {env.get('RMW_IMPLEMENTATION')}")
    print(f"[phase129] ROS_DOMAIN_ID: {env.get('ROS_DOMAIN_ID')}")
    print(f"[phase129] ROS_AUTOMATIC_DISCOVERY_RANGE: {env.get('ROS_AUTOMATIC_DISCOVERY_RANGE', '<unset>')}")
    print(f"[phase129] RViz2 config: {rviz_config}")

    print("--- node list (diagnostic) ---")
    nodes = ros2env.probe_node_list(pixi_python, ros2_script, env)
    print(nodes.rstrip() or "<empty>")
    if NODE_NAME not in nodes:
        print(f"[phase129] node list did not include {NODE_NAME}; continuing with publisher endpoint and echo checks.")

    print("--- topic info -v /tf (diagnostic) ---")
    tf_info = ros2env.probe_topic_info(pixi_python, ros2_script, env, TF_TOPIC)
    print(tf_info.rstrip() or "<empty>")
    if not has_phase129_publisher(tf_info):
        print("[phase129] /tf topic info did not prove the publisher; continuing with /tf echo content check.")

    print("--- topic info -v /points ---")
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

    print("--- echo /tf ---")
    tf_echo = ros2env.echo_once(pixi_python, ros2_script, env, TF_TOPIC, TF_MSG_TYPE, args.echo_spin_seconds)
    print(tf_echo.rstrip())
    validate_tf_echo(tf_echo)

    print("--- echo /points ---")
    points_echo = ros2env.echo_once(pixi_python, ros2_script, env, POINTS_TOPIC, POINTS_MSG_TYPE, args.echo_spin_seconds)
    print(points_echo.rstrip())
    validate_pointcloud2_echo(points_echo)

    if args.launch_rviz:
        ros2env.launch_rviz(ros2_root, rviz_config, env, "phase129")

    print("[phase129] GREEN: /tf and /points external ROS2 acceptance checks completed.")
    print("[phase129] Confirm RViz2 displays TF map/base_link/point_cloud_sensor and PointCloud2 /points before marking manual PASS.")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main(sys.argv[1:]))
    except KeyboardInterrupt:
        raise SystemExit(130)
    except Exception as exc:
        print(f"[phase129] FAIL: {exc}", file=sys.stderr)
        raise SystemExit(1)
