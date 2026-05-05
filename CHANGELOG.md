# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/).

---

## 1.0.0 - 2026-05-05

### Added

- WebSocket server: pure C# implementation of RFC 6455, supporting the Foxglove WebSocket protocol (subprotocol `foxglove.sdk.v1`)
- JSON encoding: JSON serialization and deserialization for all Foxglove schema messages
- Schema support: `foxglove.FrameTransform`, `foxglove.SceneUpdate`, `foxglove.CompressedImage`
- Unity MonoBehaviour integration: `FoxgloveManager`, `FoxgloveTransformPublisher`, `FoxgloveSceneCubePublisher`, `FoxgloveCameraPublisher`
- FoxRun: `[FoxRun]` attribute for one-line auto-publish to Foxglove topics, with dual-track Roslyn ISG + Player build `.g.cs` fallback
- Parameters: `getParameters` / `setParameters`, with `parametersSubscribe` / `parametersUnsubscribe` for real-time push
- Services: `advertiseServices` / `unadvertiseServices` / `callService`, main-thread-safe `DrainServiceCalls()` dispatch
- ConnectionGraph: publisher/subscriber topology broadcast
- ClientPublish: Foxglove-to-Unity message publishing
- Assets: `fetchAsset` support with configurable multiple Asset Roots
- PlaybackControl: playback control command support
- MCAP Writer: real-time WebSocket message recording to .mcap files
- MCAP Reader: .mcap file parsing, extracting Schema/Channel/Message records
- MCAP Replay: replay recorded files to Foxglove
- MCAP compression: LZ4 and Zstd compression support (IonKiwi.lz4.dll / ZstdSharp.dll)
- MCAP Attachments: custom metadata attachment during recording
- IL2CPP build: link.xml preservation, batch build via `FoxgloveBuild.BuildWindowsIl2Cpp` editor script
- `FoxgloveParameterComponent`: drag-and-drop parameter exposure component
- 465 automated dotnet tests covering all functional modules
- Demo project (`Untiy2Foxglove`): ready-to-run demonstration scene
- Sample (`BasicVisualization`): minimal setup example
- Logger bridge: `IFoxgloveLogger` interface, making protocol errors traceable in Unity Console, dotnet tests, and IL2CPP Player

### Changed

- Package renamed from `dev.foxglove.sdk` to `dev.unity2foxglove.sdk`
- Removed dependency on external Python bridge process; WebSocket server runs in-process in Unity
- Refactored Transport abstraction layer, supporting Managed Backend (pure C#) and Native Backend (reserved)

### Fixed

- Phase 16 code review: fixed various code quality issues, null checks, resource disposal, etc.

### Known Limitations

- Protobuf binary encoding is not implemented; currently only JSON is supported
- WebGL platform is not supported (depends on `TcpListener`)
- macOS / Linux platforms have not been verified
- Native Backend (C implementation) has not yet been integrated into the transport layer
