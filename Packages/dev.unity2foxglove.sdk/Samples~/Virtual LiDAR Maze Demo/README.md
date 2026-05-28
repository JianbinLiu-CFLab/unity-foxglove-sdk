# Virtual LiDAR Maze Demo

Demonstrates the Virtual LiDAR sensor in a procedurally generated maze. No prefabs or `.unity` scenes required -- everything is built at runtime using `GameObject.CreatePrimitive`.

## Setup

1. Import this sample into your Unity project.
2. Create an empty scene.
3. Add `Phase138MazeDemoBootstrap` to an empty GameObject.
4. Press Play.

## Controls

| Key | Action |
|-----|--------|
| W   | Move forward |
| S   | Move backward |
| A   | Turn left |
| D   | Turn right |

Check `_useAutoWander` on the vehicle controller for a hands-free demo.

## Performance

- `columnStep=4` (default) balances quality and throughput.
- Set `columnStep=1` on the VirtualLidar for a stress test (every laser column fired).

## Limitations

- Desktop only (requires standalone Unity Player).
- Legacy Input Manager must be active (WASD input).
- Auto-wander uses a simple wall-contact rotation -- no pathfinding.

## Output Topic

`/unity/maze_lidar/points`
