# BasicVisualization Sample

The minimal low-dependency sample for quickly verifying core SDK publish chain: Transform, SceneUpdate, and Camera from Unity to Foxglove.

## Purpose

Use this sample to verify the SDK inside an arbitrary Unity project after importing it through Package Manager.

## What this sample includes

- `BasicVisualization.unity` — ready-to-run scene with FoxgloveManager, Cube, and Camera
- `FoxgloveSimpleLayout.json` — Foxglove Desktop layout with 3D, Image, Plot, and RawMessages panels

## What this sample does NOT include

- Parameters / Services
- FoxRun (`[FoxRun]` attribute + source generation)
- MCAP recording/replay
- Input System or URP dependencies

For the full demo experience, import `Full Demo Visualization` instead.

## Importing this sample into your project

1. After installing the `dev.unity2foxglove.sdk` package via Unity Package Manager, open **Window > Package Manager**
2. Find **Unity2Foxglove SDK** in the package list
3. Expand the **Samples** dropdown
4. Click **Import** next to **Basic Visualization**
5. After import, the sample files appear under `Assets/Samples/Unity2Foxglove SDK/<version>/Basic Visualization/Scenes/BasicVisualization.unity`

## Setup

1. Open `Assets/Samples/Unity2Foxglove SDK/<version>/Basic Visualization/Scenes/BasicVisualization.unity`
2. Press Play

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
