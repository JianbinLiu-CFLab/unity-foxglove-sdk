# Foxglove Unity SDK

Real-time data streaming from Unity to [Foxglove](https://foxglove.dev) for visualization.

## Status

**Phase 3 complete.** Typed JSON schema channels, FrameTransform + SceneUpdate with cube primitive, Foxglove 3D panel shows green cube.

## Supported Unity Versions

- Unity 2022 LTS+
- Editor + Standalone Player (Windows first)
- WebGL not supported

## What each Phase does

| Phase | Status | Capabilities |
|-------|--------|-------------|
| 0 | Done | Package skeleton, abstraction layer |
| 1 | Done | WebSocket handshake, subprotocol, serverInfo |
| 2 | Done | Channel advertise, subscribe/unsubscribe, MessageData routing |
| 3 | **Done** | Official schemas (FrameTransform, SceneUpdate), typed DTOs, 3D cube |
| 4 | Planned | Unity MonoBehaviour integration, publishers |

## Quick Start

```csharp
var runtime = new FoxgloveRuntime();
runtime.Start("My Unity App", port: 8765);

// Register a typed schema channel
runtime.RegisterSchemaChannel(1, "/tf", "foxglove.FrameTransform");

// Publish a typed message
var tf = new FrameTransformMessage
{
    Timestamp = new FoxgloveTime { Sec = 1 },
    ParentFrameId = "unity_world",
    ChildFrameId = "child",
    Translation = new FoxgloveVector3 { X = 1, Y = 0, Z = 0 },
    Rotation = new FoxgloveQuaternion { X = 0, Y = 0, Z = 0, W = 1 }
};
runtime.PublishJson(1, tf);
```

## Manual Verification

### Empty server (Phase 1)

```powershell
dotnet run --project Packages/dev.unityfoxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj -- --serve --port 8765
```

### Heartbeat demo (Phase 2)

```powershell
dotnet run --project Packages/dev.unityfoxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj -- --serve --port 8765 --demo
```

### 3D cube demo (Phase 3)

```powershell
dotnet run --project Packages/dev.unityfoxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj -- --serve --port 8765 --demo3d
```

### Foxglove Desktop operation steps

1. **Open connection**: "Open connection" → select **Foxglove WebSocket** → URL `ws://127.0.0.1:8765`
2. **View topics**: left sidebar Topics panel shows `/tf` and `/scene`
3. **View 3D**: switch to 3D panel, select `/scene` topic → green cube at origin
4. **Raw view**: switch panel to Raw Messages, select `/scene` → see SceneUpdate JSON payload
5. **Reconnect**: close Foxglove, reopen, repeat — server survives

### Phase 3 manual acceptance

| # | Acceptance criterion | Result |
|---|---------------------|--------|
| 1 | Foxglove connects `ws://127.0.0.1:8765` | Pass |
| 2 | Topics show `/tf` and `/scene` | Pass |
| 3 | Raw Messages shows SceneUpdate JSON | Pass |
| 4 | 3D panel shows green cube | Pass |
| 5 | Disconnect + reconnect works | Pass |
| 6 | Closing Foxglove does not crash server | Pass |

## Architecture

See [Architecture.md](Architecture.md) for the transport abstraction, protocol layer, and module breakdown.

## Dependencies

- `Newtonsoft.Json` — JSON serialization (NuGet for tests, `com.unity.nuget.newtonsoft-json` for Unity)
- No other third-party runtime dependencies — WebSocket server is a custom RFC 6455 implementation on `System.Net.Sockets.TcpListener`
