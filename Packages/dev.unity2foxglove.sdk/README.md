# Unity2Foxglove SDK

Stream Unity real-time data (Transforms, scene entities, camera frames, custom fields) to the [Foxglove](https://foxglove.dev) visualization platform via WebSocket.

## Version requirements

- Unity 6000.0 LTSC or later (developed on 6000.3.14f1 LTSC; compatible with 6000.0.74f1 LTSC)
- Editor + Standalone Player. Windows is verified for v1.9.6; macOS/Linux are intended targets but not yet verified.
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

## Package choices

**For most Unity projects, install only this SDK package.** It already covers
Foxglove WebSocket streaming, FoxRun over WebSocket, MCAP recording/replay,
sensors, services, and the optional ROS2 Bridge sidecar. Direct native ROS2 is
an opt-in capability, not a prerequisite for any of those workflows.

| If you need | Add alongside this SDK |
| --- | --- |
| Windows x64 Foxglove Cloud remote access | `dev.unity2foxglove.remotegateway.win64` |
| Direct native ROS2 using packaged standard messages | `dev.unity2foxglove.ros2forunity` and exactly one `dev.unity2foxglove.ros2forunity.runtime.<distro>.win64` package |
| Direct native ROS2 using generated custom FoxRun DTOs | The facade, one matching runtime, `dev.unity2foxglove.foxrun.ros2.interfaces`, and the same-distro `dev.unity2foxglove.foxrun.ros2.interfaces.typesupport.<distro>.win64` add-on |

Do not install multiple packaged ROS2 runtimes or multiple custom typesupport
add-ons in one Unity project. The full selection matrix, including the
external ROS2 For Unity alternative, is in the
[repository package-combination guide](../../README.md#package-combinations).

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
- FoxRun `Publish`, `Subscribe`, and `PublishAndSubscribe` over Foxglove WebSocket with JSON, Protobuf, or typed schemaless MessagePack; typed MessagePack authoring uses the maintained FoxRun Publish extension
- MCAP recording and replay (LZ4/Zstd compression)
- Paused replay scrubbing with Unity scene snapshot updates and bounded panel-history rebuilds
- Managed WebSocket backpressure for slow clients
- Optional Unity-native WSS/TLS mode and lightweight shared query-token gate
- Parameters remote read/write
- Service remote invocation
- IL2CPP standalone build support
- Coordinate system conversion (LeftHand/RightHand)

## Security note

Inspector-entered local gate secrets such as WSS certificate passwords, shared
WebSocket tokens, replay cursor bearer tokens, and Remote MCAP bearer tokens
are serialized into Unity scenes or prefabs. Use them for local development and
manual acceptance only, and avoid committing real production secrets.

For these manager secrets, non-empty environment variables take priority over
Inspector fallback values:

| Secret | Environment variable | Inspector fallback |
|---|---|---|
| Shared WebSocket token | `FOXGLOVE_SHARED_TOKEN` | `Shared Token` |
| WSS certificate password | `FOXGLOVE_CERTIFICATE_PASSWORD` | `Certificate Password` |
| Replay cursor bearer token | `FOXGLOVE_REPLAY_CURSOR_TOKEN` | `Replay Cursor Bridge Token` |
| Remote MCAP bearer token | `FOXGLOVE_REMOTE_MCAP_TOKEN` | `Remote MCAP Bearer Token` |

## Full documentation

See [Documentation~/README.md](Documentation~/README.md).
