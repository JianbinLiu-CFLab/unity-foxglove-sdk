# RViz2 Standard Visualization v1

This docs/config/evidence kit consolidates the source-only ROS2 For Unity RViz2 acceptance workflow for the v1 topic set:

```text
/tf
/scan
/points
/markers
```

It is not a publisher sample by itself. Import the three publisher samples first:

```text
RViz2 Standard Visualization Acceptance
RViz2 PointCloud2 Acceptance
RViz2 MarkerArray Acceptance
```

Then import this kit if you want the combined RViz2 config and final checklist.

## Scope

The v1 workflow is sample-driven. It covers `tf2_msgs/msg/TFMessage`, `sensor_msgs/msg/LaserScan`, generic unorganized `sensor_msgs/msg/PointCloud2`, and cube-only `visualization_msgs/msg/MarkerArray`.

It does not add CameraInfo, raw Image, CompressedImage, IMU, Odometry, PoseStamped, NavSatFix, services, actions, MCAP replay fanout, rosbag2 interop, or a global Inspector mapping UI. Normal Foxglove WebSocket, MCAP, Replay, FoxRun, and `foxglove_msgs` ROS2 bridge workflows remain separate from this optional ROS2 For Unity path. The core Unity2Foxglove SDK remains ROS-free.

## Requirements

- A Unity project with `dev.unity2foxglove.ros2forunity`.
- A ROS2 For Unity runtime package or external ROS2 For Unity import.
- The compile symbol `UNITY2FOXGLOVE_ROS2_FOR_UNITY`.
- Windows ROS2 Jazzy at `C:\ros2_jazzy\ros2-windows` for the canonical helper.

## Unity Setup

1. Import the three publisher samples listed above.
2. Add `UNITY2FOXGLOVE_ROS2_FOR_UNITY` if the runtime package did not add it automatically.
3. Add `Phase128Rviz2TfLaserScanSmoke`, `Phase129Rviz2PointCloud2Smoke`, and `Phase130Rviz2MarkerArraySmoke` to scene objects.
4. Configure a single owner for each TF edge. In particular, avoid conflicting `map -> base_link` publishers. Preferred setup: let `Phase128Rviz2TfLaserScanSmoke` own `map -> base_link -> laser`, and turn off `Publish Shared Base Tf` on `Phase129Rviz2PointCloud2Smoke` so it only publishes `base_link -> point_cloud_sensor`.
5. Enter Play Mode and wait for all publish counts to increase.
6. Record runtime root, runtime package flag, topic observations, RViz2 screenshots, and final verdict in the evidence template.

## Windows Acceptance Helper

Run Unity first, then execute this canonical command from the repository root:

```text
python Scripts\smoke\phase131_standard_visualization_acceptance.py --ros2-root C:\ros2_jazzy\ros2-windows --rviz-config "Packages\dev.unity2foxglove.ros2forunity\Samples~\RViz2 Standard Visualization v1\rviz2_phase131_standard_visualization.rviz" --launch-rviz
```

The helper uses:

```text
<ros2-root>\.pixi\envs\default\python.exe <ros2-root>\Scripts\ros2-script.py
```

It checks publisher endpoints and bounded echoes for `/tf`, `/scan`, `/points`, and `/markers`. Add `--rmw rmw_cyclonedds_cpp` if your Unity/R2FU runtime is using Cyclone DDS instead of the default Fast DDS setting. Leave `ROS_AUTOMATIC_DISCOVERY_RANGE` unset for canonical same-machine acceptance unless deliberately debugging discovery.

RViz2 is launched directly through `<ros2-root>\bin\rviz2.exe` with RViz2/Ogre/gz_math DLL directories added to `PATH`. This avoids treating a bare `ros2` launcher as the primary Windows path.

## Secondary Diagnostics

Use bare ROS2 commands only after the ROS2 environment is already known-good:

```text
$ ros2 topic info /tf
$ ros2 topic info /scan
$ ros2 topic info /points
$ ros2 topic info /markers
$ ros2 topic echo --once /tf
$ ros2 topic echo --once /scan
$ ros2 topic echo --once /points
$ ros2 topic echo --once /markers
```

## RViz2 Checks

- Fixed Frame is `map`.
- TF shows `map`, `base_link`, `laser`, and `point_cloud_sensor`.
- LaserScan receives `/scan`.
- PointCloud2 receives `/points`.
- MarkerArray receives `/markers`.
- There are no persistent fixed-frame, missing-transform, stale-transform, or extrapolation errors.

## PASS Criteria

- Automated offline validation passes.
- The helper reports `GREEN`.
- RViz2 displays TF, LaserScan, PointCloud2, and MarkerArray.
- Evidence records commit hash, package version, Unity version, ROS2 distro, RMW implementation, ROS2 For Unity source, screenshots, and final verdict.

This kit does not bump the package version or create a release tag by itself. Versioning and release tags remain part of the separate release process.
