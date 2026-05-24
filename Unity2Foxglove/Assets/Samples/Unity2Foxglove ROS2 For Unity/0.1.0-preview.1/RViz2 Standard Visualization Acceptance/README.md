# RViz2 Standard Visualization Acceptance

This source-only sample verifies the optional ROS2 For Unity route with standard ROS2 visualization messages. It publishes `tf2_msgs/msg/TFMessage` on `/tf` and `sensor_msgs/msg/LaserScan` on `/scan` for RViz2.

It is intentionally narrow. It does not add PointCloud2, MarkerArray, Camera/Image, MCAP replay fanout, rosbag2, or any core SDK ROS2 dependency.

## Requirements

- A Unity project with `dev.unity2foxglove.ros2forunity`.
- A ROS2 For Unity runtime package or external ROS2 For Unity import.
- The compile symbol `UNITY2FOXGLOVE_ROS2_FOR_UNITY`.
- Windows ROS2 Jazzy at `C:\ros2_jazzy\ros2-windows` for the canonical helper, or an already sourced ROS2/RViz2 environment for secondary manual checks.

## Unity Setup

1. In Package Manager, import the `RViz2 Standard Visualization Acceptance` sample.
2. Add `UNITY2FOXGLOVE_ROS2_FOR_UNITY` if the runtime package did not add it automatically.
3. Add `Phase128Rviz2TfLaserScanSmoke` to a scene object.
4. Enter Play Mode and wait for the component status to report ready and publish counts to increase.
5. Record the runtime root, runtime root package flag, and whether `Assets/Ros2ForUnity` exists in the evidence template.

The sample publishes this frame tree:

```text
map -> base_link -> laser
```

RViz2 should use `map` as the fixed frame.

## Windows Acceptance Helper

Run Unity first, then execute:

```powershell
python Scripts\smoke\phase128_rviz2_acceptance.py `
  --ros2-root C:\ros2_jazzy\ros2-windows `
  --rviz-config "Packages\dev.unity2foxglove.ros2forunity\Samples~\RViz2 Standard Visualization Acceptance\rviz2_phase128_tf_laserscan.rviz"
```

The helper uses:

```text
<ros2-root>\.pixi\envs\default\python.exe <ros2-root>\Scripts\ros2-script.py
```

It checks the `unity2foxglove_phase128_rviz2` node, publisher endpoints for `/tf` and `/scan`, one TF echo, and one LaserScan echo. Add `--launch-rviz` to open RViz2 with the included config after the CLI checks pass. Add `--rmw rmw_cyclonedds_cpp` if your Unity/R2FU runtime is using Cyclone DDS instead of the default Fast DDS setting.
Leave `ROS_AUTOMATIC_DISCOVERY_RANGE` unset for the canonical same-machine acceptance path unless you are deliberately debugging discovery behavior.

To launch only RViz2 after Unity is already publishing, use the Python launcher:

```text
python Scripts\smoke\launch_phase128_rviz2.py
```

The launcher uses direct `rviz2.exe`, adds the required RViz2/Ogre/gz_math DLL directories, and passes the config path safely even when the workspace path contains spaces.

## Secondary Manual Commands

Use these only after the ROS2 environment is already sourced and known-good. On Windows, keep using `<ros2-root>\.pixi\envs\default\python.exe <ros2-root>\Scripts\ros2-script.py` rather than a bare launcher.

```text
$ ros2 topic info -v /tf
$ ros2 topic info -v /scan
$ ros2 topic echo --once /tf tf2_msgs/msg/TFMessage
$ ros2 topic echo --once /scan sensor_msgs/msg/LaserScan
$ python Scripts\smoke\launch_phase128_rviz2.py
```

## PASS Criteria

- `/tf` and `/scan` each have a publisher endpoint from `unity2foxglove_phase128_rviz2`.
- `/tf` echo contains `map`, `base_link`, and `laser`.
- `/scan` echo contains `frame_id: laser` and non-empty finite ranges.
- RViz2 shows the TF tree and receives the LaserScan display without persistent fixed-frame, missing-transform, stale-transform, or extrapolation errors.
