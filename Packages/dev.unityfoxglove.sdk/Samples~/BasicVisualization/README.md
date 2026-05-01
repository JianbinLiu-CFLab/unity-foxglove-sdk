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
