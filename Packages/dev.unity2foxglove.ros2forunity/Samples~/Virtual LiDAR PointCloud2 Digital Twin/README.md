# Virtual LiDAR PointCloud2 Digital Twin (ROS2)

Optional ROS2 For Unity diagnostic sample for the Phase 138L PointCloud2 Native
handoff. It is not required for the product path.

For the product path, configure the scene directly:

1. On `FoxgloveManager`, enable `ROS2 Native (R2FU)`.
2. On `FoxglovePointCloudPublisher`, choose `PointCloud2 Native`.
3. Set topic `/points` and the desired frame id, for example `os_lidar`.
4. Leave `Publish PointCloud2 TF Anchor` disabled when another TF tree already
   owns that frame. Enable it only as an RViz fallback when no other `/tf`
   source resolves the PointCloud2 frame.

With Unity in Play mode, `/points` should then appear as
`sensor_msgs/msg/PointCloud2`. If the optional fallback anchor is enabled,
`/tf` should also carry the matching frame anchor, without adding this smoke
component.

## Prerequisites

- `dev.unity2foxglove.ros2forunity` package installed
- `dev.unity2foxglove.ros2forunity.runtime.jazzy.win64` package installed
- `UNITY2FOXGLOVE_ROS2_FOR_UNITY` scripting define added to Project Settings

## Optional Diagnostic Setup

Add `Phase138VirtualLidarPointCloud2Smoke` only when you intentionally want a
separate diagnostic harness that records publish-call timing and drop counters.
Do not mount it for normal product acceptance, because the runtime bridge in the
adapter package already publishes the configured `FoxglovePointCloudPublisher`
topic and TF anchor.

## Default Configuration

- Node base: `phase138_virtual_lidar` (the runtime node appends a unique suffix,
  shown as `Effective Node Name`, to avoid duplicate-node collisions)
- Topic: `/points`
- Publish interval: 0.1 s (10 Hz)
- Data copy before publish: disabled by default

## Important Note

The Phase 138L product path and this optional diagnostic sample do not read
`VirtualLidar.LastFrame.Points` and does not pack points on the Unity main
thread. The core SDK worker prepares the packed PointCloud2 data and raises a
schema-neutral handoff event.
