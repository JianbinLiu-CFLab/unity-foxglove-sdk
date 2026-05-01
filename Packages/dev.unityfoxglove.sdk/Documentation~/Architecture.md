# Architecture

## Decision: Pure C# MVP

**Decision date:** 2026-04-30

We chose **pure C# WebSocket protocol implementation** over wrapping the official `foxglove-sdk` C FFI.

**Why:**
- Fastest path to a working Unity↔Foxglove visualization loop.
- No native build/CI/linking overhead during MVP phase.
- The WebSocket protocol (JSON control plane + binary data) is well-defined and relatively small.

**When to switch to Native Backend (Phase 5):**
- Pure C# path fails IL2CPP tests on target platforms.
- Protocol drift becomes unmanageable vs. official SDK.
- We need Parameters/Services/PlaybackControl and the C FFI is more reliable.

## Phases

| Phase | Status | What |
|-------|--------|------|
| 0 | Done | Package skeleton, abstraction layer, tech decision |
| 1 | **Done** | WebSocket handshake, subprotocol, serverInfo |
| 2 | Planned | Channel advertise, subscribe/unsubscribe, MessageData |
| 3 | Planned | Official schemas, FrameTransform, SceneUpdate |
| 4 | Planned | Unity MonoBehaviour integration, publishers |
| 5 | Planned | IL2CPP hardening, Native Backend evaluation |
| 6 | Planned | Optional: MCAP, parameters, services |

## Layers

```
┌─────────────────────────────────────┐
│   Unity Components (Phase 4)        │  FoxgloveManager, Publishers
├─────────────────────────────────────┤
│   Core (FoxgloveSession)            │  Session lifecycle, publish API
├─────────────────────────────────────┤
│   Protocol                          │  JSON messages, Binary frames
├──────────────┬──────────────────────┤
│  Transport   │  Schemas             │  IFoxgloveTransport, ISchemaRegistry
│  ManagedWs   │  IFoxgloveClock      │
└──────────────┴──────────────────────┘
```

### Transport Abstraction

`IFoxgloveTransport` hides the WebSocket implementation.

- **Current (Phase 1):** `ManagedWsBackend` — custom RFC 6455 implementation on `System.Net.Sockets.TcpListener`. Handles WebSocket handshake, subprotocol negotiation, frame encoding/decoding. No third-party WebSocket library dependency.
- **Former (replaced):** websocket-sharp was dropped because it does not echo `Sec-WebSocket-Protocol` in the handshake response, causing Foxglove Desktop to reject connections.
- **Future (Phase 5):** `NativeFoxgloveBackend` — P/Invoke to `foxglove-c.h`

Key constraint: `SendText(clientId, json)` must target specific client, never broadcast.

### Clock Abstraction

`IFoxgloveClock` provides nanosecond timestamps for MessageData frames. Default: `SystemClock` (DateTime.UtcNow).

### Schema Registry

`ISchemaRegistry` stores and serves schema definitions. Schema source: `foxglove-sdk/schemas/jsonschema/` (local clone).

### Third-party dependencies

- `Newtonsoft.Json` — via `com.unity.nuget.newtonsoft-json` (Unity) or NuGet (tests)
- No other third-party runtime dependencies

### Phase 1 Protocol Constraints

- `serverInfo.capabilities` = `[]` (no capabilities declared)
- `serverInfo.supportedEncodings` — omitted (null)
- `serverInfo.metadata` — omitted (null)
- `sessionId` — `Guid.NewGuid().ToString()`, stable per `FoxgloveSession` instance
- Subprotocol: `foxglove.sdk.v1` or `foxglove.websocket.v1` required; wrong/missing subprotocol → HTTP 400 + connection closed

### Phase 1 Implementation Notes

- **WebSocket server:** `TcpListener` + manual RFC 6455 implementation. Accept loop on thread pool, per-client receive loop.
- **Handshake:** Raw byte-by-byte HTTP header reading (`ReadLineRaw`) to avoid `StreamReader` buffering that would steal frame data from the socket.
- **Read timeout:** 5 seconds during handshake (prevent slow-connection deadlock), `Timeout.Infinite` after handshake (keep idle connections alive).
- **Subprotocol negotiation:** Matches client's `Sec-WebSocket-Protocol` header against `["foxglove.sdk.v1", "foxglove.websocket.v1"]`, echoes matched protocol in 101 response. Rejects with 400 if no match.
- **Frame support:** Text (opcode 0x1), Binary (0x2), Close (0x8), Ping→Pong (0x9/0xA), client mask decode (`^ mask[i % 4]`).
- **Client ID:** `Interlocked.Increment`-based, starting from 1, thread-safe via `ConcurrentDictionary`.
- **Send synchronization:** `lock (SendLock)` per connection, since `NetworkStream.Write` is not safe for concurrent access.
