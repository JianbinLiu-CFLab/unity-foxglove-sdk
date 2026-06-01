# Phase 138 Virtual LiDAR ROS2 PointCloud2 Evidence

## Environment

- Unity: [version]
- ROS2 distro: [jazzy]
- RMW: [rmw_fastrtps_cpp]
- ROS2 For Unity runtime: [jazzy.win64]

## ROS2 Graph

```
ros2 topic info /points -v
ros2 topic hz /points
ros2 topic bw /points
ros2 topic echo --once /points
```

## Evidence

- FoxglovePointCloudPublisher output mode: PointCloud2 Native
- Phase138 component status:
- Published PointCloud2 count:
- Dropped frame count:
- Valid point count:
- Payload bytes:
- Point step / row step:
- Last IPublisher.Publish call ms:
- RViz2 screenshot: [attached]
- ros2 topic info output: [pasted]
- ros2 topic hz output: [pasted]
- ros2 topic bw output: [pasted]
- ros2 topic echo output: [pasted]
