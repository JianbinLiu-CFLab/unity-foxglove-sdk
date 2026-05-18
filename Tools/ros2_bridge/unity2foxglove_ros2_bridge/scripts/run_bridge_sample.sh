#!/usr/bin/env bash
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Preflight and optionally launch the ROS2 Bridge sample from bash.

set -euo pipefail

HOST="${HOST:-127.0.0.1}"
PORT="${PORT:-8767}"
PAYLOAD_FORMAT="${PAYLOAD_FORMAT:-cdr-with-encapsulation}"
RUN=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --run)
      RUN=1
      shift
      ;;
    --host)
      HOST="$2"
      shift 2
      ;;
    --port)
      PORT="$2"
      shift 2
      ;;
    --payload-format)
      PAYLOAD_FORMAT="$2"
      shift 2
      ;;
    *)
      echo "Unknown argument: $1" >&2
      exit 2
      ;;
  esac
done

echo "Unity2Foxglove ROS2 Bridge sample preflight"
echo "ROS_DISTRO=${ROS_DISTRO:-<not sourced>}"

if ! command -v ros2 >/dev/null 2>&1; then
  echo "ros2 was not found. Source your ROS2 setup.bash first." >&2
  exit 1
fi

ros2 pkg prefix foxglove_msgs >/dev/null

schemas=(
  foxglove_msgs/msg/FrameTransform
  foxglove_msgs/msg/SceneUpdate
  foxglove_msgs/msg/CompressedImage
  foxglove_msgs/msg/CameraCalibration
  foxglove_msgs/msg/LaserScan
  foxglove_msgs/msg/PointCloud
  foxglove_msgs/msg/CompressedPointCloud
)

for schema in "${schemas[@]}"; do
  ros2 interface show "$schema" >/dev/null
done

echo "Preflight passed for sample schemas."
echo "For optional 41-interface diagnostics, run the Unity Manager ROS2 Bridge Health check."
echo
echo "Launch command:"
echo "ros2 launch unity2foxglove_ros2_bridge unity2foxglove_bridge.launch.py host:=$HOST port:=$PORT payload_format:=$PAYLOAD_FORMAT"

if [[ "$RUN" == "1" ]]; then
  exec ros2 launch unity2foxglove_ros2_bridge unity2foxglove_bridge.launch.py \
    "host:=$HOST" \
    "port:=$PORT" \
    "payload_format:=$PAYLOAD_FORMAT"
fi
