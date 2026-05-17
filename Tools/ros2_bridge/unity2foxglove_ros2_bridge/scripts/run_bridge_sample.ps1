# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

[CmdletBinding()]
param(
    [string]$HostName = "127.0.0.1",
    [int]$Port = 8767,
    [string]$PayloadFormat = "cdr-with-encapsulation",
    [switch]$Run
)

$ErrorActionPreference = "Stop"

Write-Host "Unity2Foxglove ROS2 Bridge sample preflight"
Write-Host ("ROS_DISTRO={0}" -f ($(if ($env:ROS_DISTRO) { $env:ROS_DISTRO } else { "<not sourced>" })))

if (-not (Get-Command ros2 -ErrorAction SilentlyContinue)) {
    throw "ros2 was not found. Source your ROS2 environment before running this script."
}

& ros2 pkg prefix foxglove_msgs | Out-Null

$schemas = @(
    "foxglove_msgs/msg/FrameTransform",
    "foxglove_msgs/msg/SceneUpdate",
    "foxglove_msgs/msg/CompressedImage",
    "foxglove_msgs/msg/CameraCalibration",
    "foxglove_msgs/msg/LaserScan",
    "foxglove_msgs/msg/PointCloud",
    "foxglove_msgs/msg/CompressedPointCloud"
)

foreach ($schema in $schemas) {
    & ros2 interface show $schema | Out-Null
}

Write-Host "Preflight passed for sample schemas."
Write-Host "For full 41-interface validation, run the Unity Manager ROS2 Bridge Health check."
Write-Host ""
Write-Host "Launch command:"
Write-Host "ros2 launch unity2foxglove_ros2_bridge unity2foxglove_bridge.launch.py host:=$HostName port:=$Port payload_format:=$PayloadFormat"

if ($Run) {
    & ros2 launch unity2foxglove_ros2_bridge unity2foxglove_bridge.launch.py "host:=$HostName" "port:=$Port" "payload_format:=$PayloadFormat"
}
