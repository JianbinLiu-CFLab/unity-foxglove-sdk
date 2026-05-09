# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/).

---

## 1.0.0 - 2026-05-09

### Added

- WebSocket server: pure C# implementation of RFC 6455, supporting the Foxglove WebSocket protocol (subprotocol `foxglove.sdk.v1`)
- JSON and Protobuf encoding: JSON serialization for all Foxglove schema messages; Protobuf with official schema catalog (46 schemas) and encoding policy
- Schema support: `foxglove.FrameTransform`, `foxglove.SceneUpdate`, `foxglove.CompressedImage`, `foxglove.Log` (JSON); full official Foxglove proto catalog
- Unity MonoBehaviour integration: `FoxgloveManager`, `FoxgloveTransformPublisher`, `FoxgloveSceneCubePublisher`, `FoxgloveCameraPublisher`, `ProtobufPublisher<T>`
- Publisher encoding policy: global manager default, per-publisher override, dual-format Transform/SceneCube, Protobuf-only generic publisher
- FoxRun: `[FoxRun]` attribute for one-line auto-publish to Foxglove topics, with dual-track Roslyn ISG + Player build `.g.cs` fallback
- Parameters: `getParameters` / `setParameters`, with `parametersSubscribe` / `parametersUnsubscribe` for real-time push
- Services: `advertiseServices` / `unadvertiseServices` / `callService`, main-thread-safe `DrainServiceCalls()` dispatch
- ConnectionGraph: publisher/subscriber topology broadcast
- ClientPublish: Foxglove-to-Unity message publishing
- Assets: `fetchAsset` support with configurable multiple Asset Roots
- PlaybackControl: playback control command support
- MCAP Writer: real-time WebSocket message recording to .mcap files
- MCAP Reader: .mcap file parsing, extracting Schema/Channel/Message/Attachment records
- MCAP Replay: replay recorded files to Foxglove
- MCAP compression: LZ4 and Zstd compression support
- MCAP Attachment/AttachmentIndex: record and index arbitrary binary attachments
- MCAP chunk CRC: generation and validation for chunk integrity
- MCAP summary CRC: Footer `summary_crc` generation and reader validation
- MCAP writer hot-path allocation reduction: direct-write message records into chunk buffer
- Transport backpressure: per-client bounded send queues with Control/Data priority, drop-oldest policy, slow-client disconnect
- Transport health/observability: read-only transport stats snapshot API and Inspector health section
- Performance baseline: quick/full .NET harness with JSON output and Python scripted runner
- IL2CPP build: link.xml preservation, batch build editor script
- `FoxgloveParameterComponent`: drag-and-drop parameter exposure component
- Automated dotnet tests covering all functional modules
- Demo project (`Unity2Foxglove`): ready-to-run demonstration scene
- Sample (`BasicVisualization`): minimal setup example
- Sample (`FullDemoVisualization`): complete experience with Parameters, Services, FoxRun, MCAP
- Logger bridge: `IFoxgloveLogger` interface, making protocol errors traceable in Unity Console, dotnet tests, and IL2CPP Player

### Changed

- Package renamed from `dev.foxglove.sdk` to `dev.unity2foxglove.sdk`
- Removed dependency on external Python bridge process; WebSocket server runs in-process in Unity
- Refactored Transport abstraction layer, supporting Managed Backend (pure C#) and Native Backend (reserved)

### Fixed

- Phase 16 code review: various code quality improvements, null checks, resource disposal

### Known Limitations

- WebGL platform is not supported (depends on `TcpListener`)
- macOS / Linux platforms have not been verified
- WSS/TLS and authentication are not implemented
- Native Backend (C implementation) has not yet been integrated into the transport layer
