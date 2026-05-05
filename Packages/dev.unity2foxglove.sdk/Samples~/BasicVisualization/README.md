# Basic Visualization Sample

Minimal low-dependency sample for verifying core publish chain: Transform, SceneUpdate, and Camera from Unity to Foxglove.

## What this sample includes

- `BasicVisualization.unity` — ready-to-run scene with FoxgloveManager, Cube, and Camera
- `FoxgloveSimpleLayout.json` — Foxglove Desktop layout with 3D, Image, Plot, and RawMessages panels

## What this sample does NOT include

- Parameters / Services
- FoxRun (`[FoxRun]` attribute + source generation)
- MCAP recording/replay
- Input System or URP dependencies

For the full demo experience (Parameters, Services, FoxRun, MCAP, playback), import `Full Demo Visualization` instead.

## Setup

1. Install this package into a Unity project
2. Import `Basic Visualization` from Package Manager
3. Open `Assets/Samples/Unity2Foxglove SDK/<version>/Basic Visualization/Scenes/BasicVisualization.unity`
4. Press Play

## Foxglove Connection

1. Open Foxglove Desktop
2. "Open connection" -> Foxglove WebSocket -> `ws://127.0.0.1:8765`
3. Import `FoxgloveSimpleLayout.json` (optional)
4. Topics: `/tf`, `/scene`, `/unity/camera`
5. 3D panel: select `/scene` to see the cube
6. Image panel: select `/unity/camera` to see the camera feed

## Component Reference

| Component | Topic | Schema | Default Rate |
|-----------|-------|--------|-------------|
| FoxgloveTransformPublisher | `/tf` | foxglove.FrameTransform | 10 Hz |
| FoxgloveSceneCubePublisher | `/scene` | foxglove.SceneUpdate | 10 Hz |
| FoxgloveCameraPublisher | `/unity/camera` | foxglove.CompressedImage | 10 Hz |

## Verification

- Foxglove connects `ws://127.0.0.1:8765`
- 3D panel shows `/scene` cube
- Image panel shows `/unity/camera` feed
- Plot panel shows `/tf.translation.*` curves

Tested on: Unity 6000.3 LTSC, Windows Editor.
