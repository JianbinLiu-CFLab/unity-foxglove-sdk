# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/).

---

## 1.9.0 - 2026-05-22

### Added

- Optional ROS2 For Unity package line with a lightweight adapter package and a separate Jazzy Windows x64 runtime package.
- Runtime package metadata for the Jazzy Windows x64 path, including manifest, file inventory, checksum, third-party notices, and package-path loading patch.
- FoxRun canonical manifest, runtime schema info, MCAP schema metadata, schema evidence sidecar, and replay schema identity guard coverage.
- Replay pose ownership arbitration and replay batch-boundary hardening so same-`LogTime` message groups are not split by tick caps.
- FoxRun analyzer and generation-model hardening, including unsupported-type diagnostics and IL2CPP-oriented generated-source equivalence checks.

### Changed

- Root README support boundaries now describe the 1.9 ROS2 For Unity adapter/runtime split instead of treating runtime packaging as only future work.
- The `Not Supported` list now distinguishes unsupported general ROS2 For Unity platform/distro coverage from the supported Jazzy Windows x64 preview runtime path.
- Package metadata, README version references, changelog, and release notes are synchronized for v1.9.0.

### Verified

- Version synchronization dry-run reported all 1.9.0 references already aligned.
- Runtime validation suite passed.
- Release package validation passed with 31 checks.
- Manual acceptance should cover Unity Play Mode, Foxglove, IL2CPP, MCAP replay, and the optional R2FU Jazzy Windows x64 adapter/runtime smoke path before publishing.

## 1.8.0 - 2026-05-17

### Added

- Optional ROS2 bridge output path that mirrors selected Unity publisher CDR payloads to a localhost ROS2 sidecar.
- ROS2 message schema catalog, minimal CDR writers, and publisher integration for transform, scene cube, camera, camera calibration, laser scan, raw point cloud, and Draco point cloud payloads.
- ROS2 bridge topic namespace controls, per-publisher bridge topic overrides, simple QoS presets, queue/drop/failure counters, and Inspector health diagnostics.
- ROS2 bridge sample scene, Foxglove layout, launch file, WSL-friendly run scripts, and package documentation for manual validation.
- Release evidence checks covering package docs, bridge sample assets, sidecar launch behavior, layout topic wiring, and ROS2 bridge acceptance artifacts.

### Changed

- ROS2 bridge health checks now keep Windows-local `ros2` CLI setup separate from a valid WSL sidecar workflow.
- The ROS2 bridge sample layout uses direct Foxglove WebSocket topics, while ROS2 sidecar topics keep their `/unity2foxglove` namespace.
- Scene cube frame ids are sanitized consistently so transform and scene entity frames align in Foxglove 3D.
- Package metadata, README version references, changelog, and release notes are synchronized for v1.8.0.

### Verified

- Runtime validation suite passed.
- Targeted ROS2 bridge validation passed.
- Release package validation passed.
- Manual validation covered Unity Play Mode, Foxglove WebSocket panels, WSL Ubuntu 24.04, ROS2 Jazzy, sidecar launch, ROS2 topic list/info/echo, topic rate checks, QoS inspection, camera, laser scan, point cloud, scene cube, and Foxglove 3D rendering.

## 1.7.0 - 2026-05-17

### Added

- Optional Draco output mode for `FoxglovePointCloudPublisher`, publishing `foxglove.CompressedPointCloud` protobuf payloads on `/unity/point_cloud_draco`.
- Bundled Windows native Draco plugin `Unity2FoxgloveDracoNative.dll` for in-process XYZ point-cloud compression.
- Runtime P/Invoke wrapper and Inspector `Check Draco` validation for the bundled native plugin.
- Compressed point-cloud smoke probes, MCAP inspection tooling, and Phase 87-89 validation coverage.

### Changed

- Raw `foxglove.PointCloud` remains the default path, while Draco is available as an opt-in point-cloud output mode.
- The hidden Phase 87 compressed point-cloud spike publisher now uses the bundled native plugin path instead of requiring a helper executable.
- Documentation, troubleshooting, Inspector reference, evidence templates, and third-party notices now describe the bundled Draco DLL flow.

### Verified

- Runtime validation suite passed.
- Phase 87, Phase 88, and Phase 89 targeted validation passed.
- Release package validation passed.
- Manual Foxglove validation confirmed Inspector Draco availability, `/unity/point_cloud_draco` schema, and 3D point-cloud rendering.

## 1.6.0 - 2026-05-16

### Added

- MCAP indexed reader surface with summary, metadata, attachment, chunk, message, and CRC validation paths exposed for replay preflight and smoke testing.
- Camera output modes for `foxglove.CompressedVideo`, including H.264 via FFmpeg, H.265/HEVC via FFmpeg, H.264 via Cisco OpenH264 runtime installation, and an experimental Windows Media Foundation H.264 backend.
- Subscription-aware heavy-topic demand gating so expensive camera, scene, laser, transform, and point-cloud payload work runs only when a subscriber or MCAP recorder needs it.
- Global default publisher rate policy and fixed-rate next-due scheduling to keep live publish cadence stable under variable Unity frame rates.
- First-layer high-throughput point-cloud QoS with point-count budget, packed-byte budget, first-point sampling, uniform-stride sampling, voxel-grid LOD, and a protocol-level point-cloud smoke probe.
- Demo point-cloud smoke source for visible 1000-point Foxglove 3D panel validation.

### Changed

- Reorganized the manager Inspector into workflow-oriented sections and added focused Inspector surfaces for MCAP replay preflight, camera output selection, and point-cloud QoS.
- Clarified FFmpeg setup as manual guidance rather than automatic installation, while keeping OpenH264 runtime installation explicit and pinned.
- Synchronized package metadata, README version references, changelog, and release notes for v1.6.0.

### Fixed

- Improved live publish cadence so configured rates no longer collapse because of frame-to-frame remainder loss.
- Reduced unnecessary heavy-topic work when Foxglove panels are not subscribed, improving camera and point-cloud runtime behavior.

### Verified

- Runtime validation suite passed.
- Release package validation passed.
- Manual Foxglove smoke validation covered H.264/H.265/OpenH264 camera video and point-cloud QoS visualization.

## 1.5.0 - 2026-05-14

### Added

- 200-series follow-up note for the multi-client playback-control timeout storm observed when Foxglove Desktop and Foxglove Web are connected at the same time.

### Changed

- Reorganized runtime and editor package folders by feature/category while preserving namespaces, public APIs, serialized fields, and Unity `.meta` GUIDs.
- Renamed broad source roots to clearer package-facing names, including `Runtime/Components`, `Runtime/Utilities`, and `Runtime/Schemas/MessageDefinitions`.
- Grouped generated protobuf artifacts under `Runtime/Schemas/Proto/Generated/Messages` and descriptor artifacts under `Runtime/Schemas/Proto/Generated/Descriptors`.
- Grouped MCAP I/O, Core session/replay/recording assets, Transport WebSocket/security/clock code, Editor tooling, and component-side publishers/adapters into focused folders.
- Synchronized package metadata, README version references, changelog, and release notes for v1.5.0.

### Fixed

- Reduced normal WSS client churn noise by keeping TLS/WebSocket pre-handshake disconnect diagnostics quiet by default, with opt-in logging still available for debugging.

### Verified

- Runtime validation suite passed.
- Release package validation passed.
- Quick performance baseline passed.

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
