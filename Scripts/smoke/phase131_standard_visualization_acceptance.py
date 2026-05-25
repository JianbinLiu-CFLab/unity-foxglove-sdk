#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Module: Scripts/smoke
# Purpose: Productized ROS2/RViz2 acceptance helper for the standard visualization v1 kit.

"""Validate the RViz2 standard visualization v1 topic set.

Start Unity manually first. Import the RViz2 Standard Visualization Acceptance,
RViz2 PointCloud2 Acceptance, and RViz2 MarkerArray Acceptance publisher samples,
then enter Play Mode. This helper uses the pinned Windows ROS2 Jazzy Python
entry point to check /tf, /scan, /points, and /markers, then launches RViz2
with the consolidated v1 config by default.
"""

from __future__ import annotations

import argparse
import pathlib
import re
import sys
import time

import _ros2_windows_env as ros2env


DEFAULT_RVIZ_CONFIG = pathlib.Path(
    r"Packages\dev.unity2foxglove.ros2forunity\Samples~"
    r"\RViz2 Standard Visualization v1\rviz2_phase131_standard_visualization.rviz"
)
NODE_128 = "unity2foxglove_phase128_rviz2"
NODE_129 = "unity2foxglove_phase129_pointcloud2"
NODE_130 = "unity2foxglove_phase130_markerarray"
TF_TOPIC = "/tf"
SCAN_TOPIC = "/scan"
POINTS_TOPIC = "/points"
MARKERS_TOPIC = "/markers"
TF_MSG_TYPE = "tf2_msgs/msg/TFMessage"
SCAN_MSG_TYPE = "sensor_msgs/msg/LaserScan"
POINTS_MSG_TYPE = "sensor_msgs/msg/PointCloud2"
MARKERS_MSG_TYPE = "visualization_msgs/msg/MarkerArray"


def parse_args(argv: list[str]) -> argparse.Namespace:
    """Parse command-line arguments."""

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--ros2-root",
        default=str(ros2env.DEFAULT_ROS2_ROOT),
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
        default=120.0,
        help="How long to wait for Unity's v1 publishers.",
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
        help="Override ROS_AUTOMATIC_DISCOVERY_RANGE. Omit to preserve the shell value or ROS2 default.",
    )
    launch_group = parser.add_mutually_exclusive_group()
    launch_group.add_argument(
        "--launch-rviz",
        dest="launch_rviz",
        action="store_true",
        help="Launch RViz2 with --rviz-config after publisher endpoint checks. This is the default.",
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


def require_tokens(label: str, output: str, tokens: list[str]) -> None:
    """Raise when any required token is absent from output."""

    missing = [token for token in tokens if token not in output]
    if missing:
        raise RuntimeError(f"{label} missing required token(s): {', '.join(missing)}\n{output}")


def echo_until_tokens(
    pixi_python: pathlib.Path,
    ros2_script: pathlib.Path,
    env: dict[str, str],
    topic: str,
    msg_type: str,
    spin_seconds: float,
    tokens: list[str],
    attempts: int = 4,
) -> str:
    """Collect bounded echoes until all required tokens have appeared."""

    combined = ""
    for _ in range(attempts):
        combined += ros2env.echo_once(pixi_python, ros2_script, env, topic, msg_type, spin_seconds) + "\n"
        if all(token in combined for token in tokens):
            return combined
    require_tokens(topic, combined, tokens)
    return combined


def validate_scan_echo(output: str) -> None:
    """Validate LaserScan echo contains a non-empty scan in the laser frame."""

    require_tokens("LaserScan echo", output, ["frame_id: laser", "angle_min:", "angle_max:", "ranges:"])
    if not re.search(r"-\s*[1-9][0-9]*(?:\.[0-9]+)?", output):
        raise RuntimeError(f"LaserScan echo did not contain a positive range.\n{output}")


def validate_pointcloud2_echo(output: str) -> None:
    """Validate PointCloud2 echo contains point_cloud_sensor frame and non-empty metadata."""

    require_tokens("PointCloud2 echo", output, ["frame_id: point_cloud_sensor", "height: 1", "fields:", "data:"])
    width_match = re.search(r"width:\s*([1-9][0-9]*)", output)
    if not width_match:
        raise RuntimeError(f"PointCloud2 echo did not contain a positive width.\n{output}")


def validate_markerarray_echo(output: str) -> None:
    """Validate MarkerArray echo contains a bounded cube marker or cleanup action."""

    require_tokens("MarkerArray echo", output, ["markers:", "frame_id: map", "ns: unity2foxglove", "id:"])
    if "action: 3" in output:
        return
    require_tokens("MarkerArray echo", output, ["type: 1", "lifetime:"])
    if "action: 0" not in output and "action: 2" not in output:
        raise RuntimeError(f"MarkerArray echo did not contain ADD or DELETE action.\n{output}")


def launch_rviz_before_echo(
    should_launch: bool,
    ros2_root: pathlib.Path,
    rviz_config: pathlib.Path,
    env: dict[str, str],
    startup_check_seconds: float,
    window_wait_seconds: float,
) -> None:
    """Launch RViz2 before one-shot echo validation, once publisher endpoints exist."""

    if should_launch:
        ros2env.launch_rviz(
            ros2_root,
            rviz_config,
            env,
            "phase131",
            startup_check_seconds=startup_check_seconds,
            window_wait_seconds=window_wait_seconds,
        )
    else:
        ros2env.log_event("phase131", "RViz2 launch skipped because --no-launch-rviz was supplied.")


def main(argv: list[str]) -> int:
    """Script entry point."""

    args = parse_args(argv)
    script_started = time.perf_counter()
    ros2env.log_event(
        "phase131",
        "script start "
        + f"launch_rviz={args.launch_rviz} wait_seconds={args.wait_seconds:.1f} "
        + f"echo_spin_seconds={args.echo_spin_seconds:.1f}",
    )
    workspace_root = ros2env.find_workspace_root()
    ros2_root = ros2env.resolve_existing_path(args.ros2_root, "ROS2 root", workspace_root)
    rviz_config = ros2env.resolve_existing_path(args.rviz_config, "RViz2 config", workspace_root)
    pixi_python, ros2_script = ros2env.validate_ros2_root(ros2_root)
    env = ros2env.build_ros_env(ros2_root, args.rmw, args.discovery_range)

    print(f"[phase131] ROS2 root: {ros2_root}")
    print(f"[phase131] pixi Python: {pixi_python}")
    print(f"[phase131] ros2-script.py: {ros2_script}")
    print(f"[phase131] ROS_DISTRO: {env.get('ROS_DISTRO')}")
    print(f"[phase131] RMW_IMPLEMENTATION: {env.get('RMW_IMPLEMENTATION')}")
    print(f"[phase131] ROS_DOMAIN_ID: {env.get('ROS_DOMAIN_ID')}")
    print(f"[phase131] ROS_AUTOMATIC_DISCOVERY_RANGE: {env.get('ROS_AUTOMATIC_DISCOVERY_RANGE', '<unset>')}")
    print(f"[phase131] RViz2 config: {rviz_config}")

    launch_rviz_before_echo(
        args.launch_rviz,
        ros2_root,
        rviz_config,
        env,
        args.rviz_startup_check_seconds,
        args.rviz_window_wait_seconds,
    )

    print("--- node list (diagnostic) ---")
    stage_started = time.perf_counter()
    ros2env.log_event("phase131", "node list probe start")
    nodes = ros2env.probe_node_list(pixi_python, ros2_script, env)
    print(nodes.rstrip() or "<empty>")
    ros2env.log_event("phase131", f"node list probe done elapsed={time.perf_counter() - stage_started:.3f}s")

    topics = [
        (TF_TOPIC, TF_MSG_TYPE, None),
        (SCAN_TOPIC, SCAN_MSG_TYPE, NODE_128),
        (POINTS_TOPIC, POINTS_MSG_TYPE, NODE_129),
        (MARKERS_TOPIC, MARKERS_MSG_TYPE, NODE_130),
    ]
    for topic, msg_type, node_name in topics:
        print(f"--- topic info -v {topic} ---")
        stage_started = time.perf_counter()
        ros2env.log_event("phase131", f"topic wait start topic={topic} type={msg_type}")
        info = ros2env.wait_for_publisher(
            pixi_python,
            ros2_script,
            env,
            topic,
            args.wait_seconds,
            msg_type,
            node_name,
        )
        print(info.rstrip())
        ros2env.log_event("phase131", f"topic wait done topic={topic} elapsed={time.perf_counter() - stage_started:.3f}s")

    print("--- echo /tf ---")
    stage_started = time.perf_counter()
    ros2env.log_event("phase131", "echo start topic=/tf")
    tf_echo = echo_until_tokens(
        pixi_python,
        ros2_script,
        env,
        TF_TOPIC,
        TF_MSG_TYPE,
        args.echo_spin_seconds,
        ["map", "base_link", "laser", "point_cloud_sensor"],
    )
    print(tf_echo.rstrip())
    ros2env.log_event("phase131", f"echo done topic=/tf elapsed={time.perf_counter() - stage_started:.3f}s")

    print("--- echo /scan ---")
    stage_started = time.perf_counter()
    ros2env.log_event("phase131", "echo start topic=/scan")
    scan_echo = ros2env.echo_once(pixi_python, ros2_script, env, SCAN_TOPIC, SCAN_MSG_TYPE, args.echo_spin_seconds)
    print(scan_echo.rstrip())
    validate_scan_echo(scan_echo)
    ros2env.log_event("phase131", f"echo done topic=/scan elapsed={time.perf_counter() - stage_started:.3f}s")

    print("--- echo /points ---")
    stage_started = time.perf_counter()
    ros2env.log_event("phase131", "echo start topic=/points")
    points_echo = ros2env.echo_once(pixi_python, ros2_script, env, POINTS_TOPIC, POINTS_MSG_TYPE, args.echo_spin_seconds)
    print(points_echo.rstrip())
    validate_pointcloud2_echo(points_echo)
    ros2env.log_event("phase131", f"echo done topic=/points elapsed={time.perf_counter() - stage_started:.3f}s")

    print("--- echo /markers ---")
    stage_started = time.perf_counter()
    ros2env.log_event("phase131", "echo start topic=/markers")
    markers_echo = ros2env.echo_once(pixi_python, ros2_script, env, MARKERS_TOPIC, MARKERS_MSG_TYPE, args.echo_spin_seconds)
    print(markers_echo.rstrip())
    validate_markerarray_echo(markers_echo)
    ros2env.log_event("phase131", f"echo done topic=/markers elapsed={time.perf_counter() - stage_started:.3f}s")

    print("[phase131] GREEN: /tf, /scan, /points, and /markers external ROS2 acceptance checks completed.")
    print("[phase131] Confirm RViz2 displays TF, LaserScan, PointCloud2, and MarkerArray before marking manual PASS.")
    ros2env.log_event("phase131", f"script completed elapsed={time.perf_counter() - script_started:.3f}s")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main(sys.argv[1:]))
    except KeyboardInterrupt:
        raise SystemExit(130)
    except Exception as exc:
        print(f"[phase131] FAIL: {exc}", file=sys.stderr)
        raise SystemExit(1)
