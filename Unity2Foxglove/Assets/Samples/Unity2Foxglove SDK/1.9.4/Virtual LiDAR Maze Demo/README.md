# Virtual LiDAR Maze Demo

Demonstrates a cart-style sensor rig driving through a generated maze with a
shared `map -> base_link -> os_sensor -> os_lidar/os_imu/os_camera` frame tree.

## Setup

1. Import this sample into your Unity project.
2. Open or create the scene you want to build into.
3. Choose **Foxglove > Phase138 > Build Maze Demo Scene**.
4. Press Play and drive with **WASD**.

Alternatively, add `Phase138MazeDemoBootstrap` to an empty GameObject and press
Play to build the same scene at runtime.

## Transport boundary

This SDK sample stays on the core Foxglove WebSocket path. Optional ROS2
delivery belongs to the companion transport packages; follow the selected
package's sample and upgrade guide instead of adding transport-specific fields
to this scene.

The inactive `CartCameraMount` demonstrates the shared sensor profile and
clock. Its camera and camera-info topics are
`/unity/sensor/camera/image/compressed` and
`/unity/sensor/camera/camera_info`.

## Foxglove

- Open the 3D panel and set **Display frame** to `map`.
- The car drives through the static maze; raise the point cloud **Decay time**
  to accumulate scanned walls into a map.

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
- Draco point clouds remain compact on the Foxglove WebSocket path.
- Camera image output uses the asynchronous core camera pipeline.

## Limitations

- Desktop only.
- WASD works with both the new Input System and the legacy Input Manager.
- Auto-wander uses simple wall-contact rotation, not pathfinding.
