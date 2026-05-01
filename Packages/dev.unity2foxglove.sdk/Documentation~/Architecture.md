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
| 1 | Done | WebSocket handshake, subprotocol, serverInfo |
| 2 | Done | Channel advertise, subscribe/unsubscribe, MessageData routing |
| 3 | **Done** | Official schemas, FrameTransform, SceneUpdate (cube), 3D panel |
| 4 | **Done** | Unity MonoBehaviour integration, Transform/SceneCube/Camera publishers |
| 5 | **Done** | IL2CPP hardening, nanosecond timestamps, transport lifecycle, link.xml, package identity migration |
| 6 | Planned | Optional: MCAP, parameters, services |

## Layers

```
┌─────────────────────────────────────┐
│   Unity Components (Phase 4)        │  FoxgloveManager, Publishers
├─────────────────────────────────────┤
│   Core (FoxgloveSession)            │  Session lifecycle, channel API,
│   FoxgloveRuntime                   │  publish routing, sub/unsub parsing,
│                                     │  RegisterSchemaChannel, PublishJson
├──────────────┬──────────────────────┤
│  Protocol    │  Schemas             │  JSON messages (Advertise, Subscribe,
│              │                      │  ServerInfo), Binary frames
│              │  FoxgloveSchemaDefs  │  FrameTransform, SceneUpdate DTOs
│              │  FoxgloveVisualMsgs  │  FoxgloveTime, Vector3, Quaternion,
│              │                      │  Pose, Color, CubePrimitive, etc.
├──────────────┴──────────────────────┤
│  Transport                           │  IFoxgloveTransport
│  ManagedWsBackend                    │  TcpListener + RFC 6455
└──────────────────────────────────────┘
```

### Schema Layer (Phase 3)

- **Source:** `foxglove-sdk/schemas/jsonschema/`, commit `main@b298c3d1649e6e5dfd77a53b12ab7c27f97c7aba`
- **Storage:** Schema JSON files embedded as assembly resources, decoded at static init
- **Schema hashes:** FrameTransform.json sha256=9986de138717bfaf, SceneUpdate.json sha256=7530dfd8585239e5
- **Coordinate strategy:** Unity raw, root frame `unity_world`, no handedness/ENU/ROS conversion
- **SceneUpdate scope:** Cube primitive only; other primitives (arrows, spheres, cylinders, lines, triangles, texts, models) are empty arrays

### Phase 3 Data Flow

```
RegisterSchemaChannel(id, topic, schemaName)
  → ISchemaRegistry.TryGetSchema(schemaName)
  → Construct AdvertiseChannel(encoding="json", schemaEncoding="jsonschema", schema=content)
  → RegisterChannel(channel) → BroadcastText(advertise)

PublishJson(channelId, message)
  → JsonConvert.SerializeObject(message)
  → Encoding.UTF8.GetBytes(json)
  → Publish(channelId, payload) → MessageData routing
```

### Transport Abstraction

`IFoxgloveTransport` hides the WebSocket implementation.

- **Current (Phase 1–3):** `ManagedWsBackend` — custom RFC 6455 implementation on `System.Net.Sockets.TcpListener`.
- **Former (replaced):** websocket-sharp was dropped (does not echo `Sec-WebSocket-Protocol`).
- **Future (Phase 5):** `NativeFoxgloveBackend` — P/Invoke to `foxglove-c.h`

### Third-party dependencies

- `Newtonsoft.Json` — via `com.unity.nuget.newtonsoft-json` (Unity) or NuGet (tests)
- No other third-party runtime dependencies

### Protocol Constraints (cumulative)

- `serverInfo.capabilities` = `[]` (no capabilities declared)
- `schemaName` and `schema` always serialized (as `""` for schema-less channels)
- `schemaEncoding` = `"jsonschema"` for typed channels, omitted otherwise
- `subscribe` uses `subscriptions: [{ id, channelId }]`
- `unsubscribe` uses `subscriptionIds: [...]`
- `MessageData` binary: opcode(1) + subscriptionId(u32 LE) + logTime(u64 LE) + payload
- `SceneEntityDeletion.type` serialized as integer (0=MATCHING_ID, 1=ALL)
- All `SceneEntity` primitive arrays always present (empty `[]` when not used)
- Unknown `op` / malformed JSON → logged, connection stays open

### Implementation Notes

- **WebSocket server:** `TcpListener` + manual RFC 6455. Read timeout 5s during handshake, infinite after.
- **Subprotocol negotiation:** Exact token match against `["foxglove.sdk.v1", "foxglove.websocket.v1"]`.
- **Schema embedding:** JSON content stored as base64-encoded C# `const` strings. Decoded at static init via `Convert.FromBase64String` + `Encoding.UTF8.GetString`. No runtime file I/O or resource streams.
- **Core schema registration:** Called once in `FoxgloveRuntime` three-parameter constructor. Idempotent (overwrites on duplicate name).
- **DTO field naming:** All wire fields use `[JsonProperty("...")]` with official field names.
- **Disconnect cleanup:** Unified `DisconnectClient(id, conn)` used by send failure, receive finally, and Stop.
- **Unity thread boundary:** `Runtime/Unity/*.cs` components access `UnityEngine` API only within Unity lifecycle callbacks (Awake, OnEnable, Update, LateUpdate, OnDisable, OnDestroy). Transport callbacks (OnClientConnected, OnTextReceived, etc.) do not touch Unity objects. `Runtime/Schemas/*.cs`, `Runtime/Core/*.cs`, `Runtime/Transport/*.cs` remain UnityEngine-free and dotnet-testable.
- **IL2CPP / link.xml:** `Runtime/link.xml` in the package is a **template** — Unity does not use it directly. The consuming project MUST copy it to `Assets/link.xml`. It preserves `Newtonsoft.Json` and `Unity.FoxgloveSDK` assemblies with `preserve="all"`. Managed stripping level: `Medium`. See `NativeBackendEvaluation.md` for native backend assessment.
- **Timestamp strategy:** `FoxgloveTimeUtil.NowUnixTimeNs()` uses `Stopwatch.GetTimestamp()` with UTC epoch anchor for nanosecond precision. `SystemClock.NowNs` delegates to the same source. No `time` capability declared in `serverInfo`.
- **Transport lifecycle:** `FoxgloveRuntime` owns transport and disposes it. `FoxgloveSession` borrows transport and only unbinds events on dispose. `IFoxgloveTransport : IDisposable`.
