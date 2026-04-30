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

`IFoxgloveTransport` hides the WebSocket implementation. Current default: `ManagedWsBackend` (WebSocketSharp-netstandard). Future: `NativeFoxgloveBackend` (P/Invoke to foxglove-c).

### Clock Abstraction

`IFoxgloveClock` provides nanosecond timestamps for MessageData frames. Default: `SystemClock` (DateTime.UtcNow).

### Schema Registry

`ISchemaRegistry` stores and serves schema definitions that Foxglove needs for decoding. Schema source: `foxglove-sdk/schemas/jsonschema/` (local clone).
