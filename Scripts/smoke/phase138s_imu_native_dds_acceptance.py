#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Module: Scripts/smoke
# Purpose: ROS2-side acceptance helper for Phase 138S IMU native DDS output.




from __future__ import annotations

import argparse
import json
import os
import pathlib
import re
import shutil
import subprocess
import sys

import _ros2_windows_env as ros2env

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(line_buffering=True)
if hasattr(sys.stderr, "reconfigure"):
    sys.stderr.reconfigure(line_buffering=True)


DEFAULT_TOPIC = "/imu/data"
IMU_MSG_TYPE = "sensor_msgs/msg/Imu"
NUMBER_RE = r"-?(?:(?:[0-9]+(?:\.[0-9]*)?)|(?:\.[0-9]+))(?:[eE][+-]?[0-9]+)?"


def parse_args(argv: list[str]) -> argparse.Namespace:
    """Build and parse CLI arguments for IMU DDS acceptance."""


    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--ros2-root",
        default=None,
        help=(
            "Windows ROS2 Jazzy root. Default on Windows: C:\\ros2_jazzy\\ros2-windows. "
            "On WSL/Linux, omit this to use the current ROS2 Python environment."
        ),
    )
    parser.add_argument(
        "--topic",
        default=DEFAULT_TOPIC,
        help="IMU topic to validate. Default: /imu/data",
    )
    parser.add_argument(
        "--expected-frame-id",
        default=None,
        help="Optional expected header.frame_id, for example os_imu or imu_link.",
    )
    parser.add_argument(
        "--wait-seconds",
        type=float,
        default=30.0,
        help="How long to wait for a publisher endpoint. Default: 30",
    )
    parser.add_argument(
        "--echo-spin-seconds",
        type=float,
        default=12.0,
        help="ROS2 spin time for bounded echo. Default: 12",
    )
    parser.add_argument(
        "--rmw",
        default=None,
        help="RMW implementation. Omit to preserve existing value or default to rmw_fastrtps_cpp.",
    )
    parser.add_argument(
        "--domain-id",
        default=None,
        help="ROS_DOMAIN_ID to use. Omit to use domain 0.",
    )
    parser.add_argument(
        "--discovery-range",
        choices=("LOCALHOST", "SUBNET", "OFF", "SYSTEM_DEFAULT"),
        default="SUBNET",
        help="ROS_AUTOMATIC_DISCOVERY_RANGE. Default: SUBNET to match Unity ROS2 For Unity discovery on Windows.",
    )
    parser.add_argument(
        "--node-name",
        default=None,
        help="Optional publisher node-name hint used during verbose topic-info validation.",
    )
    parser.add_argument(
        "--require-graph-info",
        action="store_true",
        help="Require topic-info publisher discovery before echo. By default graph info is diagnostic.",
    )
    parser.add_argument(
        "--require-nonzero-vector",
        action="store_true",
        help="Require angular_velocity or linear_acceleration to contain a non-zero component.",
    )
    parser.add_argument(
        "--print-json",
        action="store_true",
        help="Print structured JSON evidence in addition to text logs.",
    )
    parser.add_argument(
        "--no-print-json",
        dest="print_json",
        action="store_false",
        help="Disable structured JSON evidence output.",
    )
    parser.set_defaults(print_json=True)
    return parser.parse_args(argv)


def require_regex(label: str, output: str, pattern: str, message: str) -> re.Match[str]:
    """Find a regex match and raise if no match is found."""


    match = re.search(pattern, output, re.MULTILINE)
    if not match:
        raise RuntimeError(f"{label}: {message}\n{output}")
    return match


def parse_timestamp(echo_output: str) -> tuple[int, int]:
    """Parse message timestamp fields from IMU echo output."""


    sec = int(require_regex("imu echo", echo_output, r"^\s*sec:\s*([0-9]+)\s*$", "missing stamp.sec").group(1))
    nanosec = int(
        require_regex("imu echo", echo_output, r"^\s*nanosec:\s*([0-9]+)\s*$", "missing stamp.nanosec").group(1)
    )
    if sec <= 0:
        raise RuntimeError(f"imu echo: stamp.sec must be positive, got {sec}\n{echo_output}")
    if not 0 <= nanosec < 1_000_000_000:
        raise RuntimeError(f"imu echo: stamp.nanosec out of range, got {nanosec}\n{echo_output}")
    return sec, nanosec


def parse_frame_id(echo_output: str) -> str:
    """Extract frame_id value from IMU echo output."""


    return require_regex("imu echo", echo_output, r"^\s*frame_id:\s*['\"]?([^'\"\r\n]+)['\"]?\s*$", "missing frame_id").group(1)


def section_after(output: str, field: str) -> str:
    """Return the text section that follows a top-level field."""


    match = re.search(rf"(?m)^{re.escape(field)}:\s*$", output)
    if not match:
        return ""

    section = output[match.end() :]
    next_top_level = re.search(r"(?m)^[a-zA-Z_][a-zA-Z0-9_]*:", section)
    if next_top_level:
        section = section[: next_top_level.start()]
    return section


def parse_vector(output: str, field: str) -> dict[str, float]:
    """Parse x/y/z vector values from IMU echo output section."""


    section = section_after(output, field)
    if not section:
        raise RuntimeError(f"imu echo: missing {field} section\n{output}")

    values: dict[str, float] = {}
    for axis in ("x", "y", "z"):
        match = require_regex(field, section, rf"^\s*{axis}:\s*({NUMBER_RE})\s*$", f"missing {axis}")
        values[axis] = float(match.group(1))
    return values


def parse_covariance(output: str, field: str) -> list[float]:
    """Parse 9 covariance values from IMU echo output."""


    section = section_after(output, field)
    if not section:
        raise RuntimeError(f"imu echo: missing {field} section\n{output}")

    values = [float(match.group(1)) for match in re.finditer(rf"(?m)^\s*-\s*({NUMBER_RE})\s*$", section)]
    if len(values) != 9:
        raise RuntimeError(f"imu echo: {field} must contain 9 values, got {len(values)}\n{output}")
    return values


def topic_list_has_type(topic_list: str, topic: str, msg_type: str) -> bool:
    """Check whether topic list shows expected type for topic."""


    return f"{topic} [{msg_type}]" in topic_list


def probe_topic_list(
    pixi_python: pathlib.Path,
    ros2_script: pathlib.Path,
    env: dict[str, str],
    timeout_seconds: float = 5.0,
) -> str:
    """Invoke ros2-script helper to list topic/type information."""


    try:
        return ros2env.run_ros2(
            pixi_python,
            ros2_script,
            env,
            ["topic", "list", "-t", "--no-daemon"],
            check=False,
            timeout_seconds=timeout_seconds,
        ).stdout
    except subprocess.TimeoutExpired:
        return f"<topic list timed out after {timeout_seconds:.1f}s>"


def run_native_ros2_cli(args: list[str], env: dict[str, str], timeout_seconds: float) -> str:
    """Run a ros2 command in native environment and capture output."""


    ros2_exe = shutil.which("ros2", path=env.get("PATH"))
    if not ros2_exe:
        return "<ros2 command not found on PATH; source ROS2 before relying on graph diagnostics>"

    try:
        return subprocess.run(
            [ros2_exe, *args],
            env=env,
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            check=False,
            timeout=timeout_seconds,
        ).stdout
    except subprocess.TimeoutExpired:
        return f"<ros2 {' '.join(args)} timed out after {timeout_seconds:.1f}s>"


def build_native_ros_env(args: argparse.Namespace) -> dict[str, str]:
    """Build ROS environment for native (non-Windows-helper) runs."""


    env = os.environ.copy()
    env["ROS_VERSION"] = env.get("ROS_VERSION") or "2"
    env["ROS_PYTHON_VERSION"] = env.get("ROS_PYTHON_VERSION") or "3"
    env["ROS_DOMAIN_ID"] = str(args.domain_id) if args.domain_id is not None else env.get("ROS_DOMAIN_ID", "0")
    env["RMW_IMPLEMENTATION"] = args.rmw or env.get("RMW_IMPLEMENTATION") or "rmw_fastrtps_cpp"
    if args.discovery_range:
        env["ROS_AUTOMATIC_DISCOVERY_RANGE"] = args.discovery_range
    env.pop("ROS_LOCALHOST_ONLY", None)
    env.pop("ROS_DISCOVERY_SERVER", None)
    return env


def topic_info_has_publisher(topic_info: str, node_name: str | None = None) -> bool:
    """Return true if topic info includes an active publisher."""


    publisher_match = re.search(r"Publisher count:\s*([1-9][0-9]*)", topic_info)
    if not publisher_match:
        return False
    return node_name is None or f"Node name: {node_name}" in topic_info


def wait_for_native_publisher(
    env: dict[str, str],
    topic: str,
    timeout_seconds: float,
    expected_type: str,
    node_name: str | None,
) -> str:
    """Wait until the publisher appears in native topic info output."""


    import time

    deadline = time.monotonic() + timeout_seconds
    last_output = ""
    while time.monotonic() < deadline:
        last_output = run_native_ros2_cli(["topic", "info", "-v", topic, "--no-daemon"], env, timeout_seconds=5.0)
        if expected_type in last_output and topic_info_has_publisher(last_output, node_name):
            return last_output
        time.sleep(1.0)

    raise TimeoutError(f"Timed out waiting for publisher on {topic}.\nLast topic info:\n{last_output}")


def subscribe_once_imu(
    pixi_python: pathlib.Path,
    env: dict[str, str],
    topic: str,
    spin_seconds: float,
) -> dict[str, object]:
    """Receive one IMU sample with rclpy, avoiding Windows ros2 CLI echo hangs."""
    # Receive one IMU sample with rclpy, avoiding Windows ros2 CLI echo hangs.

    subscriber_code = r'''
import json
import sys
import time

import rclpy
from rclpy.qos import HistoryPolicy, QoSProfile, ReliabilityPolicy
from sensor_msgs.msg import Imu

topic = sys.argv[1]
spin_seconds = float(sys.argv[2])
received = []

def vector_dict(value):
    """Build a dict from Vector3-like values."""
    return {"x": float(value.x), "y": float(value.y), "z": float(value.z)}

def capture(msg):
    """Capture first sample and keep it for downstream validation."""
    if received:
        return
    received.append(
        {
            "stamp": {
                "sec": int(msg.header.stamp.sec),
                "nanosec": int(msg.header.stamp.nanosec),
            },
            "frame_id": str(msg.header.frame_id),
            "orientation": {
                "x": float(msg.orientation.x),
                "y": float(msg.orientation.y),
                "z": float(msg.orientation.z),
                "w": float(msg.orientation.w),
            },
            "angular_velocity": vector_dict(msg.angular_velocity),
            "linear_acceleration": vector_dict(msg.linear_acceleration),
            "orientation_covariance": [float(value) for value in msg.orientation_covariance],
            "angular_velocity_covariance": [float(value) for value in msg.angular_velocity_covariance],
            "linear_acceleration_covariance": [float(value) for value in msg.linear_acceleration_covariance],
        }
    )

rclpy.init(args=None)
node = rclpy.create_node("u2f_phase138s_imu_native_acceptance")
qos_reliable = QoSProfile(depth=10)
qos_best_effort = QoSProfile(
    history=HistoryPolicy.KEEP_LAST,
    depth=10,
    reliability=ReliabilityPolicy.BEST_EFFORT,
)
subscriptions = [
    node.create_subscription(Imu, topic, capture, qos_reliable),
    node.create_subscription(Imu, topic, capture, qos_best_effort),
]
deadline = time.monotonic() + spin_seconds
try:
    while time.monotonic() < deadline and not received:
        rclpy.spin_once(node, timeout_sec=0.2)
finally:
    for subscription in subscriptions:
        node.destroy_subscription(subscription)
    node.destroy_node()
    rclpy.shutdown()

if not received:
    print("No sensor_msgs/msg/Imu sample received on " + topic, flush=True)
    sys.exit(2)

print("U2F_IMU_JSON=" + json.dumps(received[0], sort_keys=True), flush=True)
'''

    result = subprocess.run(
        [str(pixi_python), "-c", subscriber_code, topic, str(spin_seconds)],
        env=env,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        check=False,
        timeout=spin_seconds + 10.0,
    )
    if result.returncode != 0:
        raise RuntimeError(
            "IMU direct rclpy subscriber failed:\n"
            + f"exit={result.returncode}\n"
            + result.stdout
        )

    for line in reversed(result.stdout.splitlines()):
        if line.startswith("U2F_IMU_JSON="):
            return json.loads(line[len("U2F_IMU_JSON=") :])

    raise RuntimeError("IMU direct rclpy subscriber did not print structured payload.\n" + result.stdout)


def main(argv: list[str]) -> int:
    """Run IMU DDS acceptance flow and return result code."""


    args = parse_args(argv)
    workspace_root = ros2env.find_workspace_root()
    use_windows_ros2 = os.name == "nt" or args.ros2_root is not None
    if use_windows_ros2:
        ros2_root_text = args.ros2_root or str(ros2env.DEFAULT_ROS2_ROOT)
        ros2_root = ros2env.resolve_existing_path(ros2_root_text, "ROS2 root", workspace_root)
        pixi_python, ros2_script = ros2env.validate_ros2_root(ros2_root)
        env = ros2env.build_ros_env(ros2_root, args.rmw, args.discovery_range, args.domain_id)
        print(f"[phase138s-imu-dds] ROS2 root: {ros2_root}")
        print(f"[phase138s-imu-dds] ros2-script.py: {ros2_script}")
    else:
        ros2_script = None
        pixi_python = pathlib.Path(sys.executable)
        env = build_native_ros_env(args)
        print("[phase138s-imu-dds] ROS2 root: <native WSL/Linux environment>")
        print(f"[phase138s-imu-dds] python: {pixi_python}")
    print(f"[phase138s-imu-dds] ROS_DISTRO: {env.get('ROS_DISTRO', '<unset>')}")
    print(f"[phase138s-imu-dds] RMW_IMPLEMENTATION: {env.get('RMW_IMPLEMENTATION', '<unset>')}")
    print(f"[phase138s-imu-dds] ROS_DOMAIN_ID: {env.get('ROS_DOMAIN_ID', '<unset>')}")
    print(f"[phase138s-imu-dds] ROS_AUTOMATIC_DISCOVERY_RANGE: {env.get('ROS_AUTOMATIC_DISCOVERY_RANGE', '<unset>')}")
    print(f"[phase138s-imu-dds] topic={args.topic} wait={args.wait_seconds}s spin={args.echo_spin_seconds}s")

    if ros2_script is None:
        topic_list = run_native_ros2_cli(["topic", "list", "-t", "--no-daemon"], env, timeout_seconds=5.0)
    else:
        topic_list = probe_topic_list(pixi_python, ros2_script, env)
    print(f"--- ros2 topic list -t --no-daemon ---\n{topic_list}")
    if not topic_list_has_type(topic_list, args.topic, IMU_MSG_TYPE):
        ros2env.log_event(
            "phase138s-imu-dds",
            f"{args.topic} was not confirmed as {IMU_MSG_TYPE} in diagnostic topic list; direct subscriber remains the hard gate.",
        )

    print(f"--- ros2 topic info {args.topic} --verbose --no-daemon ---")
    if args.require_graph_info:
        if ros2_script is None:
            topic_info = wait_for_native_publisher(env, args.topic, args.wait_seconds, IMU_MSG_TYPE, args.node_name)
        else:
            topic_info = ros2env.wait_for_publisher(
                pixi_python,
                ros2_script,
                env,
                args.topic,
                args.wait_seconds,
                expected_type=IMU_MSG_TYPE,
                node_name=args.node_name,
            )
    else:
        if ros2_script is None:
            topic_info = run_native_ros2_cli(["topic", "info", "-v", args.topic, "--no-daemon"], env, timeout_seconds=5.0)
        else:
            topic_info = ros2env.probe_topic_info(
                pixi_python,
                ros2_script,
                env,
                args.topic,
                timeout_seconds=5.0,
            )
    print(topic_info)

    print(f"--- direct rclpy subscribe {args.topic} {IMU_MSG_TYPE} --once ---")
    sample = subscribe_once_imu(pixi_python, env, args.topic, args.echo_spin_seconds)
    print(json.dumps(sample, indent=2, sort_keys=True))

    stamp = sample["stamp"]
    sec = int(stamp["sec"])
    nanosec = int(stamp["nanosec"])
    if sec <= 0:
        raise RuntimeError(f"imu sample: stamp.sec must be positive, got {sec}")
    if not 0 <= nanosec < 1_000_000_000:
        raise RuntimeError(f"imu sample: stamp.nanosec out of range, got {nanosec}")

    frame_id = str(sample["frame_id"])
    if args.expected_frame_id is not None and frame_id != args.expected_frame_id:
        raise RuntimeError(
            f"imu sample: frame_id mismatch, expected {args.expected_frame_id!r}, got {frame_id!r}"
        )

    angular_velocity = sample["angular_velocity"]
    linear_acceleration = sample["linear_acceleration"]
    orientation_covariance = sample["orientation_covariance"]
    angular_velocity_covariance = sample["angular_velocity_covariance"]
    linear_acceleration_covariance = sample["linear_acceleration_covariance"]
    for label, covariance in (
        ("orientation_covariance", orientation_covariance),
        ("angular_velocity_covariance", angular_velocity_covariance),
        ("linear_acceleration_covariance", linear_acceleration_covariance),
    ):
        if len(covariance) != 9:
            raise RuntimeError(f"imu sample: {label} must contain 9 values, got {len(covariance)}")

    if args.require_nonzero_vector:
        vector_values = [*angular_velocity.values(), *linear_acceleration.values()]
        if not any(abs(value) > 1e-9 for value in vector_values):
            raise RuntimeError("imu echo: angular_velocity and linear_acceleration are all zero.")

    evidence = {
        "topic": args.topic,
        "msg_type": IMU_MSG_TYPE,
        "stamp": {"sec": sec, "nanosec": nanosec},
        "frame_id": frame_id,
        "angular_velocity": angular_velocity,
        "linear_acceleration": linear_acceleration,
        "orientation_covariance_0": orientation_covariance[0],
        "covariance_lengths": {
            "orientation": len(orientation_covariance),
            "angular_velocity": len(angular_velocity_covariance),
            "linear_acceleration": len(linear_acceleration_covariance),
        },
    }

    if args.print_json:
        print("--- structured evidence ---")
        print(json.dumps(evidence, indent=2, sort_keys=True))

    print("[phase138s-imu-dds] PASS")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main(sys.argv[1:]))
    except (RuntimeError, TimeoutError, FileNotFoundError, subprocess.TimeoutExpired) as exc:
        print(f"[phase138s-imu-dds] FAIL: {exc}", file=sys.stderr)
        raise SystemExit(1) from exc
