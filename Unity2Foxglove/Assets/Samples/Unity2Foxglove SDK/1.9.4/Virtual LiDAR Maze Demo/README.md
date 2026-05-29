# Virtual LiDAR Maze Demo

Demonstrates the Virtual LiDAR sensor driving through a procedurally generated maze,
published to Foxglove with a proper `map -> base_link -> vehicle_lidar` TF tree.

## Setup (recommended: pre-generated scene)

1. Import this sample into your Unity project.
2. Open or create the scene you want to build into.
3. Menu: **Foxglove ▸ Phase138 ▸ Build Maze Demo Scene**.
   This bakes the maze, a primitive car with a roof LiDAR, the TF publishers,
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

## LiDAR sensor model

Select the `VirtualLidar` component (on the car's `LidarMount`) and set **Profile Source**:

- **BuiltInPreset** — pick an Ouster preset from the dropdown: OS-0 / OS-1 / OS-2 at
  32 / 64 / 128 rings (beams evenly spaced across each line's vertical FOV).
- **MetadataJson** — assign a sensor's real `metadata.json` TextAsset and set the
  matching **Metadata Mode** (e.g. `1024x10`). Use this for exact factory beam angles,
  e.g. a real Ouster OS-128.
- **Custom** — type the geometry directly: rings (`Pixels Per Column`), vertical FOV
  top/bottom degrees, columns per frame, scan rate, min range.

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

- `columnStep=4` (default) balances quality and throughput.
- Set `columnStep=1` on the VirtualLidar for a stress test (every laser column fired).

## Limitations

- Desktop only (requires standalone Unity Player).
- WASD works with both the new Input System and the legacy Input Manager.
- Auto-wander uses a simple wall-contact rotation -- no pathfinding.

## Output Topics

- Point cloud: `/unity/point_cloud`
- Transforms: `/tf`
