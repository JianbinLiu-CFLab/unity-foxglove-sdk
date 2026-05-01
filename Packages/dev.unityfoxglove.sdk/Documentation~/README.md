# Foxglove Unity SDK

Real-time data streaming from Unity to [Foxglove](https://foxglove.dev) for visualization.

## Status

**Phase 1 complete.** WebSocket handshake and `serverInfo` working. Foxglove Desktop can connect. No data publishing yet (Phase 2).

## Supported Unity Versions

- Unity 2022 LTS+
- Editor + Standalone Player (Windows first)
- WebGL not supported

## What Phase 1 does

- Starts a WebSocket server using `TcpListener` and RFC 6455 handshake
- Foxglove Desktop can connect via `ws://127.0.0.1:8765` (select "Foxglove WebSocket")
- Subprotocol negotiation: accepts `foxglove.sdk.v1` and `foxglove.websocket.v1`
- Each client receives a `serverInfo` message on connect (per-client `SendText`, not broadcast)
- `serverInfo.capabilities` = `[]`, `supportedEncodings` and `metadata` omitted
- Wrong subprotocol → connection rejected with HTTP 400
- No topics/channels yet (Phase 2: advertise/subscribe/MessageData)

## Quick Start (Phase 2+)

```csharp
var runtime = new FoxgloveRuntime();
runtime.Start("My Unity App", port: 8765);

var channel = new AdvertiseChannel
{
    Id = 1,
    Topic = "/unity/transform",
    Encoding = "json",
    SchemaName = "foxglove.FrameTransform"
};
runtime.Session.Channels.Register(channel);

// Each frame:
runtime.Session.Publish(1, jsonBytes);
```

## Manual Verification

### Start the server

```powershell
dotnet run --project "Packages/dev.unityfoxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj" -- --serve --port 8765
```

### Connect with Foxglove Desktop

1. Open Foxglove Desktop
2. "Open connection" → select **Foxglove WebSocket**
3. URL: `ws://127.0.0.1:8765`
4. Expected: connection succeeds, "Unity Foxglove SDK" appears, no topics listed

### Phase 1 acceptance checklist

| Criterion | Status |
|-----------|--------|
| Foxglove connects successfully, no server-side exceptions | ✓ |
| Connection during idle (no data) stays open indefinitely | ✓ |
| No topic list — expected (advertise is Phase 2) | ✓ |
| Disconnect + reconnect multiple times, no dirty client state | ✓ |
| Foxglove close/reconnect does not crash the server | ✓ |
| Wrong subprotocol → connection rejected | ✓ |

Known: seeing no topics is expected — `advertise` is in Phase 2.

## Architecture

See [Architecture.md](Architecture.md) for the transport abstraction and module breakdown.

## Dependencies

- `Newtonsoft.Json` — JSON serialization (NuGet for tests, `com.unity.nuget.newtonsoft-json` for Unity)
- No third-party WebSocket library — custom RFC 6455 implementation on top of `System.Net.Sockets.TcpListener`
