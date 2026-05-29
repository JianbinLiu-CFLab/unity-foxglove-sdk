# Virtual LiDAR PointCloud2 Digital Twin (ROS2)

Optional ROS2 For Unity adapter sample that mirrors a VirtualLidar component's
PointCloudFrame output to the ROS2 topic `/points` as `sensor_msgs/msg/PointCloud2`.

## Prerequisites

- `dev.unity2foxglove.ros2forunity` package installed
- `dev.unity2foxglove.ros2forunity.runtime.jazzy.win64` package installed
- `UNITY2FOXGLOVE_ROS2_FOR_UNITY` scripting define added to Project Settings

## Setup

1. Add `VirtualLidar` to a GameObject (see SDK core maze demo sample).
2. Add `Phase138VirtualLidarPointCloud2Smoke` to the same GameObject.
3. Assign the VirtualLidar component reference in the Inspector, or leave
   empty for auto-resolution via GetComponent.
4. Press Play. The component mirrors the most recent VirtualLidar frame to
   ROS2 `/points` at the configured publish interval.

## Default Configuration

- Node: `phase138_virtual_lidar`
- Topic: `/points`
- Publish interval: 0.1 s (10 Hz)

## Important Note

This sample requires `VirtualLidar.LastFrame` to be a public property.
If the field is not yet exposed, add the following to VirtualLidar.cs:

```csharp
public PointCloudFrame LastFrame { get; private set; }
```

Then set it in the scan method after building the frame.
