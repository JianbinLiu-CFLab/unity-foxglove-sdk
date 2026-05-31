# Virtual LiDAR Maze Demo

Demonstrates the Virtual LiDAR sensor driving through a procedurally generated maze,
published to Foxglove with a proper
`map -> base_link -> os_sensor -> os_lidar/os_imu` TF tree.

## Setup (recommended: pre-generated scene)

1. Import this sample into your Unity project.
2. Open or create the scene you want to build into.
3. Menu: **Foxglove ▸ Phase138 ▸ Build Maze Demo Scene**.
   This bakes the maze, a primitive car with a roof LiDAR/IMU unit, the TF publishers,
   a `FoxgloveManager` (RightHand mode), and an overview camera as real,
   inspectable GameObjects. Tweak anything in the Inspector, then save the scene.
4. Press Play and drive with **WASD**.

Alternatively, add `Phase138MazeDemoBootstrap` to an empty GameObject and press
Play to build the same scene at runtime.

## Foxglove

- Open the 3D panel and set **Display frame** to `map`.
- The car drives through the static maze; raise the point cloud **Decay time**
  (e.g. 3 s) to accumulate the scanned walls into a map.

## Coordinate system

The `FoxgloveManager` is set to **RightHand**, so transforms and the point cloud
share the same handedness (Unity left-hand ➜ Foxglove/ROS right-hand: X forward,
Y left, Z up). Leaving it in LeftHand mode publishes TF in Unity axes while the
LiDAR cloud stays in ROS axes, which is what makes the cloud look rotated.

## Sensor unit profile

Select the `SensorUnitProfile` component on `Lidar-IMU-Unit` (`os_sensor`) and set
the LiDAR/IMU unit identity there:

- **BuiltInPreset** — pick an Ouster preset from the dropdown: OS-0 / OS-1 / OS-2 at
  32 / 64 / 128 rings (beams evenly spaced across each line's vertical FOV).
- **MetadataJson** — assign a sensor's real `metadata.json` TextAsset and set the
  matching **Metadata Mode** (e.g. `1024x10`). Use this for exact factory beam angles,
  e.g. a real Ouster OS-128.
- **Custom** — type the geometry directly: rings (`Pixels Per Column`), vertical FOV
  top/bottom degrees, columns per frame, scan rate, min range.

The same component owns the Ouster-style `lidar_to_sensor_transform` and
`imu_to_sensor_transform` values. `VirtualLidar` on `LidarMount` only controls
LiDAR scan behavior such as frame id, range, ray budget, and debug rays.

This covers spinning / semi-solid-state sensors (Ouster, Velodyne, Hesai). Livox-style
non-repetitive (rosette) scanning is not modelled by the ring/column geometry and is
out of scope for this profile.

## Controls

| Key | Action |
|-----|--------|
| W   | Move forward |
| S   | Move backward |
| A   | Turn left |
| D   | Turn right |

Set `_useAutoWander` on the vehicle controller for a hands-free demo.

## Performance

- The generated 138I stress scene uses `OS-2-128`, `2048x10`, `columnStep=1`,
  no ray cap, a 262144 point budget, and a 10Hz point-cloud publisher.
- Draco output is the compressed visualization path and runs native encode work on
  a worker thread. Raw/ROS2 PointCloud2 validation remains the full-stride path for
  SLAM fields such as `ring`, `time_offset`, and absolute-ns `t`.

## Limitations

- Desktop only (requires standalone Unity Player).
- WASD works with both the new Input System and the legacy Input Manager.
- Auto-wander uses a simple wall-contact rotation -- no pathfinding.

## Output Topics

- Point cloud: `/unity/point_cloud`
- Transforms: `/tf`
