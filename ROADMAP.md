
This roadmap summarizes how Unity2Foxglove reached v1.0.0 and where it may go next. It is intentionally higher level than the private development notes: the goal is to give users and contributors useful context without exposing day-to-day implementation history.

## 1. Completed in v1.0.0

### 1.1 Protocol Foundation

- Implemented the Foxglove WebSocket server path directly inside Unity.
- Added schema advertisement, channel registration, subscriptions, and typed message publishing.
- Built the managed WebSocket backend used by both Editor and standalone Player builds.

### 1.2 Unity Visualization

- Added Unity publishers for transforms, scene primitives, camera images, and debug data.
- Added coordinate conversion options for Unity/Foxglove axis conventions.
- Added UPM samples for both minimal visualization and the full demo workflow.

### 1.3 Runtime Interaction

- Added Foxglove Parameters and Services support for live control from Foxglove.
- Added demo controls for cube color, scale, pose reset, and runtime state inspection.
- Added connection graph and client-publish support for richer Foxglove protocol coverage.

### 1.4 FoxRun Debug Publishing

- Added `[FoxRun]` attribute-based debug publishing for lightweight runtime telemetry.
- Added source generation for Editor workflows.
- Added generated-source fallback for IL2CPP Player builds.

### 1.5 MCAP Recording and Replay

- Added MCAP writer support with schemas, channels, chunks, indexes, summaries, and compression.
- Added MCAP reader and replay engine for loading, seeking, playing, pausing, and replaying recorded sessions.
- Added recording coverage for Unity-published data, parameters, services, client-published data, and coordinate metadata.

### 1.6 Packaging, CI, and Release Hardening

- Migrated the package to `dev.unity2foxglove.sdk`.
- Added package metadata, samples, license files, release notes, third-party notices, and CI checks.
- Hardened IL2CPP behavior, package paths, WebSocket parsing, MCAP bounds checks, and sample import workflows.

### 1.7 High-Throughput Point Clouds

- Added point-cloud QoS controls for point-count, packed-byte, stride, and voxel-grid sampling.
- Added optional Draco compressed point-cloud output with a bundled Windows native plugin.
- Kept raw `foxglove.PointCloud` as the default path while allowing `foxglove.CompressedPointCloud(draco)` as an opt-in mode.

### 1.8 Optional ROS2 Bridge

- Added an optional localhost ROS2 bridge mirror path for selected Unity publishers.
- Added ROS2 message schema catalog support, CDR payload generation, bridge topic namespace controls, per-publisher topic overrides, simple QoS presets, and bridge health diagnostics.
- Added a ROS2 bridge sample scene, Foxglove layout, launch file, WSL-friendly scripts, and manual validation notes for Unity, Foxglove, WSL Ubuntu, and ROS2 Jazzy.

## 2. Candidate Future Work

### 2.1 Documentation and Onboarding

- Keep improving package docs, demo docs, and sample docs from the user's point of view.
- Add more screenshots, short walkthroughs, and troubleshooting notes based on real user feedback.
- Keep private development plans separate from public documentation.

### 2.2 Platform Validation

- Validate more Unity versions beyond the current Unity 6000.0+ package target.
- Expand IL2CPP checks across Windows, Linux, and macOS.
- Add clearer guidance for package installation in fresh Unity projects.

### 2.3 Protocol Coverage

- Protobuf schema catalog and protobuf publishing are implemented.
- Expand examples for Assets, Playback Control, Client Publish, Parameters, Services, and Connection Graph.
- Optional Unity-native `wss://` support, the local development certificate generator, Root CA distributor, hosted-browser Origin allowance, and shared token gate are implemented in v1.4.0. Future work should focus on clearer remote-access guidance and production deployment boundaries; local demos continue to default to `ws://127.0.0.1`.

### 2.4 MCAP and Replay

- MCAP Attachment/AttachmentIndex and summary CRC are implemented.
- MCAP writer hot-path allocation was reduced.
- Add more replay adapters for Unity scene objects and custom user data.
- Consider extracting reusable C# MCAP pieces if they become useful outside Unity.

### 2.5 Developer Experience

- Transport health/observability Inspector section is implemented.
- Improve `[FoxRun]` diagnostics and generated-source visibility.
- Keep reducing setup friction for users who only want "press Play and connect Foxglove."

### 2.6 ROS2 and Data-Exchange Exploration

- The current ROS2 bridge is optional, disabled by default, and sidecar-based. It is intended for local ROS2 graph integration without changing the default Foxglove WebSocket or MCAP workflows.
- RobotecAI ROS2 For Unity is the preferred ROS2 product mainline because it supports the goal that Unity users should not install ROS2 locally on the Windows Unity machine. This supersedes the embedded rclcpp spike route as historical evidence rather than the product path.
- The repository uses a one-repo, multi-package model: `dev.unity2foxglove.sdk` remains the ROS-free core package; `dev.unity2foxglove.ros2forunity` owns facade, adapter samples, docs, and diagnostics; `dev.unity2foxglove.ros2forunity.runtime.jazzy.win64` owns the Jazzy Windows x64 runtime artifacts.
- Packages work independently and compose cleanly: the core SDK works by itself, the ROS2 For Unity adapter package compiles and reports a missing runtime gracefully, a runtime package exposes metadata/diagnostics by itself, and adapter plus runtime enables Unity-as-ROS2-node publish/subscribe.
- The delivered ROS2 For Unity runtime package (`dev.unity2foxglove.ros2forunity.runtime.jazzy.win64`) includes runtime inventory, SHA-256 checksum, third-party notices, license inventory, and has passed fresh-project Unity acceptance plus bidirectional `std_msgs/msg/String` data-path smoke through Windows ROS2 Jazzy. Humble remains legacy/fallback evidence, not the recommended new-user runtime line.
- The ROS2 For Unity direction is Jazzy-first for Windows x64. Linux, macOS, Humble, Lyrical, and alternate RMW implementations are not yet included.
- Windows Firewall may block inbound UDP for ROS2 For Unity DDS discovery; configure Inbound Allow rules for the Unity Editor or use a Fast DDS Discovery Server. WSL2 is a valid topology with proper firewall configuration.
- Standard-message, RViz2, rosbag2, MarkerArray, PointCloud2, and MCAP fanout plans are deferred until the ROS2 For Unity adapter/runtime package line is stable.

## 3. Long-Term Ideas

- Evaluate whether parts of the project should become standalone C# Foxglove protocol or MCAP libraries.
- Keep the managed C# backend as the default path, with native backend exploration only if performance or platform needs justify it.
- Explore higher-level logging APIs that feel closer to modern visual-debugging workflows while still using Foxglove and MCAP standards underneath.
- Explore native ROS2 integration only if it can remain optional, well-isolated, and compatible with normal Unity package usage without ROS installed.

## 4. Development Notes

Detailed planning notes are kept private. If you need implementation history or design context, please contact the author.
