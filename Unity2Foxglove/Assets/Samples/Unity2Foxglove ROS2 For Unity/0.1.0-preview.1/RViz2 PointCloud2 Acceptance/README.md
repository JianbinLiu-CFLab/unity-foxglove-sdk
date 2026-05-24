# RViz2 PointCloud2 Acceptance

This source-only sample publishes a generic unorganized point cloud through the optional ROS2 For Unity route. It uses Unity2Foxglove `PointCloudFrame` data, packs it with `PointCloudPackedDataBuilder`, maps the packed fields to `sensor_msgs/msg/PointCloud2`, and publishes the result on `/points`.

It also publishes `/tf` so RViz2 can use `map` as the fixed frame:

```text
map -> base_link -> point_cloud_sensor
```

The sample is generic and not vendor-specific. It does not add organized clouds, PointCloud2 subscription, LiDAR vendor presets, MarkerArray, Camera/Image, MCAP replay fanout, rosbag2, or any core SDK ROS2 dependency.

## Requirements

- A Unity project with `dev.unity2foxglove.ros2forunity`.
- A ROS2 For Unity runtime package or external ROS2 For Unity import.
- The compile symbol `UNITY2FOXGLOVE_ROS2_FOR_UNITY`.
- Windows ROS2 Jazzy at `C:\ros2_jazzy\ros2-windows` for the canonical helper, or an already sourced ROS2/RViz2 environment for secondary manual checks.

## Unity Setup

1. In Package Manager, import the `RViz2 PointCloud2 Acceptance` sample.
2. Add `UNITY2FOXGLOVE_ROS2_FOR_UNITY` if the runtime package did not add it automatically.
3. Add `Phase129Rviz2PointCloud2Smoke` to a scene object.
4. Enter Play Mode and wait for the component status to report ready and publish counts to increase.
5. Record runtime root, runtime root package flag, asset runtime flag, point count, point step, and row step in the evidence template.

The default topic is `/points` because that is the common RViz2 convention for point cloud displays. The message type is `sensor_msgs/msg/PointCloud2` because it is the ROS2 standard point cloud message RViz2 understands directly.

The v1 payload is an animated synthetic wave with 1000 points, 50 columns, 0.08 m spacing, and 0.35 m wave height. It remains unorganized:

```text
height = 1
width = 1000
```

## Windows Acceptance Helper

Run Unity first, then execute this canonical command from the repository root:

```text
python Scripts\smoke\phase129_pointcloud2_acceptance.py --ros2-root C:\ros2_jazzy\ros2-windows --rviz-config "Packages\dev.unity2foxglove.ros2forunity\Samples~\RViz2 PointCloud2 Acceptance\rviz2_phase129_pointcloud2.rviz" --launch-rviz
```

The same command split across lines:

```powershell
python Scripts\smoke\phase129_pointcloud2_acceptance.py `
  --ros2-root C:\ros2_jazzy\ros2-windows `
  --rviz-config "Packages\dev.unity2foxglove.ros2forunity\Samples~\RViz2 PointCloud2 Acceptance\rviz2_phase129_pointcloud2.rviz" `
  --launch-rviz
```

The helper uses:

```text
<ros2-root>\.pixi\envs\default\python.exe <ros2-root>\Scripts\ros2-script.py
```

It checks the `unity2foxglove_phase129_pointcloud2` node, publisher endpoints for `/tf` and `/points`, one TF echo, and one PointCloud2 echo. Add `--launch-rviz` to open RViz2 with the included config after the CLI checks pass. Add `--rmw rmw_cyclonedds_cpp` if your Unity/R2FU runtime is using Cyclone DDS instead of the default Fast DDS setting.
Leave `ROS_AUTOMATIC_DISCOVERY_RANGE` unset for the canonical same-machine acceptance path unless you are deliberately debugging discovery behavior.

When `--launch-rviz` is used, the helper launches direct `rviz2.exe`, adds the required RViz2/Ogre/gz_math DLL directories, and passes the config path safely even when the workspace path contains spaces. RViz2 can still open slowly on Windows during cold starts, Defender scanning, or concurrent ROS2/colcon builds; the helper's `GREEN` line proves the ROS2 `/tf` and `/points` data path before the manual RViz2 visual check.

## Secondary Manual Commands

Use these only after the ROS2 environment is already sourced and known-good. On Windows, keep using `<ros2-root>\.pixi\envs\default\python.exe <ros2-root>\Scripts\ros2-script.py` rather than a bare launcher.

```text
$ ros2 topic info -v /tf
$ ros2 topic info -v /points
$ ros2 topic echo --once /tf tf2_msgs/msg/TFMessage
$ ros2 topic echo --once /points sensor_msgs/msg/PointCloud2
$ python Scripts\smoke\phase129_pointcloud2_acceptance.py --launch-rviz
```

## PASS Criteria

- `/tf` and `/points` each have a publisher endpoint from `unity2foxglove_phase129_pointcloud2`.
- `/tf` echo contains `map`, `base_link`, and `point_cloud_sensor`.
- `/points` echo contains `frame_id: point_cloud_sensor`, `height: 1`, `width: 1000`, non-empty fields, and non-empty data.
- RViz2 shows the TF tree and receives the PointCloud2 display without persistent fixed-frame, missing-transform, stale-transform, or extrapolation errors.
