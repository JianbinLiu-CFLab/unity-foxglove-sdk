# Virtual LiDAR Maze Demo

Demonstrates a cart-style sensor rig driving through a generated maze with a
shared `map -> base_link -> os_sensor -> os_lidar/os_imu/os_camera` frame tree.

## Setup

1. Import this sample into your Unity project.
2. Open or create the scene you want to build into.
3. Choose **Foxglove > LiDAR Maze Demo > Build Scene**.
4. Press Play and drive with **WASD**.

Alternatively, add the maze demo bootstrap component to an empty GameObject and
press Play to build the same scene at runtime.

## Product ROS2 Native Path

For SLAM consumers, use the normal product Inspector path:

- On `FoxgloveManager`, enable `ROS2 Native (R2FU)`.
- On `Lidar-IMU-Unit`, keep `FoxglovePointCloudPublisher` in `PointCloud2 Native`.
- The point cloud publishes on `/unity/point_cloud2` in frame `os_lidar`.
- `CartCameraMount` publishes compressed camera images on
  `/unity/sensor/camera/image/compressed` in frame `os_camera`.
- `CartCameraMount` publishes camera info on
  `/unity/sensor/camera/camera_info` in frame `os_camera`.
- Camera timestamps use the same shared sensor clock as LiDAR and IMU.
- CameraInfo can publish a `/tf` anchor from `os_sensor` to `os_camera`.

No diagnostic smoke component is required for the product path.

## Foxglove

- Open the 3D panel and set **Display frame** to `map`.
- The car drives through the static maze; raise the point cloud **Decay time**
  to accumulate scanned walls into a map.

## RViz2

Use `map` or `os_sensor` as the fixed frame when TF is visible. The image and
camera-info topics are intended for ROS2/RViz2 tools that consume standard camera
schemas:

```text
/unity/point_cloud2
/unity/sensor/camera/image/compressed
/unity/sensor/camera/camera_info
/imu/data
/tf
```

## Sensor Unit Profile

Select `SensorUnitProfile` on `Lidar-IMU-Unit` (`os_sensor`) to configure the
LiDAR/IMU/camera unit identity:

- `BuiltInPreset`: choose an Ouster preset.
- `MetadataJson`: assign an Ouster `metadata.json` TextAsset and matching mode.
- `Custom`: type scan geometry directly.

The same profile owns frame IDs, camera topics, and LiDAR/IMU/camera extrinsics.
`VirtualLidar` still controls scan behavior such as range, ray budget, and debug
rays.

## Controls

| Key | Action |
|-----|--------|
| W   | Move forward |
| S   | Move backward |
| A   | Turn left |
| D   | Turn right |

Set `_useAutoWander` on the vehicle controller for a hands-free demo.

## Performance

- The demo defaults to `OS-1-32`, `1024x10`, `columnStep=1`, a 32768 point
  budget, and a per-`FixedUpdate` raycast budget.
- `PointCloud2 Native` preserves SLAM fields such as `ring`, `time_offset`, and
  absolute-ns `t`.
- Camera image output uses the async camera pipeline and standard compressed
  image schema when ROS2 output is enabled.

## Limitations

- Desktop only.
- WASD works with both the new Input System and the legacy Input Manager.
- Auto-wander uses simple wall-contact rotation, not pathfinding.
