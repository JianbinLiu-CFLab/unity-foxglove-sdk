## 1. Purpose

Use this page to understand the runtime architecture before extending Unity2Foxglove, debugging protocol behavior, or reviewing build-time behavior.

## 2. Workflow

You will understand the main runtime components, how Unity data reaches Foxglove, how MCAP recording/replay fits in, and where build-time helpers are used.

## 3. Runtime Overview

Unity2Foxglove embeds the Foxglove WebSocket server inside Unity.

Main pieces:

- `FoxgloveManager`: MonoBehaviour entry point and Inspector-facing configuration.
- `FoxgloveRuntime`: runtime coordinator for session, recording, replay, assets, parameters, and services.
- `FoxgloveSession`: protocol-level WebSocket session handling server info, channels, messages, Parameters, Services, client publish, assets, playback control, and connection graph.
- `ManagedWsBackend`: managed plain WebSocket transport.
- `ManagedWssBackend`: optional Unity-native TLS WebSocket transport. It authenticates an `SslStream`, then reuses the same managed WebSocket handshake, frame, queue, Origin Guard, token gate, and stats logic as `ManagedWsBackend`.
- Publisher components: Unity-facing components that publish Transform, Scene, Camera, and FoxRun data.

## 4. Live Data Path

1. A Unity component gathers data.
2. The component resolves a `FoxgloveManager`.
3. The Manager registers a schema/channel if needed.
4. The component publishes Protobuf, JSON, or ROS2 CDR payloads, depending on the Manager and publisher encoding settings.
5. `FoxgloveSession` sends data over the WebSocket transport.
6. Foxglove Desktop renders the topic.

## 5. Coordinate Mode

`FoxgloveManager` exposes:

- `LeftHand`: Unity native left-handed coordinates.
- `RightHand`: converts to ROS/Foxglove right-handed coordinates.

Use one mode consistently for live publish, MCAP recording, and replay. Recorder channel metadata stores coordinate mode so replay can warn about mismatches.

## 6. Parameters and Services

Parameters are stored in a runtime parameter store and served through Foxglove's parameter operations.

Services are registered with descriptors and handled on the Unity main thread so handlers can safely touch Unity objects.

For user steps, see [06_Parameters_and_Services](06_Parameters_and_Services.md).

## 7. MCAP Recording and Replay

Recording attaches to the runtime publish path and writes MCAP records for channels, schemas, messages, metadata, and indexes.

Replay loads an MCAP file, seeks by time, and forwards replay messages through the runtime. Live publishers can be disabled during replay to avoid duplicate data.

Replay is snapshot-based. It forwards recorded topic messages and can drive supported objects from transform/scene snapshots, but it does not reconstruct a deterministic physics or input simulation.

For user steps, see [08_MCAP_Recording_and_Replay](08_MCAP_Recording_and_Replay.md).

## 8. FoxRun Build Behavior

In Editor, FoxRun uses a Roslyn source generator. For Player builds, a pre-build step writes physical `.g.cs` files so IL2CPP compiles the generated sources as normal Unity scripts.

Runtime publishing uses generated accessors, not runtime reflection.

Generated files are meant to be build artifacts, not hand-edited source.

## 9. Transport Backpressure

`ManagedWsBackend` uses one bounded send queue per connected client. Live topic `MessageData` frames and broadcast binary live frames use a drop-oldest policy when a client falls behind. Text protocol messages, direct binary responses, service responses, asset responses, `pong`, and `close` are control frames and are preserved ahead of live data.

If a control frame still cannot fit after stale data is dropped, the slow client is disconnected. Queue size is an internal default in this phase, not an Inspector setting.

Client-originated subscribe and client advertise requests are also budgeted. A subscribe batch is applied all-or-nothing: if the batch would exceed the per-client or total subscription budget, none of the requested subscriptions are added. A client advertise batch is likewise all-or-nothing: if any advertised channel is invalid, too large, or would exceed the client/global channel budget, none of the channels in that batch are registered. The Foxglove WebSocket protocol does not provide a per-item rejection acknowledgement for these messages, so Unity logs the budget rejection and leaves the previous session state untouched.

Replay callbacks are collected while the replay cursor is locked and drained after the lock is released. Listener exceptions are isolated per handler: a failing listener is logged and later listeners still run. This differs from the default C# multicast delegate behavior and is intentional so one scene listener cannot stall replay delivery.

Several public APIs return defensive copies to protect runtime state. Protobuf descriptor lookups clone descriptor bytes, and `Ros2BridgeFrame.Payload` returns a fresh payload copy on every call. Hot-path bridge internals use `PayloadLength` and `WritePayloadTo`; external callers that need repeated inspection should cache one `Payload` result instead of repeatedly reading the property.

## 10. Secure WebSocket Mode

`FoxgloveManager` can run either plain `ws://` or secure `wss://` for one manager instance. The default remains plain `ws://127.0.0.1:8765` for existing scenes. Selecting `SecureWebSocket` constructs `ManagedWssBackend` and injects it into `FoxgloveRuntime`; it does not rely on the runtime default constructor.

WSS mode uses a PFX certificate with a private key. The optional root CA distributor is an HTTP bootstrap helper only. Users must compare the displayed SHA-256 fingerprint before importing or trusting the CA. Binding the distributor to `0.0.0.0` exposes the CA download page to the network and should be used only on trusted lab networks.

The optional query token is a lightweight connection gate. It is protected in transit only after TLS is established. Do not treat it as OAuth, mTLS, or user identity. URLs and Inspector labels redact token values.

## 11. IL2CPP Preservation

Unity2Foxglove supports Protobuf, JSON, and productized ROS2 CDR channels. Protobuf is the default for publishers that support it; JSON remains available for compatibility, debugging, and JSON-only publishers. ROS2 uses `ros2msg` schema metadata with CDR payloads for the supported publisher set. IL2CPP builds still need preservation rules for Newtonsoft.Json and the SDK runtime assembly because the package keeps JSON paths available.

The practical build checklist is in [09_IL2CPP_Build_Guide](09_IL2CPP_Build_Guide.md).

## 12. Extension Points

Common extension points:

- Add a new publisher component by deriving from `FoxglovePublisherBase`.
- Register Parameters and Services through `FoxgloveManager`.
- Add FoxRun attributes for debug values.
- Add MCAP replay adapters for custom scene objects.
- Add asset roots for file-backed resources.

## 13. Compatibility Notes

- SDK core targets Unity 6000.0 LTSC or later. Unity 2022 is not supported.
- The demo project is developed on Unity 6000.3.14f1 LTSC; compatible with Unity 6000.0.74f1 LTSC.
- WebGL is excluded.
- Foxglove WebSocket is bidirectional: Unity publishes data, Foxglove can send Parameters, Services, playback, assets, and client-published messages when those features are enabled.
