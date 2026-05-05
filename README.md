# 1. Unity2Foxglove

[![License](https://img.shields.io/badge/license-Apache%202.0-blue.svg)](LICENSE)
[![Unity](https://img.shields.io/badge/Unity-2022.3%2B-black?logo=unity)](https://unity.com/)
[![.NET](https://img.shields.io/badge/.NET-9.0-purple?logo=dotnet)](https://dotnet.microsoft.com/)
[![Release](https://img.shields.io/badge/release-v1.0.0-green)](https://github.com/JianbinLiu-CFLab/Unity2Foxglove/releases)

A cross-platform Unity SDK for real-time runtime data streaming, MCAP recording and replay, and in-editor debugging. It runs inside Unity, speaks the Foxglove WebSocket protocol directly, and can work with [Foxglove](https://foxglove.dev), MCAP files, or custom clients.

## 1.1 Purpose 

Unity2Foxglove turns your Unity Editor and standalone player into a live data server. It addresses four core needs:

### 1.1.1 Real-time Streaming
- Live WebSocket streaming of Unity runtime data to Foxglove or any compatible client.
- Zero external processes — the server runs entirely in-process.
- Topics update at configurable rates (default 10 Hz), with sub-millisecond timestamp precision.

### 1.1.2 Runtime Debugging
- Replace scattered `Debug.Log` calls with real-time Plots, 3D overlays, and parameter tuning panels.
- Attach `[FoxRun]` to any field and watch it stream live — a [Rerun](https://rerun.io)-like experience directly in your Unity workflow.
- Modify parameters from Foxglove and see changes instantly in Unity, without stopping Play Mode.

### 1.1.3 MCAP Recording and Replay
- Record entire sessions to [MCAP](https://mcap.dev) (MCAP Specification by Foxglove) files with LZ4/Zstd compression.
- **Drive scene reproduction** — Replay recorded MCAP files to drive GameObjects, reconstructing the exact scene state from recorded data. Every Transform update, parameter change, and service call is preserved and replayed in sequence.
- **Why MCAP?**
  - Open format with a well-defined [specification](https://mcap.dev/specification) — no vendor lock-in.
  - Random-access seek via chunk indexes, enabling instant jump to any point in time.
  - Built-in compression (LZ4, Zstd) with per-chunk granularity.
  - Self-describing: schemas and channels are embedded in the file alongside data.
  - Growing ecosystem of readers and tools across Python, Rust, C++, TypeScript, and now C#.
- Replay in Foxglove for visual analysis, or use any MCAP-compatible tool for offline processing.

### 1.1.4 Cross-Platform Data Bridge
- A pure C# WebSocket server that runs on any platform Unity supports (Windows, Linux, macOS).
- No ROS installation, no Python bridge process, no native dependencies required.
- Same code path in Editor, Standalone Player, and IL2CPP builds.

## 1.2 Application Scenarios

Typical scenarios:

- Robotics and autonomous systems: visualize sensor data, debug control loops, tune parameters online, record and replay test runs.
- Game development: monitor gameplay metrics, record playtest sessions for post-mortem analysis and scene reproduction.
- Simulation and digital twins: stream real-time state to external dashboards or analysis pipelines, replay historical runs.
- Unity tooling: expose runtime state through a stable protocol instead of one-off editor windows, UDP scripts, or temporary debug UI.

## 1.3 The Problem

Existing approaches for getting Unity runtime data to external tools share common pain points:

- **ROS2/ROS bridge** — Requires a ROS installation on the host, complex middleware setup, and is effectively Linux-only. Custom message types need code generation and bridge configuration.
- **Third-party SDKs** — Official Foxglove C++/Python SDKs require a separate process running outside Unity, additional serialization steps, and are constrained to the platforms those SDKs support.
- **Ad-hoc UDP/TCP scripts** — Manual socket code, fragile serialization, no schema validation, and no built-in replay or compression.

All of these share the same fundamental problem: **Unity runs in-process, but the data consumer is out-of-process**. The bridge becomes a project of its own — adding complexity, platform constraints, and maintenance burden.

## 1.4 The Solution

Unity2Foxglove embeds the entire stack inside Unity:

```mermaid
flowchart LR
  subgraph Unity["Unity Editor or Player"]
    Manager["FoxgloveManager"]
    Publishers["Publishers and FoxRun sources"]
    Runtime["Runtime services"]
    Recorder["MCAP recorder"]
    Replay["MCAP replay engine"]
  end

  subgraph LiveClients["Live clients"]
    Foxglove["Foxglove"]
    Custom["Custom Foxglove WebSocket clients"]
  end

  subgraph Files["Offline files"]
    McapFile[".mcap recording"]
    McapTools["MCAP tools"]
  end

  Publishers --> Manager
  Runtime --> Manager
  Manager <-->|Foxglove WebSocket| Foxglove
  Manager <-->|Foxglove WebSocket| Custom
  Manager --> Recorder
  Recorder --> McapFile
  McapFile --> Replay
  McapFile --> McapTools
  Replay --> Runtime
```

No external processes. No ROS installation. No platform lock-in. Just attach a `FoxgloveManager` component, press Play, and connect.

The live WebSocket path is intentionally bidirectional:

- Unity publishes topics such as `/tf`, `/scene`, `/unity/camera`, `/debug/*`, connection graph updates, playback state, and service responses.
- Foxglove or a custom client sends subscriptions, parameter reads/writes, service calls, asset fetch requests, playback commands, and optional client-published messages.
- MCAP is a file path, not a WebSocket target: Unity records `.mcap` files, Unity can replay them, and external MCAP tools can inspect them offline.

## 1.5 Project Layout

```mermaid
flowchart TD
  Repo["Unity2Foxglove repository"]
  Package["Packages/dev.unity2foxglove.sdk"]
  Demo["Untiy2Foxglove demo project"]
  Scripts["Scripts"]
  Docs["Documentation"]

  Repo --> Package
  Repo --> Demo
  Repo --> Scripts
  Package --> Docs
  Package --> Samples["Samples~/BasicVisualization"]
  Demo --> DemoDocs["Docs"]
  Scripts --> BuildScript["build_unity_il2cpp.py"]
```

- Use `Packages/dev.unity2foxglove.sdk` when you want to install the SDK into your own Unity project.
- Use `Untiy2Foxglove` when you want a ready-to-open demo project for Foxglove panels, MCAP recording, replay, IL2CPP, and manual acceptance.
- Use `Samples~/BasicVisualization` when you want the smallest package sample that demonstrates the basic publisher setup.

---

## 2. Installation

### 2.1 Use as Unity Package (recommended)

For adding the SDK to your own Unity project.

1. Clone this repository
2. Unity menu: `Window > Package Manager > + > Add package from disk...`
3. Select `Packages/dev.unity2foxglove.sdk/package.json`

Or install via Git URL:

```
https://github.com/JianbinLiu-CFLab/Unity2Foxglove.git?path=Packages/dev.unity2foxglove.sdk
```

### 2.2 Open the Demo Project

For quickly exploring all features without creating a new project.

1. Clone this repository
2. Unity Hub > Open > select the `Untiy2Foxglove` directory
3. The scene is pre-configured with FoxgloveManager and all Publisher components
4. Press Play to start

---

## 3. Quick Connection

1. Open **Foxglove Desktop** or **Foxglove Studio**
2. "Open connection" > select **Foxglove WebSocket**
3. Enter URL `ws://127.0.0.1:8765`
4. The Topics panel will show `/tf`, `/scene`, `/unity/camera`, etc.
5. Switch to the 3D panel and select the `/scene` topic to see the Cube

---

## 4. Current Capabilities

### 4.1 WebSocket Server
- Pure C# RFC 6455 WebSocket server (built on `System.Net.Sockets.TcpListener`)
- Supports the Foxglove WebSocket protocol (subprotocol `foxglove.sdk.v1`)
- Full serverInfo and capabilities declaration

### 4.2 Data Encoding
- JSON encoding for all Foxglove schema messages
- Channel registration and message publishing with JSON schemas

### 4.3 Schema Support
- `foxglove.FrameTransform` — coordinate frame transforms
- `foxglove.SceneUpdate` — 3D scene entity updates
- `foxglove.CompressedImage` — compressed images (JPEG/PNG)
- `[FoxRun]` attribute for zero-code auto-publish of fields to Foxglove topics

### 4.4 Unity Integration
- **FoxgloveManager** — core MonoBehaviour, manages the WebSocket server lifecycle
- **FoxgloveTransformPublisher** — automatically publishes a GameObject's Transform to `/tf`
- **FoxgloveSceneCubePublisher** — publishes a Cube as SceneUpdate messages
- **FoxgloveCameraPublisher** — publishes Camera render output as CompressedImage
- **FoxgloveParameterComponent** — exposes component properties as Foxglove Parameters
- Drag-and-drop configuration, runs with zero code

### 4.5 Parameters
- `getParameters` / `setParameters` — Foxglove reads and modifies Unity parameters
- `parametersSubscribe` / `parametersUnsubscribe` — real-time push of parameter changes
- Supports int, float, string, json, and other types

### 4.6 Services
- Service registration and invocation (`advertiseServices` / `unadvertiseServices` / `callService`)
- Main-thread safety: `DrainServiceCalls()` dispatches callbacks to the Unity main thread

### 4.7 Connection Graph and Client Publish
- **ConnectionGraph** — publishes topology information so Foxglove shows publisher/subscriber relationships
- **ClientPublish** — supports Foxglove publishing messages to Unity

### 4.8 Assets
- `fetchAsset` — Foxglove pulls Unity project asset files by URI
- Supports `asset://` protocol prefix with configurable multiple Asset Roots

### 4.9 PlaybackControl
- Supports playback control commands from Foxglove

### 4.10 MCAP Recording and Replay
- **MCAP Writer** — writes WebSocket session messages to `.mcap` files in real time
- **MCAP Reader** — parses `.mcap` files, extracting Schema/Channel/Message records
- **MCAP Replay** — replays recorded files; Foxglove Studio can open local `.mcap` for visualization
- **Compression** — LZ4 and Zstd compression algorithms (via IonKiwi.lz4.dll and ZstdSharp.dll bindings)
- **MCAP Attachments** — attach custom metadata during recording
- Records: topic messages, Parameters, Services, ClientPublish, ConnectionGraph, and other protocol data

### 4.11 FoxRun Source Generation
- `[FoxRun("/debug/health", RateHz = 5)]` — one-line annotation auto-publishes a field to a Foxglove topic
- Editor: generated in real time by Roslyn Incremental Source Generator (ISG)
- Player builds: `.g.cs` fallback files generated via `IPreprocessBuildWithReport`
- IL2CPP Player build support

### 4.12 IL2CPP Build Support
- Complete `link.xml` preservation
- Verified cross-platform IL2CPP Standalone Player build (Windows, with Linux/macOS support via build scripts)
- Batch build via `Scripts/build_unity_il2cpp.py`

### 4.13 Testing
- 465 automated dotnet tests covering all functional modules
- Test command:
  ```
  dotnet run --project Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj
  ```

---

## 5. Current Limitations

| Limitation | Details |
|------------|---------|
| **Protobuf not supported** | Currently only JSON encoding is supported. Protobuf binary encoding is planned for v1.1.0 |
| **WebGL not supported** | WebGL platform does not support `TcpListener` and cannot run |
| **Native Backend not enabled** | The C native backend has not yet been integrated into the transport layer |

---

## 6. Documentation

The documentation is split into two layers:

```mermaid
flowchart TD
  Start["I want to use Unity2Foxglove"]
  PackageDocs["Package documentation"]
  DemoDocs["Demo project documentation"]
  SampleDocs["BasicVisualization sample"]
  ReleaseDocs["Release and compliance notes"]

  Start --> PackageDocs
  Start --> DemoDocs
  Start --> SampleDocs
  Start --> ReleaseDocs
```

- [Package documentation](Packages/dev.unity2foxglove.sdk/Documentation~/README.md) — SDK concepts, API usage, architecture, FoxRun, MCAP, IL2CPP, and troubleshooting.
- [Demo project](Untiy2Foxglove/README.md) — ready-to-open Unity project for Foxglove operation, manual acceptance, replay, recording, and build verification.
- [Sample: BasicVisualization](Packages/dev.unity2foxglove.sdk/Samples~/BasicVisualization/README.md) — minimal package sample for users who only want the basic publisher setup.
- [Changelog](CHANGELOG.md)
- [Third-party notices](THIRD_PARTY_NOTICES.md)
- [v1.0.0 release notes](RELEASE_NOTES_v1.0.0.md)

---

## 7. Running Dotnet Tests

```bash
dotnet run --project Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj
```

CLI arguments (for manual verification):
- `--serve --port 8765` — start an empty server
- `--serve --port 8765 --demo` — start a heartbeat demo
- `--serve --port 8765 --demo3d` — start a 3D cube demo

---

## 8. License

This project is licensed under the [Apache License 2.0](LICENSE).

---

> Unity2Foxglove is an independent project and is not affiliated with or endorsed by Foxglove.
