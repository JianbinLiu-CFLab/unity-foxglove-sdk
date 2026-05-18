
This roadmap summarizes how Unity2Foxglove reached v1.0.0 and where it may go next. It is intentionally higher level than the private phase plans: the goal is to give users and contributors useful context without exposing day-to-day development notes.

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

- Protobuf schema catalog and protobuf publishing are implemented (Phase 32).
- Expand examples for Assets, Playback Control, Client Publish, Parameters, Services, and Connection Graph.
- Optional Unity-native `wss://` support, the local development certificate generator, Root CA distributor, hosted-browser Origin allowance, and shared token gate are implemented in v1.4.0. Future work should focus on clearer remote-access guidance and production deployment boundaries; local demos continue to default to `ws://127.0.0.1`.

### 2.4 MCAP and Replay

- MCAP Attachment/AttachmentIndex and summary CRC are implemented (Phase 34).
- MCAP writer hot-path allocation was reduced (Phase 37).
- Add more replay adapters for Unity scene objects and custom user data.
- Consider extracting reusable C# MCAP pieces if they become useful outside Unity.

### 2.5 Developer Experience

- Transport health/observability Inspector section is implemented (Phase 36).
- Improve `[FoxRun]` diagnostics and generated-source visibility.
- Keep reducing setup friction for users who only want "press Play and connect Foxglove."

### 2.6 ROS2 and Data-Exchange Exploration

- The current ROS2 bridge is optional, disabled by default, and sidecar-based. It is intended for local ROS2 graph integration without changing the default Foxglove WebSocket or MCAP workflows.
- The project is evaluating RobotecAI ROS2 For Unity as an optional standalone path where Unity can participate directly as a ROS2 node without installing ROS2 on the Windows Unity machine. This supersedes the embedded rclcpp spike route as the preferred product investigation because the normal SDK package must remain ROS-free by default.
- The first interop gate is Windows Unity against Ubuntu 24.04 / ROS2 Jazzy, with ROS2 Humble as a fallback because the upstream ROS2 For Unity 1.3.0 standalone Windows asset is Humble-based. ROS2 For Unity is Apache-2.0 licensed and must remain clearly attributed if any future adapter or bundled artifact is added.
- The project is also considering a broader data-exchange runtime model where Unity, Foxglove, MCAP, and ROS2 can act as configurable inputs and outputs. Possible future directions include ROS2 subscriptions into Unity, MCAP replay fanout to ROS2, and route policies that send the same topic stream to Foxglove, MCAP, ROS2, or Unity scene adapters.
- RViz2-oriented standard ROS2 message mirrors, such as `sensor_msgs`, `tf2_msgs`, and `visualization_msgs`, are being considered separately from the existing `foxglove_msgs` bridge path.

## 3. Long-Term Ideas

- Evaluate whether parts of the project should become standalone C# Foxglove protocol or MCAP libraries.
- Keep the managed C# backend as the default path, with native backend exploration only if performance or platform needs justify it.
- Explore higher-level logging APIs that feel closer to modern visual-debugging workflows while still using Foxglove and MCAP standards underneath.
- Explore native ROS2 integration only if it can remain optional, well-isolated, and compatible with normal Unity package usage without ROS installed.

## 4. Development Notes

Detailed phase plans are kept as private development notes. If you need implementation history, design context, or phase-by-phase details, please contact the author.
