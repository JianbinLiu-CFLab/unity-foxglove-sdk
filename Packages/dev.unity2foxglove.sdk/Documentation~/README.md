# Foxglove Unity SDK

Real-time data streaming from Unity to [Foxglove](https://foxglove.dev) for visualization.

## Status

**Phase 6 in progress.** Parameters (get/set/subscribe), JSON Services with binary request/response codec, service handler main-thread model, logger bridge. Package identity: `dev.unity2foxglove.sdk`.

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
| 3 | Done | Official schemas (FrameTransform, SceneUpdate), typed DTOs, 3D cube |
| 4 | Done | Unity MonoBehaviour integration, Transform/SceneCube/Camera publishers |
| 5 | Done | IL2CPP hardening, nanosecond timestamps, transport lifecycle, link.xml |
| 6 | **In Progress** | Parameters, ParametersSubscribe, JSON Services |

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

## Phase 6: Parameters & Services

### Parameters

```csharp
// Register parameters after starting the runtime
var rt = new FoxgloveRuntime();
rt.Start("My App", port: 8765);

rt.RegisterParameter("/my_param", 42, "number", writable: true);
rt.RegisterParameter("/my_readonly", "hello", "string", writable: false);

// Read parameter value
var p = rt.Parameters.GetWireParameter("/my_param");
Console.WriteLine($"Value: {p.Value}");

// Client setParameters modifies writable params only — read-only and unknown params are ignored
```

In Foxglove Parameters panel, registered parameters appear automatically. Only `writable: true` params can be edited from the panel.

### Services

```csharp
// Register a JSON service
var svcId = rt.RegisterService(new ServiceDescriptor
{
    Name = "/my_service",
    Type = "/my_service",
    Request = new ServiceSchemaDescriptor { SchemaName = "/MyRequest" },
    Response = new ServiceSchemaDescriptor { SchemaName = "/MyResponse" }
});

// In Unity (main thread), drain and handle pending calls:
rt.DrainServiceCalls();

var pending = rt.Session.Services.GetPendingCalls();
foreach (var call in pending)
{
    // Process call on main thread
    var response = Encoding.UTF8.GetBytes("{\"result\": \"ok\"}");
    rt.Session.Services.CompleteResponse(call.CallId, "json", response);
}
```

**Important:** Service handlers must execute on Unity's main thread. Transport callbacks only parse and enqueue — do not access Unity API from within handler delegates before drain. Use `FoxgloveManager.Update()` → `DrainServiceCalls()` to process on the main thread.

### Logger Bridge (Phase 6)

Protocol errors and warnings go through `IFoxgloveLogger` instead of `Console.Error.WriteLine`:
- **dotnet tests:** `ConsoleLogger` writes to stderr
- **Unity Editor:** visible in the Console window
- **IL2CPP Player:** visible in `Player.log`

This ensures service failures, malformed JSON, and unsupported encoding errors are always traceable.

## Manual Verification

### Empty server (Phase 1)

```powershell
dotnet run --project Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj -- --serve --port 8765
```

### Heartbeat demo (Phase 2)

```powershell
dotnet run --project Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj -- --serve --port 8765 --demo
```

### 3D cube demo (Phase 3)

```powershell
dotnet run --project Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj -- --serve --port 8765 --demo3d
```

### Windows IL2CPP build (Phase 5+)

Close the Untiy editor. IL2CPP build takes 10-30 minutes. **Option A** shows live output in one terminal but may behave inconsistently on some Windows systems. **Option B** is the most reliable: run build in one terminal, watch progress in another.

**Option A — single terminal with live output (may not pipe on all Windows):**

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.3.14f1\Editor\Unity.exe" -batchmode -quit -projectPath "D:\BaiduSyncdisk\Obsidian Vault\Websocket\00 Inbox\Untiy2Foxglove" -executeMethod FoxgloveBuild.BuildWindowsIl2Cpp -logFile - 2>&1 | Tee-Object -FilePath "D:\BaiduSyncdisk\Obsidian Vault\Websocket\00 Inbox\build\Unity\phase6-il2cpp.log"
```

**Option B — two terminals, reliable fallback:**

Terminal 1 (blocks until done, return code 0 = success):

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.3.14f1\Editor\Unity.exe" -batchmode -quit -projectPath "D:\BaiduSyncdisk\Obsidian Vault\Websocket\00 Inbox\Untiy2Foxglove" -executeMethod FoxgloveBuild.BuildWindowsIl2Cpp -logFile "D:\BaiduSyncdisk\Obsidian Vault\Websocket\00 Inbox\build\Unity\phase6-il2cpp.log"
```

Terminal 2 — watch progress (repeat anytime):

```powershell
Get-Content "D:\BaiduSyncdisk\Obsidian Vault\Websocket\00 Inbox\build\Unity\phase6-il2cpp.log" -Tail 5
```

Look for `Build succeeded:` at the end. `Exiting batchmode successfully with return code 0` means the build passed.

The Player includes `MouseDragCube` on the demo Cube: **left-drag** to rotate, **right-drag** to pan, **scroll** to scale. Changes are visible in Foxglove 3D panel in real-time.

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
