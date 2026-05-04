# Foxglove Unity SDK

Real-time data streaming from Unity to [Foxglove](https://foxglove.dev) for visualization.

## Status

**Phase 12 complete.** Full MCAP recorder + reader + replay engine, compression (LZ4/Zstd), extended recording (Parameters, Services, ClientPublish, ConnectionGraph), coordinate mode. 350+ automated tests. Package identity: `dev.unity2foxglove.sdk`.

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
| 6 | Done | Parameters, ParametersSubscribe, JSON Services |
| 7 | Done | ParametersSubscribe push, time capability, logger bridge, ConnectionGraph |
| 8 | Done | ClientPublish, ConnectionGraph refinement |
| 9 | Done | Assets / fetchAsset, PlaybackControl, unified publisher clock |
| 10 | Done | MCAP recording/dual-write (topic messages) |
| 11 | Done | MCAP reader, ReplayEngine, object adapter, coordinate mode |
| 12 | Done | MCAP compression (LZ4/Zstd), Parameters/Services/ClientPublish/ConnectionGraph recording |

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

**Editor vs Player timing:** In Editor Mode, connect Foxglove *after* pressing Play — if you connect before Play, the session hasn't started yet and ConnectionGraph / topic list may be incomplete. IL2CPP Player auto-starts the server on launch, so connecting immediately after startup always shows the full topology.

### Foxglove Desktop operation steps

1. **Open connection**: "Open connection" → select **Foxglove WebSocket** → URL `ws://127.0.0.1:8765`
2. **View topics**: left sidebar Topics panel shows `/tf` and `/scene`
3. **View 3D**: switch to 3D panel, select `/scene` topic → green cube at origin
   - Cube color uses Foxglove RGBA: `[r, g, b, a]` with values 0 to 1. The 4th value is alpha (0 = transparent, 1 = opaque).
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

### Phase 8 manual verification

**Connection Graph:**
1. Play → Foxglove connect → select **Connection Graph** panel
2. Expected: see publisher node (unity) with topics `/tf`, `/scene`, `/unity/camera`

**ClientPublish:**
1. Foxglove → **+** → **Publish** panel
2. Topic field: `/test/client_publish`
3. Message schema: select a known schema, e.g. `foxglove.CompressedImage`
4. Foxglove auto-generates the JSON template for that schema. Edit the payload values if desired.
5. Click **Publish**
6. Expected: Unity Console shows `[ClientMsg] client=1 topic=/test/client_publish payload=...`

### Phase 9: fetchAsset verification

**Setup:**
1. In Unity, select Foxglove GameObject → FoxgloveManager Inspector
2. Under **Asset Roots**, add:
   - `Uri Prefix`: `asset://demo/`
   - `Local Root`: `Assets` (relative to project root)
   - `Max Mb`: `16` (16 MB per file; leave default unless large assets needed)
3. Play

**Run the test script (separate PowerShell):**

```powershell
& "D:\BaiduSyncdisk\Obsidian Vault\Websocket\00 Inbox\Untiy2Foxglove\Tests\test_fetch_asset.ps1"
```

The script connects with `foxglove.sdk.v1` subprotocol, drains initial messages, sends:

```json
{"op":"fetchAsset","requestId":42,"uri":"asset://demo/Scripts/FoxgloveDemoSetup.cs"}
```

then reads the binary `fetchAssetResponse` frame.

**Verified output (2026-05-03):**

```
Binary: opcode=4 requestId=42 status=0 errorLen=0
[PASS] fetchAsset SUCCESS — got 4685 bytes, content matches
```

- `opcode=4` — `ServerOpcode.FetchAssetResponse`
- `requestId=42` — matches request
- `status=0` — success
- 4685 bytes returned, file content verified

**Error cases (bad URI, no root):**

```
Binary: opcode=4 requestId=42 status=1 errorLen=23
[FAIL] Server error: No asset roots registered
```

See `Untiy2Foxglove/Tests/test_fetch_asset.ps1` for the full script.

## MCAP Recording (Phase 10)

**Status:** Done. Records `/tf`, `/scene`, `/unity/camera` topic messages to .mcap files during live WebSocket sessions.

### Quick Start

Enable recording via `FoxgloveManager` Inspector:
- Check **Enable Recording** under "MCAP Recording" header
- Set output prefix and directory (defaults to `Application.persistentDataPath`)
- .mcap files are timestamped: `foxglove_20260503_145719.mcap`

Or via code:

```csharp
var runtime = new FoxgloveRuntime();
runtime.EnableRecording("D:/recordings/my_session.mcap");
runtime.Start("My App", port: 8765);
// ... publish data ...
runtime.Stop(); // gracefully closes the .mcap file
```

### Known Limitations

MCAP recording only captures topic message data (Schema, Channel, Message records) via the `FoxgloveSession.Publish()` dual-write hook. The following live WebSocket protocol features are **not** recorded to .mcap and are unavailable when playing back the file in Foxglove Studio:

- **Parameters** — parameter values travel over JSON text protocol, not the publish path
- **Services** — service calls and responses use JSON text + binary response, not the publish path
- **ConnectionGraph** — publisher/subscriber topology is dynamically maintained during the live session only
- **ClientPublish** — client-published data arrives via `OnClientBinary`, outside the current dual-write scope

This is expected behavior. Expanding MCAP recording to these data paths is planned for Phase 12.

### Manual Verification

1. In Unity, enable MCAP Recording on `FoxgloveManager` Inspector
2. Play → Foxglove connect → run for a few seconds
3. Stop Play → find the .mcap file in the recording directory
4. Open Foxglove Studio → "Open local file" → select the .mcap file
5. Verify: 3D panel shows scene, Plot panel shows time-series data, timeline scrubbing works
6. Verify: Parameters / Services / ConnectionGraph panels are unavailable (expected — see limitations above)

## Architecture

See [Architecture.md](Architecture.md) for the transport abstraction, protocol layer, and module breakdown.

### Verify transport logging (Phase 7)

To confirm `IFoxgloveLogger` is wired through to Unity Console:

```powershell
$ws = New-Object System.Net.WebSockets.ClientWebSocket
$ws.ConnectAsync("ws://127.0.0.1:8765", [System.Threading.CancellationToken]::None).Wait(2000)
```

**Expected:** Unity Console shows:

```
[Foxglove] Client connected without accepted subprotocol, closing.
UnityEngine.Debug:LogError(object)
Unity.FoxgloveSDK.Components.UnityLogger:LogError(...)
Unity.FoxgloveSDK.Transport.ManagedWsBackend:Handshake(...)
```

The stack trace confirms the log flows through `UnityLogger` → `ManagedWsBackend` → `Debug.LogError`, proving the Phase 7 logger bridge is fully connected.

---

## Dependencies

- `Newtonsoft.Json` — JSON serialization (NuGet for tests, `com.unity.nuget.newtonsoft-json` for Unity)
- No other third-party runtime dependencies — WebSocket server is a custom RFC 6455 implementation on `System.Net.Sockets.TcpListener`
