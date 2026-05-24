# RViz2 MarkerArray Acceptance

This source-only sample publishes one animated cube scene marker through the optional ROS2 For Unity route. It maps deterministic Unity marker state to `visualization_msgs/msg/MarkerArray` and publishes it on `/markers`.

The v1 frame is fixed directly in `map`. The sample does not publish `/tf` because the marker header frame is already the RViz2 fixed frame:

```text
frame_id = map
topic = /markers
type = visualization_msgs/msg/MarkerArray
```

The sample is intentionally narrow. It does not add arbitrary marker types, mesh resources, text markers, interactive markers, PointCloud2, Camera/Image, MCAP replay fanout, rosbag2, or any core SDK ROS2 dependency.

## Requirements

- A Unity project with `dev.unity2foxglove.ros2forunity`.
- A ROS2 For Unity runtime package or external ROS2 For Unity import.
- The compile symbol `UNITY2FOXGLOVE_ROS2_FOR_UNITY`.
- Windows ROS2 Jazzy at `C:\ros2_jazzy\ros2-windows` for the canonical helper, or an already sourced ROS2/RViz2 environment for secondary manual checks.

## Unity Setup

1. In Package Manager, import the `RViz2 MarkerArray Acceptance` sample.
2. Add `UNITY2FOXGLOVE_ROS2_FOR_UNITY` if the runtime package did not add it automatically.
3. Add `Phase130Rviz2MarkerArraySmoke` to a scene object.
4. Enter Play Mode and wait for the component status to report ready and publish counts to increase.
5. Record runtime root, runtime root package flag, asset runtime flag, marker action, marker id, active marker count, and publish count in the evidence template.

The default topic is `/markers` because that is the common RViz2 convention for marker displays. The message type is `visualization_msgs/msg/MarkerArray` because RViz2 understands it directly.

Marker IDs are deterministic positive 31-bit values produced from stable marker names with FNV-1a. `DELETE` uses the same namespace and ID as the previous `ADD`, and the sample also emits `DELETEALL` periodically to prove cleanup behavior. Marker lifetime is zero, which means the marker persists until another marker message deletes or replaces it.

## Windows Acceptance Helper

Run Unity first, then execute this canonical command from the repository root:

```text
python Scripts\smoke\phase130_markerarray_acceptance.py --ros2-root C:\ros2_jazzy\ros2-windows --rviz-config "Packages\dev.unity2foxglove.ros2forunity\Samples~\RViz2 MarkerArray Acceptance\rviz2_phase130_markerarray.rviz" --launch-rviz
```

The helper uses:

```text
<ros2-root>\.pixi\envs\default\python.exe <ros2-root>\Scripts\ros2-script.py
```

It checks the `unity2foxglove_phase130_markerarray` node, the `/markers` publisher endpoint, and one bounded MarkerArray echo. Add `--launch-rviz` to open RViz2 with the included config after the CLI checks pass. Add `--rmw rmw_cyclonedds_cpp` if your Unity/R2FU runtime is using Cyclone DDS instead of the default Fast DDS setting.
Leave `ROS_AUTOMATIC_DISCOVERY_RANGE` unset for the canonical same-machine acceptance path unless you are deliberately debugging discovery behavior.

When `--launch-rviz` is used, the helper launches direct `rviz2.exe`, adds the required RViz2/Ogre/gz_math DLL directories, and passes the config path safely even when the workspace path contains spaces. RViz2 can still open slowly on Windows during cold starts, Defender scanning, or concurrent ROS2/colcon builds; the helper's `GREEN` line proves the ROS2 `/markers` data path before the manual RViz2 visual check.

## Secondary Manual Commands

Use these only after the ROS2 environment is already sourced and known-good. On Windows, keep using `<ros2-root>\.pixi\envs\default\python.exe <ros2-root>\Scripts\ros2-script.py` rather than a bare launcher.

```text
$ ros2 topic info -v /markers
$ ros2 topic echo --once /markers visualization_msgs/msg/MarkerArray
$ python Scripts\smoke\phase130_markerarray_acceptance.py --launch-rviz
```

## PASS Criteria

- `/markers` has a publisher endpoint from `unity2foxglove_phase130_markerarray`.
- `/markers` echo contains `frame_id: map`, namespace `unity2foxglove`, type `CUBE`, and zero marker lifetime.
- RViz2 shows the Grid and MarkerArray display without persistent fixed-frame or stale marker errors.
