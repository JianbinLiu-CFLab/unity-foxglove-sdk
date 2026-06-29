# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Preflight and optionally launch the ROS2 Bridge sample from PowerShell.

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

function Invoke-Ros2Checked {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Description,
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    $output = & ros2 @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw ("{0} failed with exit code {1}: ros2 {2}`n{3}" -f $Description, $LASTEXITCODE, ($Arguments -join " "), ($output -join "`n"))
    }
    return $output
}

Invoke-Ros2Checked -Description "foxglove_msgs package lookup" -Arguments @("pkg", "prefix", "foxglove_msgs")

$schemas = @(
    "foxglove_msgs/msg/FrameTransform",
    "foxglove_msgs/msg/SceneUpdate",
    "foxglove_msgs/msg/CompressedImage",
    "foxglove_msgs/msg/CameraCalibration",
    "foxglove_msgs/msg/LaserScan",
    "foxglove_msgs/msg/PointCloud",
    "foxglove_msgs/msg/CompressedPointCloud"
)

$interfaces = @(Invoke-Ros2Checked -Description "foxglove_msgs interface catalog lookup" -Arguments @("interface", "package", "foxglove_msgs"))
foreach ($schema in $schemas) {
    if (-not ($interfaces -ccontains $schema)) {
        throw "ROS2 interface check failed: $schema is not listed by foxglove_msgs."
    }
}

Write-Host "Preflight passed for sample schemas."
Write-Host "For optional 41-interface diagnostics, run the Unity Manager ROS2 Bridge Health check."
Write-Host ""
Write-Host "Launch command:"
Write-Host "ros2 launch unity2foxglove_ros2_bridge unity2foxglove_bridge.launch.py host:=$HostName port:=$Port payload_format:=$PayloadFormat"

if ($Run) {
    & ros2 launch unity2foxglove_ros2_bridge unity2foxglove_bridge.launch.py "host:=$HostName" "port:=$Port" "payload_format:=$PayloadFormat"
    if ($LASTEXITCODE -ne 0) {
        throw ("ROS2 Bridge launch failed with exit code {0}." -f $LASTEXITCODE)
    }
}
