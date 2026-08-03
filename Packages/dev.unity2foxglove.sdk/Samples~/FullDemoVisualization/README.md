# Full Demo Visualization Sample

## Purpose

Use this sample when you want the complete importable Unity2Foxglove demo inside your own Unity project.

It demonstrates the main package workflows together:

- live transform, scene, and camera streaming;
- Parameters;
- declarative `[FoxService]` Services;
- FoxRun auto-published debug topics;
- MCAP recording/replay settings;
- a preconfigured Foxglove Desktop layout.

## When to Use This Sample

Use **Full Demo Visualization** when:

- you want to evaluate the SDK as a package user;
- you want a scene that shows the main components working together;
- you want Parameters, Services, FoxRun, camera streaming, and MCAP controls in one place.

Use **Basic Visualization** instead when you only need a minimal `/tf`, `/scene`, and `/unity/camera` smoke test.

Use the repository `Unity2Foxglove/` project when you are contributing to the SDK or validating IL2CPP/release workflows.

## Requirements

- Unity 6000.0 LTSC or later.
- Universal Render Pipeline (`com.unity.render-pipelines.universal`).

This sample was developed on Unity 6000.3.14f1 LTSC and is intended to remain compatible with Unity 6000.0.74f1 LTSC or later.

## Import Steps

1. Install `dev.unity2foxglove.sdk` through Unity Package Manager.
2. Install URP if your project does not already include it.
3. Open **Window > Package Manager**.
4. Select **Unity2Foxglove SDK**.
5. Expand **Samples**.
6. Click **Import** next to **Full Demo Visualization**.

After import, Unity places the sample under:

```text
Assets/Samples/Unity2Foxglove SDK/<version>/Full Demo Visualization/
```

## What This Sample Includes

- `Scenes/FullDemoVisualization.unity`
- `FoxgloveFullLayout.json`
- `Scripts/FoxgloveDemoSetup.cs`
- `Scripts/MouseDragCube.cs`
- `Scripts/TestLog.cs`
- URP settings under `Settings/`

## Scene Contents

| GameObject | Components | Purpose |
|------------|------------|---------|
| Foxglove | `FoxgloveManager`, `FoxgloveDemoSetup` | Starts the server, registers parameters, exposes `/cube/reset_pose` with `[FoxService]`, and owns recording/replay settings |
| Cube | `FoxgloveTransformPublisher`, `FoxgloveSceneCubePublisher`, `MouseDragCube` | Publishes transform/scene data and supports mouse-driven interaction |
| Main Camera | `FoxgloveCameraPublisher` | Streams `/unity/camera` |
| TestLog | `TestLog` with `[FoxRun]` fields | Publishes the debug topics and owns the controlled `/phase185/messagepack/full-duplex` typed MessagePack acceptance contract |

## Run Steps

1. Open:

   ```text
   Assets/Samples/Unity2Foxglove SDK/<version>/Full Demo Visualization/Scenes/FullDemoVisualization.unity
   ```

2. Press Play in the Unity Editor.
3. Open Foxglove Desktop.
4. Open a Foxglove WebSocket connection to:

   ```text
   ws://127.0.0.1:8765
   ```

5. Import the full layout:

   ```text
   Assets/Samples/Unity2Foxglove SDK/<version>/Full Demo Visualization/FoxgloveFullLayout.json
   ```

## Expected Result

Foxglove should show:

| Area | Expected result |
|------|-----------------|
| Topics | `/tf`, `/scene`, `/unity/camera`, `/debug/position`, `/debug/position2`, `/debug/health` |
| 3D | The cube and transform frame update live |
| Image | The Unity camera stream appears on `/unity/camera` |
| Plot | `/tf.translation.*` values update as the cube moves |
| Parameters | `/cube/color` and `/cube/scale` are editable |
| Service Call | Declarative `/cube/reset_pose` resets the cube with `{}` |
| Raw Messages | FoxRun debug topics publish live values |

The controlled MessagePack contract supports publish, subscribe, and full
duplex over the Foxglove WebSocket path. Its live and MCAP channels are
schemaless (`message_encoding=msgpack`, schema id zero), and the maintained
**FoxRun Publish** extension is required for typed field discovery/editing;
built-in Foxglove panels do not render or author typed MessagePack. The probe
uses ordinary fields, while a MessagePack input topic may instead contain
exactly one `FoxRunStream<T>`—never a mixed or multi-stream topology.

## Interaction Checks

- Left-drag in the Game view to rotate the cube.
- Right-drag to move the cube.
- Scroll to scale the cube.
- Change `/cube/color` in the Parameters panel and confirm the cube color changes.
- Change `/cube/scale` and confirm the cube size changes.
- Call `/cube/reset_pose` with `{}` and confirm the cube returns to its default pose and responds with `status: "ok"`.

## Notes

- This sample is the package-facing full demo. It should stay stable and user-friendly.
- New SDK features should be proven in the repository `Unity2Foxglove/` project before being promoted into this sample.
- Generated FoxRun `.g.cs` files, local build outputs, and repository-only acceptance artifacts should not be copied into this sample.
