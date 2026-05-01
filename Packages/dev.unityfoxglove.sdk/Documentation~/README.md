# Foxglove Unity SDK

Real-time data streaming from Unity to [Foxglove](https://foxglove.dev) for visualization.

## Status

**Phase 2 complete.** Channel advertise, subscribe/unsubscribe, and MessageData routing working. Foxglove Desktop can see topics and receive data.

## Supported Unity Versions

- Unity 2022 LTS+
- Editor + Standalone Player (Windows first)
- WebGL not supported

## What each Phase does

| Phase | Status | Capabilities |
|-------|--------|-------------|
| 0 | Done | Package skeleton, abstraction layer |
| 1 | Done | WebSocket handshake, subprotocol, serverInfo |
| 2 | **Done** | Channel advertise, subscribe/unsubscribe, MessageData routing |
| 3 | Planned | Official schemas, FrameTransform, SceneUpdate |

## Quick Start

```csharp
var runtime = new FoxgloveRuntime();
runtime.Start("My Unity App", port: 8765);

// Register a JSON channel
var ch = new AdvertiseChannel
{
    Id = 1,
    Topic = "/debug/heartbeat",
    Encoding = "json",
    SchemaName = "",
    Schema = ""
};
runtime.RegisterChannel(ch);

// Publish data
var json = "{\"seq\":1,\"message\":\"hello\"}";
runtime.Publish(1, Encoding.UTF8.GetBytes(json));
```

## Manual Verification

### Empty server (Phase 1 behavior)

```powershell
dotnet run --project Packages/dev.unityfoxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj -- --serve --port 8765
```

Expected: Foxglove connects, no topics listed.

### Demo with heartbeat topic

```powershell
dotnet run --project Packages/dev.unityfoxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj -- --serve --port 8765 --demo
```

Expected: Foxglove connects, `/debug/heartbeat` topic visible, JSON data streams on subscribe.

### Foxglove Desktop operation steps

1. **Open connection**: "Open connection" → select **Foxglove WebSocket** → URL `ws://127.0.0.1:8765`
2. **View topics**: left sidebar Topics panel shows `/debug/heartbeat`
3. **Subscribe**: click `/debug/heartbeat`, switch panel to **Raw Messages**, select the topic from the top dropdown
4. **Verify data**: JSON messages appear at 1 Hz: `{"seq":...,"unixTimeNs":...,"message":"hello foxglove"}`
5. **Reconnect**: close Foxglove, reopen, repeat steps 1–4 — server survives and resumes

### Phase 2 manual acceptance

| # | Acceptance criterion | Result |
|---|---------------------|--------|
| 1 | Foxglove connects `ws://127.0.0.1:8765` | Pass |
| 2 | Topics panel shows `/debug/heartbeat` | Pass |
| 3 | Subscribe receives continuous heartbeat JSON | Pass |
| 4 | Disconnect + reconnect still receives `serverInfo` + `advertise` | Pass |
| 5 | Closing Foxglove does not crash the server | Pass |

## Architecture

See [Architecture.md](Architecture.md) for the transport abstraction, protocol layer, and module breakdown.

## Dependencies

- `Newtonsoft.Json` — JSON serialization (NuGet for tests, `com.unity.nuget.newtonsoft-json` for Unity)
- No other third-party runtime dependencies — WebSocket server is a custom RFC 6455 implementation on `System.Net.Sockets.TcpListener`
