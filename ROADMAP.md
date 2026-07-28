# Unity2Foxglove Roadmap

Updated: 2026-07-20

This public roadmap describes the current development baseline and the remaining product directions. It is intentionally higher level than private implementation plans. A tagged release can lag this document; release notes remain the authority for the exact contents of a published package.

## 1. Status at a Glance

| Area | Current status | Remaining boundary |
| --- | --- | --- |
| Foxglove WebSocket protocol | Product baseline complete | Continue parity work only when it supports a concrete Unity workflow. |
| FoxRun generated bindings | Bidirectional development baseline complete | Broader complex-type depth, clearer diagnostics, and cross-target emitter reuse. |
| MCAP recording and replay | Recording, indexed reading, scene reproduction, and Foxglove-owned timeline workflow complete | Large-file latency, multi-file/search workflows, and explicit deterministic-simulation contracts. |
| Unity data transport | Independent output/input configuration, coordinate modes, recording boundary, and Inspector workflows complete | More end-user onboarding and cross-platform evidence. |
| Optional ROS2 For Unity | Windows x64 Humble, Jazzy, and Lyrical package lines plus built-in/custom typed transport implemented | Windows Player and Linux-peer certification, Linux packaging, and broader redistribution evidence. |
| Camera and point cloud | Async camera, raw/compressed point-cloud, QoS, sampling, and native ROS2 paths implemented | In-Unity renderer, repeatable multi-LiDAR fixtures, remote QoS, and hardware validation. |
| Security | Local WSS, certificate tooling, Origin allowance, and token gates implemented | Identity and authorization for untrusted remote deployment. |
| Platform support | Windows Editor/Player paths receive the strongest current evidence | Expand IL2CPP and package acceptance on Linux and macOS before claiming support. |

“Complete” in this table means the architecture and repository implementation exist with automated evidence. Platform-specific support is claimed only where its separate acceptance cell has actually run.

## 2. Current Product Baseline

### 2.1 In-Process Foxglove Runtime

- A managed Foxglove WebSocket server runs directly inside Unity Editor and standalone Players.
- The runtime covers server info, schemas, channels, subscriptions, time, status messages, graph updates, client publish, assets, parameters, services, and playback control used by the Unity workflows.
- JSON and Protobuf are first-class wire formats. Typed schema catalogs and generated descriptors keep the runtime contract visible to Foxglove and MCAP.
- Local development defaults remain simple: add `FoxgloveManager`, press Play, and connect Foxglove. WSS, token, certificate, and Origin controls are available when the deployment requires them.

### 2.2 FoxRun: One Declaration, Two Directions

FoxRun is now a generated field/property binding rather than a publish-only shortcut:

```csharp
using static Unity.FoxgloveSDK.Components.FoxRunFlow;
using static Unity.FoxgloveSDK.Components.FoxRunPolicy;

[FoxRun("/robot/pose")]
private PoseState _pose;

[FoxRun("/robot/state", Mode = Subscribe, Policy = Change, Hz = 30)]
private RobotState _state;

[FoxRun("/debug/state", Mode = PublishAndSubscribe, Policy = FixedRate, Hz = 10)]
private DebugState _debugState;
```

The current declaration model includes:

- `Publish`, `Subscribe`, and debug-oriented `PublishAndSubscribe` flows;
- `FixedRate`, `Change`, and `Trigger` policies, with `Change + Hz` providing
  an explicit heartbeat;
- one resolved subscription source per member and multiple simultaneous output sinks;
- independent direction scheduling under one explicit full-duplex policy/rate;
- JSON and typed Protobuf WebSocket input/output;
- generated input admission, latest-wins staging, main-thread apply, and echo suppression;
- static custom DTO mapping for optional native ROS2 transport;
- one shared emitter used by the Roslyn Editor host and the physical IL2CPP source host.

The compact surface and its runtime safety rules are documented in [Shared-Emitter Dual-Host AOT Code Generation for Bidirectional Unity Telemetry](docs/research-shared-emitter-architecture.md).

### 2.3 MCAP Recording, Analysis, and Scene Reproduction

The MCAP path includes:

- chunked recording, indexes, summaries, LZ4/Zstd compression, attachments, and summary CRC;
- bounded reading, seeking, history queries, and decoded JSON/Protobuf/ROS2 CDR paths;
- canonical schema evidence, `.schema` sidecars, and Off/Warn/Strict replay identity policy;
- directional and coordinate-mode metadata at the external data boundary;
- replay scene adapters for transforms, scene state, camera, IMU, point cloud, and other registered behaviors;
- deterministic pose-source arbitration, initial-batch deferral, positive/negative target caches, and bounded callback queues.

For interactive analysis, Foxglove opens the MCAP through the Manager's local Remote files URL. The `Unity Replay Sync` panel forwards Foxglove's global cursor to Unity, so the playback bar and Plot-driven seek control Unity scene time. Foxglove remains the time owner; Unity applies latest-at snapshots for seeks and complete forward ranges for normal playback. See [Foxglove-Owned Timeline and Deterministic Unity Scene Reproduction](docs/research-remote-timeline-scene-reproduction.md).

This is deterministic state application, not deterministic execution of Unity physics, random state, user scripts, or external services.

### 2.4 Data Transport and High-Rate Sensors

- The Manager Inspector groups `Publish Data` and `Subscribe Data` under one `Data Transport` workflow.
- Output destinations remain independently selectable: Foxglove WebSocket, ROS2 Native, and ROS2 Bridge can coexist where the contract supports them.
- Input and output coordinate modes are separate because conversion responsibility reverses with direction.
- MCAP records the external boundary representation rather than applying an extra replay conversion.
- Camera publishing supports bounded async JPEG work and optional video sidecar modes.
- Point-cloud publishing includes point/byte budgets, stride and voxel sampling, raw `foxglove.PointCloud`, optional Draco compression, native ROS2 `PointCloud2`, and throughput instrumentation.

### 2.5 Optional ROS2 Package Line

The core SDK remains ROS-free. Native ROS2 code is isolated into optional packages:

This remains a one-repo, multi-package design. The ROS2 For Unity adapter/runtime package line is the optional native mainline and supersedes the embedded rclcpp spike route. It builds on RobotecAI ROS2 For Unity under its Apache-2.0 boundary; Humble, Jazzy, and Lyrical are package/runtime choices rather than branches in the core SDK. Broad ROS2 schema expansion remains deferred until a concrete Unity workflow and acceptance fixture require it.

| Package family | Role |
| --- | --- |
| `dev.unity2foxglove.sdk` | Foxglove WebSocket, MCAP, Replay, FoxRun, and normal Unity workflows. |
| `dev.unity2foxglove.ros2forunity` | ROS2 For Unity facade, lifecycle, generated binding integration, diagnostics, and samples. |
| `runtime.humble.win64`, `runtime.jazzy.win64`, `runtime.lyrical.win64` | Mutually exclusive Windows x64 ROS2 runtime artifacts and capability metadata. |
| `foxrun.ros2.interfaces` | Static generated custom FoxRun ROS2 interface package. |
| distro-specific `foxrun.ros2.interfaces.typesupport.*.win64` | Matching custom-interface native/managed typesupport add-ons. |

The implemented path supports existing ROS2 message types and generated custom FoxRun DTO interfaces. Runtime/RMW selection is capability-driven rather than hard-coded into the core SDK.

The Windows-local Unity Editor matrix has passed four real peer rows:

- Humble + FastDDS;
- Jazzy + FastDDS;
- Lyrical + FastDDS;
- Lyrical + Zenoh with an owned router.

That evidence covers the local Editor data path and graph/type/QoS observations. It does not certify Windows Player, Linux peer, Linux Player, or cross-machine discovery.

### 2.6 Packaging, Evidence, and Maintenance

- The repository uses one source repository with a ROS-free core package and separately versioned optional integration/runtime packages.
- CI separates default, adapter, and native compile/test lanes and keeps all build output below the repository `build/` root.
- Schema manifests, artifact inventories, checksums, third-party notices, analyzer freshness, package validators, MCAP conformance, and Unity manual acceptance form separate evidence layers.
- Large runtime packages and generated artifacts are validated as package content; temporary colcon, CMake, Unity, `bin`, and `obj` output is not source.

## 3. Near-Term Priorities

### 3.1 FoxRun and Source-Generation Platform

1. Converge reusable source-generator build plumbing across related Unity telemetry targets.
2. Expand explicitly supported complex-type depth while retaining bounded generated copy/dispose behavior.
3. Extract a clearer multi-backend emitter seam only after shared infrastructure and type semantics are stable.
4. Improve diagnostics, generated-source visibility, and first-error guidance without growing a second declaration model.

### 3.2 Onboarding and Trust

- Reduce first-success setup to a small, verifiable Unity workflow.
- Keep Inspector terminology aligned with the user's data flow rather than internal transport classes.
- Add screenshots and short walkthroughs for FoxRun input/output, Foxglove timeline replay, and optional ROS2 setup.
- Publish platform and package evidence with explicit scope instead of presenting raw test counts as support claims.

### 3.3 MCAP and Replay

- Measure and reduce large-MCAP scrub latency.
- Add user-driven recording search, hosted/range access, and multi-file timeline workflows.
- Expand replay adapters only for concrete Unity scene or custom-data use cases.
- Keep deterministic simulation/physics replay as a separate contract requiring captured inputs, seeds, lifecycle rules, and external-system ownership.
- Evaluate a reusable managed MCAP library boundary only after dependencies can be separated cleanly from Unity lifecycle and component code.

### 3.4 Cross-Platform Release Evidence

- Run IL2CPP Player and fresh-package acceptance across Windows, Linux, and macOS.
- Complete the pending custom ROS2 Windows Player and Linux-peer matrix cells before promoting those claims.
- Evaluate Lyrical Ubuntu 26.04 standalone/runtime feasibility as an optional package track.
- Verify artifact inventories, licenses, checksums, and native dependency closure independently for every platform package.

### 3.5 Remote Deployment Security

- Define identity, authorization, credential rotation, audit, and deployment ownership before enabling untrusted remote control.
- Keep loopback/local-development defaults separate from a production remote-access claim.
- Do not treat a bearer token or self-signed local certificate as a complete multi-user authorization system.

### 3.6 Point-Cloud and Multi-Sensor Track

1. Retain the current measured high-throughput publication path and add remote-QoS evidence only from real networks.
2. Build a reusable in-Unity point-cloud renderer.
3. Add repeatable bag-based multi-LiDAR fixtures and TF integration.
4. Run Ouster/Livox hardware validation only after the renderer and recorded fixtures are stable.

## 4. Longer-Term Candidates

These are options, not release promises:

- semantic telemetry graphs and run manifests;
- indexed MCAP query and differential trace comparison;
- runtime insight dashboards and rule/anomaly-driven capture;
- bounded local agent/MCP query surfaces for existing telemetry and replay state;
- cross-project emitter reuse for Foxglove/MCAP and Rerun/RRD;
- standalone managed protocol or MCAP packages if a clean non-Unity consumer emerges.

Each candidate should start from a concrete user workflow, fixture, and acceptance gate rather than a broad parity goal.

## 5. Explicit Non-Goals

- Replacing the official Foxglove SDK ecosystem or pursuing multi-language SDK parity.
- Making the ROS2 optional package a dependency of the core SDK.
- Supporting simultaneous subscription from multiple providers for one FoxRun member.
- Claiming deterministic simulation from timestamped scene-state replay alone.
- Adding ROS1, every ROS2 schema, or every Foxglove schema without a demonstrated Unity workflow.
- Turning local WSS/token tooling into an unqualified production-security claim.
- Committing generated build, Unity transient, CMake/colcon, `bin`, or `obj` output as source.

## 6. Evidence Policy

Roadmap status follows evidence scope:

- automated behavior tests prove the tested contract, not every Unity/platform combination;
- structural tests prove repository and architecture boundaries, not runtime interoperability;
- conformance tests prove agreement with a specification or independent implementation for the tested fixture;
- Unity/Foxglove/ROS2 manual acceptance proves only the recorded topology and application mode;
- a local Editor PASS never becomes a Player, Linux, macOS, or cross-machine claim without that separate run.

This policy keeps completed work visible while leaving the remaining cells honest.
