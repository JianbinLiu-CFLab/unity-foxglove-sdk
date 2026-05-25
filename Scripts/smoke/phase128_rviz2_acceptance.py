#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Module: Scripts/smoke
# Purpose: Manual ROS2 and RViz2 acceptance helper for Phase128 TF/LaserScan.

"""Validate Phase128 RViz2 standard visualization topics.

Start Unity manually first, import the RViz2 Standard Visualization Acceptance
sample, add Phase128Rviz2TfLaserScanSmoke to a scene object, and enter Play
Mode. This helper then uses the pinned Windows ROS2 Jazzy Python entry point to
check the external ROS2 graph and, optionally, launch RViz2.
"""

from __future__ import annotations

import argparse
import math
import pathlib
import re
import sys

import _ros2_windows_env as ros2env


DEFAULT_ROS2_ROOT = ros2env.DEFAULT_ROS2_ROOT
DEFAULT_RVIZ_CONFIG = pathlib.Path(
    r"Packages\dev.unity2foxglove.ros2forunity\Samples~"
    r"\RViz2 Standard Visualization Acceptance\rviz2_phase128_tf_laserscan.rviz"
)
NODE_NAME = "unity2foxglove_phase128_rviz2"
TF_TOPIC = "/tf"
SCAN_TOPIC = "/scan"
TF_MSG_TYPE = "tf2_msgs/msg/TFMessage"
SCAN_MSG_TYPE = "sensor_msgs/msg/LaserScan"


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
        help="How long to wait for Unity's Phase128 node and publishers.",
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
            "or the ROS2 default. Use LOCALHOST for same-machine Unity acceptance if SUBNET sees no Unity node."
        ),
    )
    parser.add_argument(
        "--launch-rviz",
        action="store_true",
        help="Launch RViz2 with --rviz-config after CLI checks pass.",
    )
    return parser.parse_args(argv)


def has_phase128_publisher(topic_info: str) -> bool:
    """Return whether verbose topic info contains a Phase128 publisher endpoint."""

    return ros2env.topic_info_has_publisher(topic_info, NODE_NAME)


def validate_tf_echo(output: str) -> None:
    """Validate TF echo contains the required frame tree."""

    missing = [token for token in ("map", "base_link", "laser") if token not in output]
    if missing:
        raise RuntimeError(f"TF echo missing required frame token(s): {', '.join(missing)}\n{output}")


def validate_scan_echo(output: str) -> None:
    """Validate LaserScan echo contains laser frame and finite ranges."""

    if "frame_id: laser" not in output:
        raise RuntimeError(f"LaserScan echo did not contain frame_id: laser.\n{output}")

    ranges_block = extract_ranges_block(output)
    values = [float(value) for value in re.findall(r"[-+]?(?:\d+\.\d+|\d+)(?:[eE][-+]?\d+)?", ranges_block)]
    finite_values = [value for value in values if math.isfinite(value)]
    if not finite_values:
        raise RuntimeError(f"LaserScan echo did not contain non-empty finite ranges.\n{output}")


def extract_ranges_block(output: str) -> str:
    """Extract the ranges section from a ROS2 LaserScan echo."""

    ranges_index = output.find("ranges:")
    if ranges_index < 0:
        raise RuntimeError(f"LaserScan echo did not contain ranges.\n{output}")

    intensities_index = output.find("intensities:", ranges_index)
    if intensities_index < 0:
        return output[ranges_index:]
    return output[ranges_index:intensities_index]


def main(argv: list[str]) -> int:
    """Script entry point."""

    args = parse_args(argv)
    workspace_root = ros2env.find_workspace_root()
    ros2_root = ros2env.resolve_existing_path(args.ros2_root, "ROS2 root", workspace_root)
    rviz_config = ros2env.resolve_existing_path(args.rviz_config, "RViz2 config", workspace_root)
    pixi_python, ros2_script = ros2env.validate_ros2_root(ros2_root)
    env = ros2env.build_ros_env(ros2_root, args.rmw, args.discovery_range)

    print(f"[phase128] ROS2 root: {ros2_root}")
    print(f"[phase128] pixi Python: {pixi_python}")
    print(f"[phase128] ros2-script.py: {ros2_script}")
    print(f"[phase128] ROS_DISTRO: {env.get('ROS_DISTRO')}")
    print(f"[phase128] RMW_IMPLEMENTATION: {env.get('RMW_IMPLEMENTATION')}")
    print(f"[phase128] ROS_DOMAIN_ID: {env.get('ROS_DOMAIN_ID')}")
    print(f"[phase128] ROS_AUTOMATIC_DISCOVERY_RANGE: {env.get('ROS_AUTOMATIC_DISCOVERY_RANGE', '<unset>')}")
    print(f"[phase128] RViz2 config: {rviz_config}")

    print("--- node list (diagnostic) ---")
    nodes = ros2env.probe_node_list(pixi_python, ros2_script, env)
    print(nodes.rstrip() or "<empty>")
    if NODE_NAME not in nodes:
        print(f"[phase128] node list did not include {NODE_NAME}; continuing with publisher endpoint and echo checks.")

    print("--- topic info -v /scan ---")
    scan_info = ros2env.wait_for_publisher(
        pixi_python,
        ros2_script,
        env,
        SCAN_TOPIC,
        args.wait_seconds,
        expected_type=SCAN_MSG_TYPE,
        node_name=NODE_NAME,
    )
    print(scan_info.rstrip())

    print("--- topic info -v /tf (diagnostic) ---")
    tf_info = ros2env.probe_topic_info(pixi_python, ros2_script, env, TF_TOPIC)
    print(tf_info.rstrip() or "<empty>")
    if not has_phase128_publisher(tf_info):
        print("[phase128] /tf topic info did not prove the publisher; continuing with /tf echo content check.")

    print("--- echo /tf ---")
    tf_echo = ros2env.echo_once(pixi_python, ros2_script, env, TF_TOPIC, TF_MSG_TYPE, args.echo_spin_seconds)
    print(tf_echo.rstrip())
    validate_tf_echo(tf_echo)

    print("--- echo /scan ---")
    scan_echo = ros2env.echo_once(pixi_python, ros2_script, env, SCAN_TOPIC, SCAN_MSG_TYPE, args.echo_spin_seconds)
    print(scan_echo.rstrip())
    validate_scan_echo(scan_echo)

    if args.launch_rviz:
        ros2env.launch_rviz(ros2_root, rviz_config, env, "phase128")

    print("[phase128] GREEN: /tf and /scan external ROS2 acceptance checks completed.")
    print("[phase128] Confirm RViz2 displays TF map/base_link/laser and LaserScan /scan before marking manual PASS.")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main(sys.argv[1:]))
    except KeyboardInterrupt:
        raise SystemExit(130)
    except Exception as exc:
        print(f"[phase128] FAIL: {exc}", file=sys.stderr)
        raise SystemExit(1)
