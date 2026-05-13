# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/).

---

## 1.4.0 - 2026-05-13

### Added

- Unity-native secure WebSocket mode through `ManagedWssBackend`, using PFX certificates and `SslStream` while reusing the managed WebSocket protocol path.
- Optional shared query-token gate with fixed-time comparison and redacted Inspector/log display.
- Root CA distributor and Inspector local-development certificate generation workflow for WSS smoke tests.
- FoxRun `OnTrigger` publish mode for explicit event-style telemetry snapshots.
- Paused MCAP replay scrubbing with scene-only latest-at snapshots and bounded panel-history rebuild after seek debounce.

### Changed

- `FoxgloveManager` now exposes `WebSocket` / `SecureWebSocket` transport mode selection and WSS security fields in the Inspector.
- Hosted Foxglove Web URL generation now matches the official plain-loopback behavior and switches to `wss://` only when secure mode is selected.
- Replay seek handling now clears stale data-priority queues, broadcasts `didSeek` playback state, and suppresses live publishers during replay more consistently.
- Script documentation now points to the reorganized `Scripts/release`, `Scripts/build_tools`, `Scripts/performance`, and `Scripts/smoke` entry points.

### Fixed

- Hardened WebSocket handshake limits, playback-clock reads, service lifecycle cleanup, client-publish state cleanup, replay bounds checks, and generated FoxRun source escaping.

### Verified

- Runtime validation suite should be run before tagging this release.
- Release package validation should be run before tagging this release.
- Manual WSS and replay-scrub smoke tests should be repeated before publishing binary evidence.

## 1.3.0 - 2026-05-12

### Added

- Internal release-document and package-metadata synchronization for the v1.3.0 package line.

### Changed

- README badges, package metadata, and release note links were aligned for v1.3.0.

### Verified

- Runtime validation suite should be run before tagging this release.
- Release package validation should be run before tagging this release.

## 1.2.0 - 2026-05-11

### Added

- Dedicated typed sensor publishers for `foxglove.PointCloud`, `foxglove.LaserScan`, and `foxglove.CameraCalibration`.
- Shared message builders for point cloud, laser scan, and camera calibration payload construction.
- Runtime validation and manual acceptance notes for Phase 49 sensor typed publisher parity.

### Fixed

- Completed the `foxglove.PointCloud` JSON schema field item metadata so Foxglove Desktop can parse JSON mode.

### Verified

- Runtime validation suite: `All checks passed.`
- Release package validation: `27 check(s) passed.`
- Manual FullDemo smoke verified PointCloud, LaserScan, and CameraCalibration in Protobuf and JSON modes.

## 1.1.0 - 2026-05-10

### Added

- Camera protobuf parity: `FoxgloveCameraPublisher` can publish `/unity/camera` as official `foxglove.CompressedImage` protobuf.
- Shared camera protobuf builder for constructing `foxglove.CompressedImage` messages with raw JPEG bytes in the official `bytes data` field.
- Runtime validation for camera protobuf payload roundtrip, timestamp mapping, frame ID, format, and raw byte preservation.

### Changed

- New `FoxgloveManager` components now default Publisher Encoding to Protobuf.
- JSON camera publishing remains available through Manager or per-publisher encoding override.
- JSON-only publishers continue to fall back to JSON automatically under the Protobuf default.
- Schema coverage documentation now identifies protobuf as the preferred camera streaming path.

### Verified

- Runtime validation suite: `All checks passed.`
- Release package validation: `27 check(s) passed.`
- Quick performance baseline: `10/10 PASS`.

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
- WSS/TLS and authentication are not implemented in v1.0.0. This historical limitation changed in v1.4.0: optional Unity-native WSS/TLS and a lightweight shared query-token gate are now available, but production authentication/authorization is still out of scope.
- Native Backend (C implementation) has not yet been integrated into the transport layer
