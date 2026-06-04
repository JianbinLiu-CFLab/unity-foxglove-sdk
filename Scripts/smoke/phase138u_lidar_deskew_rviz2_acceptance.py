#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Module: Scripts/smoke
# Purpose: ROS2-side acceptance helper for Phase138U raw + deskewed PointCloud2 DDS.

"""Validate raw and deskewed Phase138U PointCloud2 topics and launch RViz2.

Start Unity Play Mode first with ROS2 Native (R2FU), PointCloud2 Native mode,
and PointCloud Motion Compensation enabled in RawAndDeskewedTopic mode. Then run:

    python Scripts/smoke/phase138u_lidar_deskew_rviz2_acceptance.py

The script launches RViz2 by default for visual comparison and uses direct rclpy
subscribers as the hard DDS acceptance gate. Pass --no-rviz when only the DDS
subscription check is needed.
"""

from __future__ import annotations

import argparse
import json
import pathlib
import subprocess
import sys

import _ros2_windows_env as ros2env
import launch_phase138u_lidar_deskew_rviz2 as rviz2launch


POINTCLOUD2_MSG_TYPE = "sensor_msgs/msg/PointCloud2"


def parse_args(argv: list[str]) -> argparse.Namespace:
    """Parse acceptance arguments."""

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--ros2-root", default=str(ros2env.DEFAULT_ROS2_ROOT))
    parser.add_argument("--raw-topic", default="/unity/point_cloud2")
    parser.add_argument("--deskewed-topic", default="/unity/point_cloud2_deskewed")
    parser.add_argument("--expected-frame-id", default="os_lidar")
    parser.add_argument("--fixed-frame", default="map")
    parser.add_argument("--spin-seconds", type=float, default=12.0)
    parser.add_argument("--rmw", default=None)
    parser.add_argument("--domain-id", default=None)
    parser.add_argument(
        "--discovery-range",
        choices=("LOCALHOST", "SUBNET", "OFF", "SYSTEM_DEFAULT"),
        default="SUBNET",
    )
    parser.add_argument("--print-json", action="store_true")
    parser.add_argument("--no-print-json", dest="print_json", action="store_false")
    parser.add_argument("--no-rviz", dest="launch_rviz", action="store_false")
    parser.add_argument("--skip-topic-probe", action="store_true")
    parser.set_defaults(print_json=True)
    parser.set_defaults(launch_rviz=True)
    return parser.parse_args(argv)


def normalize_topic(topic: str) -> str:
    """Normalize a topic to leading-slash form."""

    value = (topic or "").strip()
    if not value:
        raise ValueError("topic must not be empty")
    return value if value.startswith("/") else "/" + value


def subscribe_once_pointcloud2_pair(
    pixi_python: pathlib.Path,
    env: dict[str, str],
    raw_topic: str,
    deskewed_topic: str,
    spin_seconds: float,
) -> dict[str, object]:
    """Receive raw and deskewed PointCloud2 messages in one rclpy process."""

    subscriber_code = r'''
import json
import sys
import time

import rclpy
from rclpy.qos import HistoryPolicy, QoSProfile, ReliabilityPolicy
from sensor_msgs.msg import PointCloud2

raw_topic = sys.argv[1]
deskewed_topic = sys.argv[2]
spin_seconds = float(sys.argv[3])
results = {}

def capture(topic, msg):
    """Capture one PointCloud2 message into JSON-safe evidence."""
    if topic in results:
        return
    results[topic] = {
        "topic": topic,
        "msg_type": "sensor_msgs/msg/PointCloud2",
        "frame_id": msg.header.frame_id,
        "stamp": {"sec": int(msg.header.stamp.sec), "nanosec": int(msg.header.stamp.nanosec)},
        "height": int(msg.height),
        "width": int(msg.width),
        "point_step": int(msg.point_step),
        "row_step": int(msg.row_step),
        "data_length": len(msg.data),
        "is_dense": bool(msg.is_dense),
        "fields": [field.name for field in msg.fields],
    }

rclpy.init(args=None)
node = rclpy.create_node("phase138u_pointcloud2_direct_subscriber")
qos_reliable = QoSProfile(depth=10)
qos_best_effort = QoSProfile(
    history=HistoryPolicy.KEEP_LAST,
    depth=10,
    reliability=ReliabilityPolicy.BEST_EFFORT,
)
subs = []
for topic in (raw_topic, deskewed_topic):
    subs.append(node.create_subscription(PointCloud2, topic, lambda msg, t=topic: capture(t, msg), qos_reliable))
    subs.append(node.create_subscription(PointCloud2, topic, lambda msg, t=topic: capture(t, msg), qos_best_effort))

deadline = time.time() + spin_seconds
try:
    while rclpy.ok() and len(results) < 2 and time.time() < deadline:
        rclpy.spin_once(node, timeout_sec=0.2)
finally:
    for subscription in subs:
        node.destroy_subscription(subscription)
    node.destroy_node()
    rclpy.shutdown()

missing = [topic for topic in (raw_topic, deskewed_topic) if topic not in results]
if missing:
    print("Missing sensor_msgs/msg/PointCloud2 sample(s): " + ", ".join(missing), flush=True)
    print("PHASE138U_PARTIAL_JSON=" + json.dumps(results, sort_keys=True), flush=True)
    sys.exit(2)
print("PHASE138U_POINTCLOUD2_JSON=" + json.dumps({"raw": results[raw_topic], "deskewed": results[deskewed_topic]}, sort_keys=True), flush=True)
'''
    result = subprocess.run(
        [str(pixi_python), "-c", subscriber_code, raw_topic, deskewed_topic, str(spin_seconds)],
        env=env,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        check=False,
        timeout=spin_seconds + 10.0,
    )
    if result.returncode != 0:
        raise RuntimeError(
            "PointCloud2 direct rclpy subscriber failed:\n"
            + "Unity must be in Play Mode, Enable Deskew must be on, and Output Policy should be RawAndDeskewedTopic.\n"
            + result.stdout)

    for line in reversed(result.stdout.splitlines()):
        if line.startswith("PHASE138U_POINTCLOUD2_JSON="):
            return json.loads(line[len("PHASE138U_POINTCLOUD2_JSON="):])
    raise RuntimeError("PointCloud2 direct subscriber did not print structured payload.\n" + result.stdout)


def validate_sample(sample: dict[str, object], expected_frame_id: str | None) -> None:
    """Validate one PointCloud2 sample."""

    if expected_frame_id and sample.get("frame_id") != expected_frame_id:
        raise RuntimeError(f"frame_id mismatch for {sample.get('topic')}: {sample.get('frame_id')} != {expected_frame_id}")
    if int(sample.get("width", 0)) <= 0:
        raise RuntimeError(f"PointCloud2 width is zero for {sample.get('topic')}")
    if int(sample.get("data_length", 0)) <= 0:
        raise RuntimeError(f"PointCloud2 data is empty for {sample.get('topic')}")


def main(argv: list[str]) -> int:
    """Script entry point."""

    args = parse_args(argv)
    workspace_root = ros2env.find_workspace_root()
    ros2_root = ros2env.resolve_existing_path(args.ros2_root, "ROS2 root", workspace_root)
    pixi_python, ros2_script = ros2env.validate_ros2_root(ros2_root)
    env = ros2env.build_ros_env(
        ros2_root,
        rmw_implementation=args.rmw,
        discovery_range=args.discovery_range,
        domain_id=args.domain_id,
    )

    raw_topic = normalize_topic(args.raw_topic)
    deskewed_topic = normalize_topic(args.deskewed_topic)

    print(f"[phase138u-lidar-deskew] ROS2 root: {ros2_root}")
    print(f"[phase138u-lidar-deskew] ros2-script.py: {ros2_script}")
    print(f"[phase138u-lidar-deskew] raw={raw_topic} deskewed={deskewed_topic} spin={args.spin_seconds}s")

    if args.launch_rviz:
        config_path = rviz2launch.write_config(
            workspace_root,
            raw_topic,
            deskewed_topic,
            rviz2launch.normalize_frame(args.fixed_frame),
        )
        print(f"[phase138u-lidar-deskew] RViz2 config: {config_path}")
        rviz_process = ros2env.launch_rviz(
            ros2_root,
            config_path,
            env=env,
            log_prefix="phase138u-lidar-deskew",
            startup_check_seconds=1.5,
            window_wait_seconds=0.0,
        )
        print(f"[phase138u-lidar-deskew] RViz2 pid={rviz_process.pid}")

    if not args.skip_topic_probe:
        print("--- ros2 topic list -t --no-daemon ---")
        try:
            topic_list = ros2env.run_ros2(
                pixi_python,
                ros2_script,
                env,
                ["topic", "list", "-t", "--no-daemon"],
                check=False,
                timeout_seconds=5.0,
            ).stdout
            print(topic_list)
        except subprocess.TimeoutExpired:
            print("<topic list timed out after 5.0s>")

    evidence = subscribe_once_pointcloud2_pair(pixi_python, env, raw_topic, deskewed_topic, args.spin_seconds)
    validate_sample(evidence["raw"], args.expected_frame_id)
    validate_sample(evidence["deskewed"], args.expected_frame_id)

    if evidence["raw"]["width"] != evidence["deskewed"]["width"]:
        raise RuntimeError(
            f"raw/deskewed point count mismatch: {evidence['raw']['width']} != {evidence['deskewed']['width']}"
        )

    print("--- structured evidence ---")
    print(json.dumps(evidence, indent=2, sort_keys=True))
    if args.print_json:
        print("PHASE138U_LIDAR_DESKEW_JSON=" + json.dumps(evidence, sort_keys=True))
    print("[phase138u-lidar-deskew] PASS")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main(sys.argv[1:]))
    except KeyboardInterrupt:
        raise SystemExit(130)
    except Exception as exc:
        print(f"[phase138u-lidar-deskew] FAIL: {exc}", file=sys.stderr)
        raise SystemExit(1)
