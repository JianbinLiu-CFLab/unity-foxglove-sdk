#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Module: Scripts/smoke
# Purpose: Short-lived rclpy collector for Phase132 standard message acceptance.

"""Collect one message from each Phase132 standard ROS2 topic and print JSON."""

from __future__ import annotations

import json
import os
import sys
import time

import rclpy
from geometry_msgs.msg import PoseStamped
from nav_msgs.msg import Odometry
from rclpy.node import Node
from sensor_msgs.msg import CameraInfo, Image, Imu, NavSatFix


RESULT_PREFIX = "PHASE132_PROBE_JSON="
TOPICS = [
    "/camera/camera_info",
    "/camera/image_raw",
    "/imu/data",
    "/odom",
    "/pose",
    "/fix",
]


class Phase132Probe(Node):
    """Subscribe to every Phase132 topic and retain the first message summary."""

    def __init__(self) -> None:
        """Create bounded subscriptions for all standard message sample topics."""
        super().__init__("phase132_acceptance_probe")
        self.results: dict[str, dict[str, object]] = {}
        self.create_subscription(CameraInfo, "/camera/camera_info", self.on_camera_info, 10)
        self.create_subscription(Image, "/camera/image_raw", self.on_image, 10)
        self.create_subscription(Imu, "/imu/data", self.on_imu, 10)
        self.create_subscription(Odometry, "/odom", self.on_odometry, 10)
        self.create_subscription(PoseStamped, "/pose", self.on_pose, 10)
        self.create_subscription(NavSatFix, "/fix", self.on_navsatfix, 10)

    def capture_once(self, topic: str, payload: dict[str, object]) -> None:
        """Store only the first message for each topic."""

        if topic not in self.results:
            self.results[topic] = payload

    def on_camera_info(self, msg: CameraInfo) -> None:
        """Capture CameraInfo fields needed for acceptance."""

        self.capture_once(
            "/camera/camera_info",
            {
                "frame_id": msg.header.frame_id,
                "height": int(msg.height),
                "width": int(msg.width),
                "distortion_model": msg.distortion_model,
                "k": list(msg.k),
                "r": list(msg.r),
                "p": list(msg.p),
            },
        )

    def on_image(self, msg: Image) -> None:
        """Capture Image fields without dumping the full byte array."""

        data = msg.data
        self.capture_once(
            "/camera/image_raw",
            {
                "frame_id": msg.header.frame_id,
                "height": int(msg.height),
                "width": int(msg.width),
                "encoding": msg.encoding,
                "step": int(msg.step),
                "data_length": len(data),
                "data_sum": int(sum(data)),
            },
        )

    def on_imu(self, msg: Imu) -> None:
        """Capture IMU fields needed for acceptance."""

        self.capture_once(
            "/imu/data",
            {
                "frame_id": msg.header.frame_id,
                "orientation_w": float(msg.orientation.w),
                "linear_acceleration_z": float(msg.linear_acceleration.z),
                "orientation_covariance": list(msg.orientation_covariance),
                "angular_velocity_covariance": list(msg.angular_velocity_covariance),
                "linear_acceleration_covariance": list(msg.linear_acceleration_covariance),
            },
        )

    def on_odometry(self, msg: Odometry) -> None:
        """Capture Odometry fields needed for acceptance."""

        self.capture_once(
            "/odom",
            {
                "frame_id": msg.header.frame_id,
                "child_frame_id": msg.child_frame_id,
                "orientation_w": float(msg.pose.pose.orientation.w),
                "pose_covariance": list(msg.pose.covariance),
                "twist_covariance": list(msg.twist.covariance),
            },
        )

    def on_pose(self, msg: PoseStamped) -> None:
        """Capture PoseStamped fields needed for acceptance."""

        self.capture_once(
            "/pose",
            {
                "frame_id": msg.header.frame_id,
                "orientation_w": float(msg.pose.orientation.w),
            },
        )

    def on_navsatfix(self, msg: NavSatFix) -> None:
        """Capture NavSatFix fields needed for acceptance."""

        self.capture_once(
            "/fix",
            {
                "frame_id": msg.header.frame_id,
                "latitude": float(msg.latitude),
                "longitude": float(msg.longitude),
                "altitude": float(msg.altitude),
                "status": int(msg.status.status),
                "service": int(msg.status.service),
                "position_covariance": list(msg.position_covariance),
                "position_covariance_type": int(msg.position_covariance_type),
            },
        )


def main(argv: list[str]) -> int:
    """Run the collector and print one JSON result line."""

    timeout_seconds = float(argv[0]) if argv else 20.0
    rclpy.init()
    node = Phase132Probe()
    deadline = time.monotonic() + timeout_seconds
    while rclpy.ok() and len(node.results) < len(TOPICS) and time.monotonic() < deadline:
        rclpy.spin_once(node, timeout_sec=0.1)

    missing = [topic for topic in TOPICS if topic not in node.results]
    print(RESULT_PREFIX + json.dumps({"messages": node.results, "missing": missing}, sort_keys=True), flush=True)

    # On Windows, rclpy teardown can occasionally outlive the acceptance timeout.
    # This probe is a short-lived process, so exit after flushing the summary.
    os._exit(1 if missing else 0)


if __name__ == "__main__":
    main(sys.argv[1:])
