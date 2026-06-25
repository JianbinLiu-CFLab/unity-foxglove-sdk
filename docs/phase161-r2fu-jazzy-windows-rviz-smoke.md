# Phase161 Jazzy R2FU Windows RViz Smoke Notes

This note records the Phase161 Jazzy runtime refresh pitfalls and the
validated Windows RViz2 smoke path for the Phase138U raw and deskewed
PointCloud2 workflow.

## Scope

- Unity sample scene: `Phase138_Foxglove_MCAP_Smoke`
- Unity output mode: `ROS2 Native (R2FU)`
- R2FU runtime: `Jazzy Win64`
- Point cloud output: `PointCloud2 Native`
- Raw topic: `/unity/point_cloud2`
- Deskewed topic: `/unity/point_cloud2_deskewed`
- Point frame: `os_lidar`
- RViz fixed frame: `map`

## Pitfalls Found

1. The initial hang was not a PointCloud2 QoS issue.

   Unity froze while ROS2 For Unity was being initialized from shutdown or
   callback paths. The fix was to keep native bridge callbacks from lazily
   creating the ROS2 runtime and to guard prewarm during unstable Play Mode
   transitions.

2. The refreshed Jazzy package missed a FastRTPS dependency.

   `rmw_fastrtps_cpp.dll` required `rosidl_dynamic_typesupport_fastrtps.dll`.
   Without it, Jazzy native initialization could freeze or fail during Play
   Mode. The runtime package refresh now includes and validates that DLL.

3. Phase138U intentionally keeps the point-cloud TF anchor disabled.

   In the maze scene, the point cloud publisher should log `tf=disabled`.
   That is expected because the scene owns the real TF chain:

   ```text
   map -> base_link -> os_sensor -> os_lidar
   ```

   Enabling the point-cloud fallback anchor in this scene can create a
   conflicting direct `map -> os_lidar` edge.

4. RViz `Frame [map] does not exist` can be a Windows FastDDS transport problem.

   During the Phase161 Jazzy verification, Unity was publishing `/tf` and both
   point-cloud topics, but RViz started with the default FastDDS transport path
   failed to resolve the `map` frame. A direct rclpy subscriber using UDPv4
   received the complete TF chain and both point-cloud topics.

5. `ros2 topic list` is a weak signal on this Windows/FastDDS path.

   The graph probe may time out or show only `/parameter_events` and `/rosout`
   even when a direct subscription can receive `/tf` or PointCloud2 samples.
   Treat direct bounded subscription evidence as stronger than `topic list` or
   short `topic hz` output.

## Stabilized Smoke Environment

The shared Windows ROS2 smoke helper now sets this environment for
`rmw_fastrtps_cpp` on Windows:

```text
FASTDDS_BUILTIN_TRANSPORTS=UDPv4
```

The Phase138U launch and acceptance helpers print the active RMW, discovery
range, and FastDDS transport value so a run log can prove whether the stabilized
path was used.

## Validation Steps

1. Start Unity Play Mode with Jazzy R2FU native output enabled.

2. Confirm Unity logs show native readiness:

   ```text
   ROS2 version: jazzy. Build type: standalone. RMW: rmw_fastrtps_cpp
   [Foxglove][R2FU] Transform DDS ready: topic=/tf source=/tf.
   [Foxglove][R2FU] Transform DDS ready: topic=/tf source=/tf_lidar.
   [Foxglove][R2FU] Transform DDS ready: topic=/tf source=/tf_sensor.
   [Foxglove][R2FU] PointCloud2 Native DDS ready: topic=/unity/point_cloud2 tf=disabled.
   [Foxglove][R2FU] PointCloud2 Native DDS ready: topic=/unity/point_cloud2_deskewed tf=disabled.
   ```

3. Run the DDS-only acceptance helper:

   ```powershell
   python Scripts/smoke/ros2/phase138u_lidar_deskew_rviz2_acceptance.py --no-rviz --skip-topic-probe --allow-static --no-require-wall-improvement --spin-seconds 8 --no-print-json
   ```

4. Confirm the helper reports:

   ```text
   RMW=rmw_fastrtps_cpp discovery=SUBNET fastdds_transports=UDPv4
   PASS (DDS-WIRING-ONLY: static capture accepted)
   ```

5. Launch RViz2 through the Phase138U helper:

   ```powershell
   python Scripts/smoke/ros2/launch_phase138u_lidar_deskew_rviz2.py --skip-topic-probe
   ```

6. Confirm RViz2 shows:

   ```text
   Fixed Frame: map
   Global Status: Ok
   PointCloud2 visible on the raw and/or deskewed displays
   ```

## Phase161 Results

- Runtime package validation passed after the Jazzy dependency repair.
- Phase161 runtime validation passed after the lifecycle and dependency fixes.
- Direct rclpy probing received:
  - TF edges: `map->base_link`, `base_link->os_sensor`,
    `os_sensor->os_lidar`, `os_sensor->os_imu`
  - Raw PointCloud2: `/unity/point_cloud2`, frame `os_lidar`
  - Deskewed PointCloud2: `/unity/point_cloud2_deskewed`, frame `os_lidar`
- The Phase138U DDS-only helper passed with UDPv4 FastDDS transport.
- Manual RViz2 acceptance passed with `Fixed Frame: map` and `Global Status:
  Ok`.

