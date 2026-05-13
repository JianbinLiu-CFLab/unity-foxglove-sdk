# Unity2Foxglove SDK

Stream Unity real-time data (Transforms, scene entities, camera frames, custom fields) to the [Foxglove](https://foxglove.dev) visualization platform via WebSocket.

## Version requirements

- Unity 6000.0 LTSC or later (developed on 6000.3.14f1 LTSC; compatible with 6000.0.74f1 LTSC)
- Editor + Standalone Player. Windows is verified for v1.4.0; macOS/Linux are intended targets but not yet verified.
- Dependency: `com.unity.nuget.newtonsoft-json` 3.2.1

## Quick install

Add to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "dev.unity2foxglove.sdk": "file:../../Packages/dev.unity2foxglove.sdk"
  }
}
```

## Minimal usage

1. Create an empty GameObject in the scene and add the **FoxgloveManager** component
2. On the GameObject you want to track, add a **FoxgloveTransformPublisher**
3. Play > open Foxglove Desktop > connect to `ws://127.0.0.1:8765`
4. View the GameObject's position and rotation in real time in the 3D panel

```csharp
// Or use [FoxRun] for zero-code custom field publishing
public partial class MyLogger : MonoBehaviour
{
    [FoxRun("/debug/position")]
    private Vector3 _pos;
}
```

## Features

- Structured data publishing (FrameTransform, SceneUpdate, CompressedImage)
- Typed sensor publishers for PointCloud, LaserScan, and CameraCalibration
- `[FoxRun]` attribute for generated fixed-rate, change-driven, interval, and explicit trigger publishing
- MCAP recording and replay (LZ4/Zstd compression)
- Paused replay scrubbing with Unity scene snapshot updates and bounded panel-history rebuilds
- Managed WebSocket backpressure for slow clients
- Optional Unity-native WSS/TLS mode and lightweight shared query-token gate
- Parameters remote read/write
- Service remote invocation
- IL2CPP standalone build support
- Coordinate system conversion (LeftHand/RightHand)

## Full documentation

See [Documentation~/README.md](Documentation~/README.md).
