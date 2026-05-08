# Full Demo Visualization Sample

Complete demo sample mirroring the repository demo project. Includes Parameters, Services, FoxRun, MCAP recording/replay, and playback workflows.

## Prerequisites

- Unity 6000.0 LTSC or later (developed on 6000.3.14f1 LTSC; compatible with 6000.0.74f1 LTSC)
- Input System (`com.unity.inputsystem`)
- Universal Render Pipeline (`com.unity.render-pipelines.universal`)

If you don't want to install these dependencies, use `Basic Visualization` instead.

## Setup

1. Install this package into a Unity project
2. Install Input System and URP via Package Manager
3. Import `Full Demo Visualization` from Package Manager
4. Open `FullDemoVisualization.unity`
5. Press Play

## What this sample includes

- `FullDemoVisualization.unity` — full demo scene with all publishers
- `FoxgloveFullLayout.json` — Foxglove Desktop layout with 3D, Image, Plot, Parameters, Service Call, TopicGraph, Publish, and RawMessages panels
- `InputSystem_Actions.inputactions` — Input System bindings for mouse-driven cube control
- `Settings/` — URP pipeline assets

## Scene contents

| GameObject | Components | Purpose |
|-----------|------------|---------|
| Foxglove | FoxgloveManager | Server entry point |
| Cube | FoxgloveTransformPublisher, FoxgloveSceneCubePublisher, MouseDragCube | Visualized cube with mouse control |
| Main Camera | FoxgloveCameraPublisher | Camera image streaming |
| TestLog | TestLog (`[FoxRun]`) | Auto-published `/debug/position`, `/debug/health` |

## Foxglove Connection

1. Open Foxglove Desktop
2. "Open connection" -> Foxglove WebSocket -> `ws://127.0.0.1:8765`
3. Import `FoxgloveFullLayout.json`
4. Topics: `/tf`, `/scene`, `/unity/camera`, `/debug/position`, `/debug/health`

## Verification

- 3D panel: `/scene` cube visible
- Image panel: `/unity/camera` feed
- Plot panel: `/tf.translation.*` curves
- Parameters panel: modify `/cube/color` (RGBA array) -> cube color changes; modify `/cube/scale` -> cube size changes
- Service Call panel: call `/cube/reset_pose` with `{}` -> cube resets to origin
- RawMessages: `/debug/position`, `/debug/health` (auto-published by FoxRun)
- Mouse controls: left-drag rotate cube, right-drag pan cube, scroll scale cube

Tested on: Unity 6000.3.14f1 LTSC, Windows Editor. Compatible with Unity 6000.0.74f1 LTSC.
