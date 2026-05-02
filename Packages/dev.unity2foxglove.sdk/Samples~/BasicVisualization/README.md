# Basic Visualization Sample

Minimal setup to stream Transform, Scene cube, and Camera data from Unity to Foxglove.

## Setup

1. Import this package into a Unity 2022.3+ project
2. Create an empty GameObject named `Foxglove`
3. Add component `FoxgloveManager`
4. Create a Cube in the scene
5. Add `FoxgloveTransformPublisher` and `FoxgloveSceneCubePublisher` to the cube
6. Select Main Camera, add `FoxgloveCameraPublisher`
7. Press Play

## Foxglove Connection

1. Open Foxglove Desktop
2. "Open connection" → Foxglove WebSocket → `ws://127.0.0.1:8765`
3. Topics: `/tf`, `/scene`, `/unity/camera`
4. 3D panel: select `/scene` to see the cube
5. Image panel: select `/unity/camera` to see the camera feed

## Migration from Python Bridge

Old setup:
```
Unity (C#) → UDP/TCP → Python (foxglove-sdk) → Foxglove
```

New setup (this package):
```
Unity (C# + FoxgloveManager) → WebSocket → Foxglove
```

No more:
- `UdpClient.Send(jsonBytes)` → use `FoxgloveTransformPublisher`
- `TcpClient.Connect(host, 9001)` → use `FoxgloveCameraPublisher`
- Python bridge process → Foxglove server runs inside Unity

## Component Reference

| Component | Topic | Schema | Default Rate |
|-----------|-------|--------|-------------|
| FoxgloveTransformPublisher | `/tf` | foxglove.FrameTransform | 10 Hz |
| FoxgloveSceneCubePublisher | `/scene` | foxglove.SceneUpdate | 10 Hz |
| FoxgloveCameraPublisher | `/unity/camera` | foxglove.CompressedImage | 10 Hz |

## Phase 6: Parameters & Services Verification

### Setup

1. Create a Cube in the scene with `MouseDragCube` component
2. Add `FoxgloveDemoSetup` component to the Foxglove GameObject, wire `_cube` and `_manager` references
3. Press Play

### Parameters Panel Verification

1. Open Foxglove Desktop, connect to `ws://127.0.0.1:8765`
2. Open Parameters panel — you should see `/cube/color` and `/cube/scale`
3. Modify `/cube/color` (e.g., `[1.0, 0.0, 0.0, 1.0]` for red) — cube color changes in 3D panel
4. Modify `/cube/scale` (e.g., `2.0`) — cube size changes in 3D panel
5. **Troubleshooting:** If Parameters panel is empty, verify `serverInfo.capabilities` includes `"parameters"` and Unity has called `rt.RegisterParameter(...)` at runtime

### Service Call Panel Verification

1. Open Foxglove Desktop, connect to `ws://127.0.0.1:8765`
2. Open Service Call panel → Settings (gear icon)
3. Enter `Service name`: `/cube/reset_pose`
4. In the Request text area, enter: `{}`
5. Click "Call service"
6. Cube pose resets to origin, Plot panel `/tf.translation.*` curves jump to zero
7. **Troubleshooting:** If `/cube/reset_pose` doesn't appear, verify `serverInfo.capabilities` includes `"services"` and Unity has registered the service

### Plot Panel Verification

1. Open Plot panel in Foxglove
2. Add series: `/tf.translation.x`, `/tf.translation.y`, `/tf.translation.z`
3. In Unity, drag/rotate/scroll the cube with mouse
4. Plot curves should update in real-time reflecting position changes
5. After calling `/cube/reset_pose`, all curves should jump back to zero

### Manual Acceptance Checklist

- [ ] Foxglove connects `ws://127.0.0.1:8765`
- [ ] 3D panel shows `/scene` cube
- [ ] Camera panel shows `/unity/camera` image
- [ ] Plot panel shows `/tf.translation.*` curves; drag cube → curves change
- [ ] Parameters panel shows `/cube/color` and `/cube/scale`
- [ ] Modify `/cube/color` → cube color changes
- [ ] Modify `/cube/scale` → cube size changes
- [ ] Service Call panel: `/cube/reset_pose` with `{}` → cube resets to origin
- [ ] IL2CPP Player: same verification passes
