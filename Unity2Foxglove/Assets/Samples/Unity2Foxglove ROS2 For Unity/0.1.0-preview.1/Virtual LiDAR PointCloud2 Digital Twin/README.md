# Virtual LiDAR PointCloud2 Digital Twin (ROS2)

Optional ROS2 For Unity adapter sample that mirrors a VirtualLidar component's
prepared PointCloud2 Native output to the ROS2 topic `/points` as
`sensor_msgs/msg/PointCloud2`.

## Prerequisites

- `dev.unity2foxglove.ros2forunity` package installed
- `dev.unity2foxglove.ros2forunity.runtime.jazzy.win64` package installed
- `UNITY2FOXGLOVE_ROS2_FOR_UNITY` scripting define added to Project Settings

## Setup

1. Configure the scene's `FoxglovePointCloudPublisher` output mode as
   `PointCloud2 Native`.
2. Add `Phase138VirtualLidarPointCloud2Smoke` to the same GameObject, or to a
   nearby object in the sensor hierarchy.
3. Assign the `FoxglovePointCloudPublisher` reference in the Inspector, or leave
   empty for auto-resolution.
4. Press Play. The component subscribes to prepared native frames and publishes
   ROS2 `/points` at the configured publish interval.

## Default Configuration

- Node: `phase138_virtual_lidar`
- Topic: `/points`
- Publish interval: 0.1 s (10 Hz)
- Data copy before publish: disabled by default

## Important Note

This sample is the Phase 138L native path. It does not read
`VirtualLidar.LastFrame.Points` and does not pack points on the Unity main
thread. The core SDK worker prepares the packed PointCloud2 data and raises a
schema-neutral handoff event; this sample only fills the generated ROS2 message
and measures the `IPublisher.Publish` call.
