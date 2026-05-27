#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Module: Scripts/smoke
# Purpose: Manual ROS2 and RViz2 acceptance helper for Phase130 MarkerArray.

"""Validate Phase130 RViz2 MarkerArray standard visualization topics.

Start Unity manually first, import the RViz2 MarkerArray Acceptance sample, add
Phase130Rviz2MarkerArraySmoke to a scene object, and enter Play Mode. This
helper then uses the pinned Windows ROS2 Jazzy Python entry point to check the
external ROS2 graph and launch RViz2 by default.
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
    r"\RViz2 MarkerArray Acceptance\rviz2_phase130_markerarray.rviz"
)
NODE_NAME = "unity2foxglove_phase130_markerarray"
TF_TOPIC = "/tf"
TF_MSG_TYPE = "tf2_msgs/msg/TFMessage"
MARKERS_TOPIC = "/markers"
MARKERS_MSG_TYPE = "visualization_msgs/msg/MarkerArray"


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
        help="How long to wait for Unity's Phase130 node and /markers publisher.",
    )
    parser.add_argument(
        "--echo-spin-seconds",
        type=float,
        default=20.0,
        help="ROS2 spin time for the bounded marker echo.",
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


def validate_markerarray_echo(output: str) -> None:
    """Validate MarkerArray echo contains a bounded ADD/MODIFY cube marker."""

    required = ["markers:", "frame_id: map", "ns: unity2foxglove", "id:", "lifetime:"]
    missing = [token for token in required if token not in output]
    if missing:
        raise RuntimeError(f"MarkerArray echo missing required token(s): {', '.join(missing)}\n{output}")

    id_match = re.search(r"\bid:\s*([1-9][0-9]*)", output)
    if not id_match:
        raise RuntimeError(f"MarkerArray echo did not contain a positive deterministic id.\n{output}")

    if "type: 1" not in output:
        raise RuntimeError(f"MarkerArray echo did not contain CUBE marker type 1.\n{output}")
    if "action: 0" not in output:
        raise RuntimeError(f"MarkerArray echo did not contain an ADD/MODIFY action.\n{output}")
    if "sec: 0" not in output or "nanosec: 0" not in output:
        raise RuntimeError(f"MarkerArray echo did not contain zero marker lifetime.\n{output}")


def echo_markerarray_until_add(
    pixi_python: pathlib.Path,
    ros2_script: pathlib.Path,
    env: dict[str, str],
    spin_seconds: float,
    attempts: int = 4,
) -> str:
    """Collect bounded marker echoes until an ADD/MODIFY marker is observed."""

    combined = ""
    for _ in range(attempts):
        combined += ros2env.echo_once(pixi_python, ros2_script, env, MARKERS_TOPIC, MARKERS_MSG_TYPE, spin_seconds) + "\n"
        if "action: 0" in combined:
            return combined
    return combined


def validate_tf_echo(output: str) -> None:
    """Validate the minimal TF that makes RViz2 fixed frame map valid."""

    required = ["transforms:", "frame_id: map", "child_frame_id: phase130_marker_origin"]
    missing = [token for token in required if token not in output]
    if missing:
        raise RuntimeError(f"TF echo missing required token(s): {', '.join(missing)}\n{output}")


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
            "phase130",
            startup_check_seconds=startup_check_seconds,
            window_wait_seconds=window_wait_seconds,
        )
    else:
        ros2env.log_event("phase130", "RViz2 launch skipped because --no-launch-rviz was supplied.")


def main(argv: list[str]) -> int:
    """Script entry point."""

    args = parse_args(argv)
    workspace_root = ros2env.find_workspace_root()
    ros2_root = ros2env.resolve_existing_path(args.ros2_root, "ROS2 root", workspace_root)
    rviz_config = ros2env.resolve_existing_path(args.rviz_config, "RViz2 config", workspace_root)
    pixi_python, ros2_script = ros2env.validate_ros2_root(ros2_root)
    env = ros2env.build_ros_env(ros2_root, args.rmw, args.discovery_range)

    print(f"[phase130] ROS2 root: {ros2_root}")
    print(f"[phase130] pixi Python: {pixi_python}")
    print(f"[phase130] ros2-script.py: {ros2_script}")
    print(f"[phase130] ROS_DISTRO: {env.get('ROS_DISTRO')}")
    print(f"[phase130] RMW_IMPLEMENTATION: {env.get('RMW_IMPLEMENTATION')}")
    print(f"[phase130] ROS_DOMAIN_ID: {env.get('ROS_DOMAIN_ID')}")
    print(f"[phase130] ROS_AUTOMATIC_DISCOVERY_RANGE: {env.get('ROS_AUTOMATIC_DISCOVERY_RANGE', '<unset>')}")
    print(f"[phase130] RViz2 config: {rviz_config}")

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
        print(f"[phase130] node list did not include {NODE_NAME}; continuing with publisher endpoint and echo checks.")

    print("--- topic info -v /tf (diagnostic) ---")
    tf_info = ros2env.wait_for_publisher(
        pixi_python,
        ros2_script,
        env,
        TF_TOPIC,
        args.wait_seconds,
        expected_type=TF_MSG_TYPE,
        node_name=NODE_NAME,
    )
    print(tf_info.rstrip())

    print("--- topic info -v /markers ---")
    markers_info = ros2env.wait_for_publisher(
        pixi_python,
        ros2_script,
        env,
        MARKERS_TOPIC,
        args.wait_seconds,
        expected_type=MARKERS_MSG_TYPE,
        node_name=NODE_NAME,
    )
    print(markers_info.rstrip())

    print("--- echo /tf ---")
    tf_echo = ros2env.echo_once(
        pixi_python,
        ros2_script,
        env,
        TF_TOPIC,
        TF_MSG_TYPE,
        args.echo_spin_seconds,
    )
    print(tf_echo.rstrip())
    validate_tf_echo(tf_echo)

    print("--- echo /markers ---")
    markers_echo = echo_markerarray_until_add(
        pixi_python,
        ros2_script,
        env,
        args.echo_spin_seconds,
    )
    print(markers_echo.rstrip())
    validate_markerarray_echo(markers_echo)

    print("[phase130] GREEN: /tf and /markers external ROS2 acceptance checks completed.")
    print("[phase130] Confirm RViz2 displays MarkerArray /markers without fixed-frame warnings before marking manual PASS.")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main(sys.argv[1:]))
    except KeyboardInterrupt:
        raise SystemExit(130)
    except Exception as exc:
        print(f"[phase130] FAIL: {exc}", file=sys.stderr)
        raise SystemExit(1)
