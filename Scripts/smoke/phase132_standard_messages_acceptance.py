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
standard ROS2 topics and representative echo payloads, then launches RViz2 by
default for a lightweight visual sanity check.
"""

from __future__ import annotations

import argparse
import json
import pathlib
import re
import subprocess
import sys
import time

import _ros2_windows_env as ros2env


DEFAULT_RVIZ_CONFIG = pathlib.Path(
    r"Packages\dev.unity2foxglove.ros2forunity\Samples~"
    r"\ROS2 Standard Message Expansion\rviz2_phase132_standard_messages.rviz"
)
NODE_NAME = "unity2foxglove_phase132_standard_messages"
TOPICS = [
    ("/camera/camera_info", "sensor_msgs/msg/CameraInfo"),
    ("/camera/image_raw", "sensor_msgs/msg/Image"),
    ("/imu/data", "sensor_msgs/msg/Imu"),
    ("/odom", "nav_msgs/msg/Odometry"),
    ("/pose", "geometry_msgs/msg/PoseStamped"),
    ("/fix", "sensor_msgs/msg/NavSatFix"),
]
PHASE132_RCLPY_PROBE_PATH = pathlib.Path(__file__).with_name("_phase132_rclpy_probe.py")


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
        help="Compatibility timeout for publisher preflight. Echo remains the hard acceptance gate.",
    )
    parser.add_argument(
        "--topic-info-timeout-seconds",
        type=float,
        default=2.0,
        help="Per-topic timeout for optional verbose topic-info diagnostics before echo validation.",
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
        default="LOCALHOST",
        help="ROS_AUTOMATIC_DISCOVERY_RANGE for same-machine Unity acceptance. Default: LOCALHOST.",
    )
    launch_group = parser.add_mutually_exclusive_group()
    launch_group.add_argument(
        "--launch-rviz",
        dest="launch_rviz",
        action="store_true",
        help="Launch RViz2 with --rviz-config after the rclpy message checks. This is the default.",
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
    parser.add_argument(
        "--graph-diagnostics",
        action="store_true",
        help="Run optional ros2 node/topic graph diagnostics before the rclpy collection gate.",
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


def topic_list_has_type(output: str, topic: str, msg_type: str) -> bool:
    """Return whether `ros2 topic list -t` reports a topic with the expected type."""

    return f"{topic} [{msg_type}]" in output


def wait_for_topic_type(
    pixi_python: pathlib.Path,
    ros2_script: pathlib.Path,
    env: dict[str, str],
    topic: str,
    msg_type: str,
    timeout_seconds: float,
) -> str:
    """Wait until topic list reports a topic/type pair."""

    deadline = time.monotonic() + timeout_seconds
    last_output = ""
    while True:
        remaining = deadline - time.monotonic()
        if remaining <= 0.0:
            break

        probe_timeout_seconds = max(0.5, min(5.0, remaining))
        try:
            result = ros2env.run_ros2(
                pixi_python,
                ros2_script,
                env,
                ["topic", "list", "-t", "--no-daemon"],
                check=False,
                timeout_seconds=probe_timeout_seconds,
            )
            last_output = result.stdout
        except subprocess.TimeoutExpired:
            last_output = f"<topic list timed out after {probe_timeout_seconds:.1f}s>"

        if topic_list_has_type(last_output, topic, msg_type):
            return last_output

        remaining = deadline - time.monotonic()
        if remaining > 0.0:
            time.sleep(min(1.0, remaining))

    raise TimeoutError(
        f"Timed out waiting for topic list entry {topic} [{msg_type}].\n"
        f"Last topic list:\n{last_output}"
    )


def wait_for_phase132_topic(
    pixi_python: pathlib.Path,
    ros2_script: pathlib.Path,
    env: dict[str, str],
    topic: str,
    msg_type: str,
    timeout_seconds: float,
) -> str:
    """Wait for a Phase132 topic, with a topic-list fallback for slow verbose info."""

    try:
        return ros2env.wait_for_publisher(
            pixi_python,
            ros2_script,
            env,
            topic,
            timeout_seconds,
            msg_type,
            NODE_NAME,
        )
    except (TimeoutError, subprocess.TimeoutExpired) as exc:
        ros2env.log_event(
            "phase132",
            f"verbose endpoint wait inconclusive for {topic}; falling back to topic list + echo gate. reason={exc}",
        )
        try:
            topic_list = wait_for_topic_type(
                pixi_python,
                ros2_script,
                env,
                topic,
                msg_type,
                min(timeout_seconds, 15.0),
            )
            return (
                "Verbose endpoint check timed out; topic list fallback succeeded.\n"
                + topic_list
            )
        except TimeoutError as topic_list_exc:
            ros2env.log_event(
                "phase132",
                f"topic list fallback inconclusive for {topic}; echo remains the acceptance gate. reason={topic_list_exc}",
            )
            return (
                "Endpoint preflight inconclusive; continuing to bounded echo validation.\n"
                + str(topic_list_exc)
            )


def collect_phase132_messages(
    pixi_python: pathlib.Path,
    env: dict[str, str],
    timeout_seconds: float,
) -> dict[str, dict[str, object]]:
    """Collect one message from every Phase132 topic using rclpy in the ROS2 runtime."""

    if not PHASE132_RCLPY_PROBE_PATH.exists():
        raise FileNotFoundError(f"Phase132 rclpy probe script does not exist: {PHASE132_RCLPY_PROBE_PATH}")

    try:
        completed = subprocess.run(
            [str(pixi_python), str(PHASE132_RCLPY_PROBE_PATH), str(timeout_seconds)],
            env=env,
            capture_output=True,
            text=True,
            timeout=timeout_seconds + 10.0,
            check=False,
        )
        stdout = completed.stdout
        stderr = completed.stderr
        returncode = completed.returncode
    except subprocess.TimeoutExpired as exc:
        stdout = exc.stdout or ""
        stderr = exc.stderr or ""
        if isinstance(stdout, bytes):
            stdout = stdout.decode(errors="replace")
        if isinstance(stderr, bytes):
            stderr = stderr.decode(errors="replace")
        returncode = 124

    prefix = "PHASE132_PROBE_JSON="
    payload_line = next((line[len(prefix) :] for line in stdout.splitlines() if line.startswith(prefix)), None)
    if payload_line is None:
        raise RuntimeError(
            "Phase132 rclpy probe did not produce JSON output.\n"
            f"exit={returncode}\nstdout:\n{stdout}\nstderr:\n{stderr}"
        )

    payload = json.loads(payload_line)
    missing = payload.get("missing") or []
    messages = payload.get("messages") or {}
    if missing:
        raise RuntimeError(
            "Phase132 rclpy probe timed out before receiving all topics: "
            + ", ".join(str(topic) for topic in missing)
            + f"\nstdout:\n{stdout}\nstderr:\n{stderr}"
        )
    if returncode != 0:
        raise RuntimeError(
            f"Phase132 rclpy probe failed with exit code {returncode}.\n"
            f"stdout:\n{stdout}\nstderr:\n{stderr}"
        )
    return messages


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


def require_summary_field(topic: str, summary: dict[str, object], field: str) -> object:
    """Return a collected summary field or raise with context."""

    if field not in summary:
        raise RuntimeError(f"{topic} summary missing field {field}: {summary}")
    return summary[field]


def validate_camera_info_summary(summary: dict[str, object]) -> None:
    """Validate collected CameraInfo fields."""

    if require_summary_field("/camera/camera_info", summary, "frame_id") != "camera_optical_frame":
        raise RuntimeError(f"CameraInfo frame_id mismatch: {summary}")
    if int(require_summary_field("/camera/camera_info", summary, "width")) != 32:
        raise RuntimeError(f"CameraInfo width mismatch: {summary}")
    if int(require_summary_field("/camera/camera_info", summary, "height")) != 24:
        raise RuntimeError(f"CameraInfo height mismatch: {summary}")
    for field, expected_len in (("k", 9), ("r", 9), ("p", 12)):
        values = require_summary_field("/camera/camera_info", summary, field)
        if not isinstance(values, list) or len(values) != expected_len:
            raise RuntimeError(f"CameraInfo {field} length mismatch: {summary}")
    k = require_summary_field("/camera/camera_info", summary, "k")
    if float(k[0]) <= 0.0 or float(k[4]) <= 0.0:
        raise RuntimeError(f"CameraInfo focal terms are not positive: {summary}")


def validate_image_summary(summary: dict[str, object]) -> None:
    """Validate collected Image fields."""

    if require_summary_field("/camera/image_raw", summary, "frame_id") != "camera_optical_frame":
        raise RuntimeError(f"Image frame_id mismatch: {summary}")
    width = int(require_summary_field("/camera/image_raw", summary, "width"))
    height = int(require_summary_field("/camera/image_raw", summary, "height"))
    step = int(require_summary_field("/camera/image_raw", summary, "step"))
    if width != 32 or height != 24 or step != 96:
        raise RuntimeError(f"Image dimensions mismatch: {summary}")
    if require_summary_field("/camera/image_raw", summary, "encoding") != "rgb8":
        raise RuntimeError(f"Image encoding mismatch: {summary}")
    expected = height * step
    if int(require_summary_field("/camera/image_raw", summary, "data_length")) < expected:
        raise RuntimeError(f"Image data length is shorter than expected {expected}: {summary}")
    if int(require_summary_field("/camera/image_raw", summary, "data_sum")) <= 0:
        raise RuntimeError(f"Image data appears empty: {summary}")


def validate_imu_summary(summary: dict[str, object]) -> None:
    """Validate collected IMU fields."""

    if require_summary_field("/imu/data", summary, "frame_id") != "base_link":
        raise RuntimeError(f"IMU frame_id mismatch: {summary}")
    if float(require_summary_field("/imu/data", summary, "orientation_w")) <= 0.0:
        raise RuntimeError(f"IMU orientation is not non-zero: {summary}")
    if float(require_summary_field("/imu/data", summary, "linear_acceleration_z")) < 9.0:
        raise RuntimeError(f"IMU acceleration z is not gravity-like: {summary}")
    for field in ("orientation_covariance", "angular_velocity_covariance", "linear_acceleration_covariance"):
        values = require_summary_field("/imu/data", summary, field)
        if not isinstance(values, list) or len(values) != 9:
            raise RuntimeError(f"IMU {field} length mismatch: {summary}")


def validate_odometry_summary(summary: dict[str, object]) -> None:
    """Validate collected Odometry fields."""

    if require_summary_field("/odom", summary, "frame_id") != "odom":
        raise RuntimeError(f"Odometry frame_id mismatch: {summary}")
    if require_summary_field("/odom", summary, "child_frame_id") != "base_link":
        raise RuntimeError(f"Odometry child_frame_id mismatch: {summary}")
    if float(require_summary_field("/odom", summary, "orientation_w")) <= 0.0:
        raise RuntimeError(f"Odometry orientation is not non-zero: {summary}")
    for field in ("pose_covariance", "twist_covariance"):
        values = require_summary_field("/odom", summary, field)
        if not isinstance(values, list) or len(values) != 36:
            raise RuntimeError(f"Odometry {field} length mismatch: {summary}")


def validate_pose_summary(summary: dict[str, object]) -> None:
    """Validate collected PoseStamped fields."""

    if require_summary_field("/pose", summary, "frame_id") != "map":
        raise RuntimeError(f"PoseStamped frame_id mismatch: {summary}")
    if float(require_summary_field("/pose", summary, "orientation_w")) <= 0.0:
        raise RuntimeError(f"PoseStamped orientation is not non-zero: {summary}")


def validate_navsatfix_summary(summary: dict[str, object]) -> None:
    """Validate collected NavSatFix fields."""

    if require_summary_field("/fix", summary, "frame_id") != "gps_link":
        raise RuntimeError(f"NavSatFix frame_id mismatch: {summary}")
    latitude = float(require_summary_field("/fix", summary, "latitude"))
    longitude = float(require_summary_field("/fix", summary, "longitude"))
    if abs(latitude) <= 0.0001 and abs(longitude) <= 0.0001:
        raise RuntimeError(f"NavSatFix coordinates are both near zero: {summary}")
    if latitude < -90.0 or latitude > 90.0 or longitude < -180.0 or longitude > 180.0:
        raise RuntimeError(f"NavSatFix coordinates are outside WGS84 ranges: {summary}")
    values = require_summary_field("/fix", summary, "position_covariance")
    if not isinstance(values, list) or len(values) != 9:
        raise RuntimeError(f"NavSatFix covariance length mismatch: {summary}")
    if int(require_summary_field("/fix", summary, "position_covariance_type")) <= 0:
        raise RuntimeError(f"NavSatFix covariance type is not known: {summary}")


VALIDATORS = {
    "/camera/camera_info": validate_camera_info,
    "/camera/image_raw": validate_image,
    "/imu/data": validate_imu,
    "/odom": validate_odometry,
    "/pose": validate_pose,
    "/fix": validate_navsatfix,
}
SUMMARY_VALIDATORS = {
    "/camera/camera_info": validate_camera_info_summary,
    "/camera/image_raw": validate_image_summary,
    "/imu/data": validate_imu_summary,
    "/odom": validate_odometry_summary,
    "/pose": validate_pose_summary,
    "/fix": validate_navsatfix_summary,
}


def main(argv: list[str]) -> int:
    """Script entry point."""

    args = parse_args(argv)
    script_started = time.perf_counter()
    ros2env.log_event(
        "phase132",
        "script start "
        + f"launch_rviz={args.launch_rviz} wait_seconds={args.wait_seconds:.1f} "
        + f"topic_info_timeout_seconds={args.topic_info_timeout_seconds:.1f} "
        + f"echo_spin_seconds={args.echo_spin_seconds:.1f}",
    )
    workspace_root = ros2env.find_workspace_root()
    ros2_root = ros2env.resolve_existing_path(args.ros2_root, "ROS2 root", workspace_root)
    rviz_config = ros2env.resolve_existing_path(args.rviz_config, "RViz2 config", workspace_root)
    pixi_python, ros2_script = ros2env.validate_ros2_root(ros2_root)
    env = ros2env.build_ros_env(ros2_root, args.rmw, args.discovery_range, args.domain_id)

    print(f"[phase132] ROS2 root: {ros2_root}")
    print(f"[phase132] pixi Python: {pixi_python}")
    print(f"[phase132] ros2-script.py: {ros2_script}")
    print(f"[phase132] ROS_DISTRO: {env.get('ROS_DISTRO')}")
    print(f"[phase132] RMW_IMPLEMENTATION: {env.get('RMW_IMPLEMENTATION')}")
    print(f"[phase132] ROS_DOMAIN_ID: {env.get('ROS_DOMAIN_ID')}")
    print(f"[phase132] ROS_AUTOMATIC_DISCOVERY_RANGE: {env.get('ROS_AUTOMATIC_DISCOVERY_RANGE', '<unset>')}")
    print(f"[phase132] RViz2 config: {rviz_config}")

    if args.graph_diagnostics:
        print("--- node list (diagnostic) ---")
        stage_started = time.perf_counter()
        ros2env.log_event("phase132", "node list probe start")
        nodes = ros2env.probe_node_list(pixi_python, ros2_script, env)
        print(nodes.rstrip() or "<empty>")
        ros2env.log_event("phase132", f"node list probe done elapsed={time.perf_counter() - stage_started:.3f}s")

        for topic, _msg_type in TOPICS:
            print(f"--- topic info -v {topic} ---")
            stage_started = time.perf_counter()
            ros2env.log_event("phase132", f"topic info diagnostic start topic={topic}")
            info = ros2env.probe_topic_info(
                pixi_python,
                ros2_script,
                env,
                topic,
                timeout_seconds=args.topic_info_timeout_seconds,
            )
            print(info.rstrip())
            ros2env.log_event(
                "phase132",
                f"topic info diagnostic done topic={topic} elapsed={time.perf_counter() - stage_started:.3f}s",
            )
    else:
        ros2env.log_event("phase132", "graph diagnostics skipped; rclpy collection is the acceptance gate.")

    print("--- rclpy collect Phase132 topics ---")
    stage_started = time.perf_counter()
    ros2env.log_event("phase132", "rclpy collect start")
    summaries = collect_phase132_messages(pixi_python, env, args.echo_spin_seconds)
    ros2env.log_event("phase132", f"rclpy collect done elapsed={time.perf_counter() - stage_started:.3f}s")

    for topic, _msg_type in TOPICS:
        print(f"--- collected {topic} ---")
        summary = summaries.get(topic)
        if not isinstance(summary, dict):
            raise RuntimeError(f"Phase132 rclpy probe did not return a summary for {topic}: {summaries}")
        print(json.dumps(summary, indent=2, sort_keys=True))
        SUMMARY_VALIDATORS[topic](summary)

    if args.launch_rviz:
        ros2env.launch_rviz(
            ros2_root,
            rviz_config,
            env,
            "phase132",
            startup_check_seconds=args.rviz_startup_check_seconds,
            window_wait_seconds=args.rviz_window_wait_seconds,
        )
    else:
        ros2env.log_event("phase132", "RViz2 launch skipped because --no-launch-rviz was supplied.")

    print("[phase132] GREEN: standard ROS2 message expansion CLI checks completed.")
    print("[phase132] RViz2 is a visual helper for /pose and /camera/image_raw; rclpy collection remains the acceptance gate.")
    ros2env.log_event("phase132", f"script completed elapsed={time.perf_counter() - script_started:.3f}s")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main(sys.argv[1:]))
    except KeyboardInterrupt:
        raise SystemExit(130)
    except Exception as exc:
        print(f"[phase132] FAIL: {exc}", file=sys.stderr)
        raise SystemExit(1)
