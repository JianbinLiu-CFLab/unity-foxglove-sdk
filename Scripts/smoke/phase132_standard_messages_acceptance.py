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
standard ROS2 topics and representative echo payloads.
"""

from __future__ import annotations

import argparse
import pathlib
import re
import sys

import _ros2_windows_env as ros2env


NODE_NAME = "unity2foxglove_phase132_standard_messages"
TOPICS = [
    ("/camera/camera_info", "sensor_msgs/msg/CameraInfo"),
    ("/camera/image_raw", "sensor_msgs/msg/Image"),
    ("/imu/data", "sensor_msgs/msg/Imu"),
    ("/odom", "nav_msgs/msg/Odometry"),
    ("/pose", "geometry_msgs/msg/PoseStamped"),
    ("/fix", "sensor_msgs/msg/NavSatFix"),
]


def parse_args(argv: list[str]) -> argparse.Namespace:
    """Parse command-line arguments."""

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--ros2-root",
        default=str(ros2env.DEFAULT_ROS2_ROOT),
        help="Windows ROS2 Jazzy root. Default: C:\\ros2_jazzy\\ros2-windows",
    )
    parser.add_argument(
        "--wait-seconds",
        type=float,
        default=120.0,
        help="How long to wait for Unity's standard message publishers.",
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
        "--domain-id",
        default=None,
        help="ROS_DOMAIN_ID to use for the helper. Omit to use domain 0.",
    )
    parser.add_argument(
        "--discovery-range",
        choices=("LOCALHOST", "SUBNET", "OFF", "SYSTEM_DEFAULT"),
        default=None,
        help="Override ROS_AUTOMATIC_DISCOVERY_RANGE. Omit to preserve the shell value or ROS2 default.",
    )
    return parser.parse_args(argv)


def require_tokens(label: str, output: str, tokens: list[str]) -> None:
    """Raise when any required token is absent from output."""

    missing = [token for token in tokens if token not in output]
    if missing:
        raise RuntimeError(f"{label} missing required token(s): {', '.join(missing)}\n{output}")


def require_regex(label: str, output: str, pattern: str, message: str) -> re.Match[str]:
    """Return the regex match or raise with context."""

    match = re.search(pattern, output, re.MULTILINE)
    if not match:
        raise RuntimeError(f"{label}: {message}\n{output}")
    return match


def parse_positive_int(label: str, output: str, field: str) -> int:
    """Parse a positive integer field from ROS2 YAML echo output."""

    match = require_regex(label, output, rf"^\s*{re.escape(field)}:\s*([1-9][0-9]*)\s*$", f"missing positive {field}")
    return int(match.group(1))


def parse_float(label: str, output: str, field: str) -> float:
    """Parse a floating-point field from ROS2 YAML echo output."""

    match = require_regex(
        label,
        output,
        rf"^\s*{re.escape(field)}:\s*(-?[0-9]+(?:\.[0-9]+)?)\s*$",
        f"missing numeric {field}",
    )
    return float(match.group(1))


def count_yaml_array_numbers_after(output: str, field: str) -> int:
    """Count YAML list scalar numbers following a top-level array field."""

    start = output.find(field + ":")
    if start < 0:
        return 0
    section = output[start + len(field) + 1 :]
    next_top_level = re.search(r"(?m)^[a-zA-Z_][a-zA-Z0-9_]*:", section)
    if next_top_level:
        section = section[: next_top_level.start()]
    return len(re.findall(r"(?m)^-\s*-?(?:[0-9]+(?:\.[0-9]+)?|[0-9]+)\s*$", section))


def validate_camera_info(output: str) -> None:
    """Validate CameraInfo echo contains meaningful calibration data."""

    require_tokens("CameraInfo echo", output, ["frame_id: camera_optical_frame", "distortion_model:", "k:", "r:", "p:"])
    height = parse_positive_int("CameraInfo echo", output, "height")
    width = parse_positive_int("CameraInfo echo", output, "width")
    if height != 24 or width != 32:
        raise RuntimeError(f"CameraInfo expected 32x24, got {width}x{height}.\n{output}")
    if not re.search(r"(?m)^-\s*(?:[1-9][0-9]*(?:\.[0-9]+)?|0\.[0-9]*[1-9][0-9]*)\s*$", output):
        raise RuntimeError(f"CameraInfo echo did not include positive focal terms.\n{output}")
    if count_yaml_array_numbers_after(output, "k") < 9 or count_yaml_array_numbers_after(output, "r") < 9:
        raise RuntimeError(f"CameraInfo k/r arrays were not fully echoed.\n{output}")
    if count_yaml_array_numbers_after(output, "p") < 12:
        raise RuntimeError(f"CameraInfo p array was not fully echoed.\n{output}")


def validate_image(output: str) -> None:
    """Validate Image echo contains a full tiny rgb8 payload."""

    require_tokens("Image echo", output, ["frame_id: camera_optical_frame", "encoding: rgb8", "data:"])
    height = parse_positive_int("Image echo", output, "height")
    width = parse_positive_int("Image echo", output, "width")
    step = parse_positive_int("Image echo", output, "step")
    if width != 32 or height != 24 or step != width * 3:
        raise RuntimeError(f"Image expected width=32 height=24 step=96, got width={width} height={height} step={step}.\n{output}")
    if "'...'" in output or "- ..." in output:
        raise RuntimeError(f"Image echo appears truncated; Phase132 validation requires full data echo.\n{output}")
    expected = height * step
    observed = count_yaml_array_numbers_after(output, "data")
    if observed < expected:
        raise RuntimeError(f"Image data echo contained {observed} bytes, expected at least {expected}.\n{output}")


def validate_imu(output: str) -> None:
    """Validate IMU echo contains non-zero default measurements."""

    require_tokens(
        "IMU echo",
        output,
        [
            "frame_id: base_link",
            "orientation:",
            "orientation_covariance:",
            "angular_velocity:",
            "angular_velocity_covariance:",
            "linear_acceleration:",
            "linear_acceleration_covariance:",
        ],
    )
    if "w: 1.0" not in output and "w: 1" not in output:
        raise RuntimeError(f"IMU orientation w is not non-zero.\n{output}")
    if not re.search(r"(?m)^\s*z:\s*9\.806", output):
        raise RuntimeError(f"IMU linear acceleration does not include gravity-like z value.\n{output}")


def validate_odometry(output: str) -> None:
    """Validate Odometry echo contains frame IDs, pose, twist, and orientation."""

    require_tokens("Odometry echo", output, ["frame_id: odom", "child_frame_id: base_link", "pose:", "twist:", "covariance:"])
    if "w: 1.0" not in output and "w: 1" not in output:
        raise RuntimeError(f"Odometry pose orientation w is not non-zero.\n{output}")


def validate_pose(output: str) -> None:
    """Validate PoseStamped echo contains map frame and pose."""

    require_tokens("PoseStamped echo", output, ["frame_id: map", "pose:", "position:", "orientation:"])
    if "w: 1.0" not in output and "w: 1" not in output:
        raise RuntimeError(f"PoseStamped orientation w is not non-zero.\n{output}")


def validate_navsatfix(output: str) -> None:
    """Validate NavSatFix echo contains explicit non-zero WGS84 coordinates."""

    require_tokens("NavSatFix echo", output, ["frame_id: gps_link", "status:", "service:", "position_covariance_type:"])
    latitude = parse_float("NavSatFix echo", output, "latitude")
    longitude = parse_float("NavSatFix echo", output, "longitude")
    parse_float("NavSatFix echo", output, "altitude")
    if abs(latitude) <= 0.0001 and abs(longitude) <= 0.0001:
        raise RuntimeError(f"NavSatFix coordinates are both near zero; expected explicit synthetic WGS84 values.\n{output}")
    if latitude < -90.0 or latitude > 90.0 or longitude < -180.0 or longitude > 180.0:
        raise RuntimeError(f"NavSatFix coordinates are outside valid WGS84 ranges.\n{output}")


VALIDATORS = {
    "/camera/camera_info": validate_camera_info,
    "/camera/image_raw": validate_image,
    "/imu/data": validate_imu,
    "/odom": validate_odometry,
    "/pose": validate_pose,
    "/fix": validate_navsatfix,
}


def main(argv: list[str]) -> int:
    """Script entry point."""

    args = parse_args(argv)
    workspace_root = ros2env.find_workspace_root()
    ros2_root = ros2env.resolve_existing_path(args.ros2_root, "ROS2 root", workspace_root)
    pixi_python, ros2_script = ros2env.validate_ros2_root(ros2_root)
    env = ros2env.build_ros_env(ros2_root, args.rmw, args.discovery_range, args.domain_id)

    print(f"[phase132] ROS2 root: {ros2_root}")
    print(f"[phase132] pixi Python: {pixi_python}")
    print(f"[phase132] ros2-script.py: {ros2_script}")
    print(f"[phase132] ROS_DISTRO: {env.get('ROS_DISTRO')}")
    print(f"[phase132] RMW_IMPLEMENTATION: {env.get('RMW_IMPLEMENTATION')}")
    print(f"[phase132] ROS_DOMAIN_ID: {env.get('ROS_DOMAIN_ID')}")
    print(f"[phase132] ROS_AUTOMATIC_DISCOVERY_RANGE: {env.get('ROS_AUTOMATIC_DISCOVERY_RANGE', '<unset>')}")

    print("--- node list (diagnostic) ---")
    nodes = ros2env.probe_node_list(pixi_python, ros2_script, env)
    print(nodes.rstrip() or "<empty>")

    for topic, msg_type in TOPICS:
        print(f"--- topic info -v {topic} ---")
        info = ros2env.wait_for_publisher(
            pixi_python,
            ros2_script,
            env,
            topic,
            args.wait_seconds,
            msg_type,
            NODE_NAME,
        )
        print(info.rstrip())

    for topic, msg_type in TOPICS:
        print(f"--- echo {topic} ---")
        echo = ros2env.echo_once(pixi_python, ros2_script, env, topic, msg_type, args.echo_spin_seconds)
        print(echo.rstrip())
        VALIDATORS[topic](echo)

    print("[phase132] GREEN: standard ROS2 message expansion CLI checks completed.")
    print("[phase132] Optional RViz2 checks require matching fixed frames or an external TF tree.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
