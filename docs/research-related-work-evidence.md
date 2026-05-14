# Research Related Work Evidence

This public note collects related-work sources used to position Unity2Foxglove as a Unity-native telemetry, Foxglove, and MCAP integration package.

It is a citation map for readers and reviewers. Each source is summarized by its relevance to Unity2Foxglove and by the boundary that keeps the comparison technically fair.

## Positioning Summary

> The reviewed public literature, official documentation, and project documentation did not identify another Unity-native package that combines an in-process Foxglove WebSocket server, AOT-safe attribute-driven telemetry generation, MCAP recording/replay, and slow-client transport hardening in a single Unity package.

This is a scoped literature and project-survey claim. The contribution is the integration and validation of known techniques inside Unity, under IL2CPP/AOT constraints, for robotics visualization workflows.

The boundary is equally important:

- Foxglove, MCAP, WebSocket transport, source generation, and ROS bridge systems already exist.
- Unity2Foxglove is not an official Foxglove project.
- Unity2Foxglove is not a replacement for the Foxglove multi-language SDK ecosystem.
- Unity2Foxglove is evaluated as a Unity-focused package that combines these pieces into one runtime, authoring, recording, replay, and validation workflow.

## Source Map

| Source | Link | Evidence Role | Boundary |
| --- | --- | --- | --- |
| Unity Manual: Scripting restrictions | https://docs.unity.cn/Manual/ScriptingRestrictions.html | Unity AOT platforms cannot implement `System.Reflection.Emit`; reflection-only usage can be affected by stripping; IL2CPP/AOT needs compile-time evidence. | Technical basis for FoxRun's generated publisher path; not a novelty source by itself. |
| Unity Manual: Roslyn analyzers and source generators | https://docs.unity.cn/Manual/roslyn-analyzers.html | Unity supports Roslyn analyzers/source generators as an additional script-compilation step, with Unity-specific packaging constraints. | Supports Editor-time generation feasibility; not the IL2CPP physical-file fallback by itself. |
| System.Text.Json reflection versus source generation | https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/reflection-vs-source-generation | Microsoft documents reflection metadata collection, source generation, reduced memory/startup cost, trimming support, and Native AOT constraints. | General .NET source-generation precedent; serialization contracts, not Unity telemetry publishers. |
| MessagePack-CSharp AOT code generation | https://github.com/MessagePack-CSharp/MessagePack-CSharp | Shows Unity/Xamarin AOT support through source-generated formatters and explains that dynamic code generation is limited to Mono/.NET Framework targets. | Strong AOT-source-generation precedent, but focused on serialization rather than runtime telemetry publishing. |
| Foxglove SDK WebSocket Server guide | https://docs.foxglove.dev/docs/sdk/websocket-server | Official WebSocket server capability surface: default `127.0.0.1:8765`, status messages, time, and PlaybackControl semantics. | Official target semantics for Unity2Foxglove parity; not Unity-specific. |
| foxglove_bridge ROS package docs | https://docs.ros.org/en/iron/p/foxglove_bridge/index.html | High-performance ROS 1/ROS 2 WebSocket bridge using the Foxglove protocol, written in C++, with parameters, graph introspection, and ROS schema support. | Strong comparison point: official/ROS bridge path, but requires ROS stack and is not embedded in Unity. |
| rosbridge_suite GitHub | https://github.com/robotwebtools/rosbridge_suite | JSON interface to ROS over WebSocket/TCP for topics, services, and parameters. | Classic external ROS bridge; useful contrast for JSON/ROS bridge workflows and performance/visibility limitations. |
| Unity ROS-TCP-Connector package | https://github.com/Unity-Technologies/ROS-TCP-Connector/tree/main/com.unity.robotics.ros-tcp-connector | Unity package for connecting Unity with ROS through Unity's robotics tooling. | Unity-native integration surface, but it connects to ROS; it is not Foxglove-native and not MCAP-focused. |
| Unity Robotics Hub ROS-Unity setup | https://github.com/Unity-Technologies/Unity-Robotics-Hub/blob/main/tutorials/ros_unity_integration/setup.md | Official tutorial instructs users to install `ROS-TCP-Connector` from Git URL and configure ROS settings/protocol. | Evidence that mainstream Unity robotics integration commonly expects ROS setup. |
| MCAP getting started | https://mcap.dev/guides/getting-started | MCAP is positioned for robotics workflows; official docs list Python, C++, Go, Swift, TypeScript, and Rust readers/writers and Foxglove schema formats. | Shows MCAP ecosystem breadth and absence of a listed official Unity/C# Unity package. |
| Rerun getting started | https://docs.rerun.io/dev/getting-started/ | Rerun targets robotics/Physical AI multimodal logging, visualization, querying, and recordings. | Strong comparison for declarative multimodal logging; not a Unity/Foxglove/MCAP bridge and primary SDKs differ. |
| Rerun GitHub | https://github.com/rerun-io/rerun | Open-source SDK for logging, storing, querying, and visualizing multimodal/multi-rate data; examples include live stream and file workflows. | Closest developer-experience neighbor, not a Unity IL2CPP Foxglove package. |
| Unity Analytics events | https://docs.unity.com/en-us/analytics/events/events | Unity Analytics events capture player/game actions and parameters for engagement, spending, funnels, dashboards, and product metrics. | Product analytics rather than live robotics visualization or bidirectional runtime debugging. |
| Unity Recorder package | https://docs.unity.cn/2022.1/Documentation/Manual/com.unity.recorder.html | Unity Recorder captures Editor Play Mode animations, video, images, audio, AOVs, and arbitrary output variables. | Capture/export workflow, not Foxglove WebSocket telemetry, MCAP robotics logs, or client control. |
| XREcho GitHub | https://github.com/liris-xr/XREcho | Unity plug-in to record/replay XR interactions, HMD/controller movement, and trajectory visualization; archived in 2024. | Relevant Unity replay/XR evidence, but session-behavior replay rather than robotics telemetry/MCAP/Foxglove. |
| Recording and replaying psychomotor user actions in VR | https://arxiv.org/abs/2205.00923 | Describes a VR recorder/replay system implemented in a modern game engine. | Related Unity/game-engine replay work, not Foxglove, MCAP, or live telemetry bridge. |
| psiUnity: A Platform for Multimodal Data-Driven XR | https://arxiv.org/abs/2511.05304 | Unity data-streaming/XR integration exists; psiUnity bridges Microsoft `\psi` data streams with Unity/MRTK3/HoloLens 2. | Related Unity data-streaming work, but not Foxglove/MCAP and not an in-process Foxglove telemetry server. |
| SimNav-XR: an extended reality platform for mobile robot simulation using ROS2 and Unity3D | https://www.frontiersin.org/journals/robotics-and-ai/articles/10.3389/frobt.2026.1708161/full | ROS2 + Unity3D robotics/XR workflows; ROS2 is the backbone and Unity connects via ROS-TCP-Connector. | Strong contrast for ROS-middleware-based Unity robotics workflows. |
| Unity and ROS as a Digital and Communication Layer for Digital Twin Application | https://www.mdpi.com/1424-8220/24/17/5680 | Unity + ROS digital-twin communication layer for robotic arm/smart manufacturing; Sensors 2024, 24(17), 5680. | Digital-twin comparison point, but not Foxglove-native telemetry and not MCAP replay as a Unity package. |

## Related-Work Buckets

These buckets summarize how the sources above relate to Unity2Foxglove.

### Foxglove And ROS Bridge Ecosystem

Core sources:

- Foxglove SDK WebSocket Server guide
- `foxglove_bridge`
- `rosbridge_suite`

Positioning:

Existing bridge systems make ROS data visible to web or Foxglove clients, usually by running a separate ROS node/process. `foxglove_bridge` is the strongest protocol-level comparison because it uses the Foxglove WebSocket protocol and supports parameters and graph introspection. Unity2Foxglove differs by placing the Foxglove-compatible server directly inside Unity and by targeting Unity publishers, replay, and Inspector workflows without requiring a ROS runtime.

### Unity-ROS And Digital-Twin Integration

Core sources:

- Unity ROS-TCP-Connector
- Unity Robotics Hub ROS-Unity setup
- SimNav-XR
- Unity and ROS as a Digital and Communication Layer

Positioning:

Unity robotics systems commonly integrate through ROS/ROS2 middleware and packages such as ROS-TCP-Connector. These works are directly relevant because they show Unity as a robotics visualization or simulation layer. Unity2Foxglove is a local-first Foxglove/MCAP path that can be used without installing or bridging through ROS.

### Declarative Multimodal Logging And Visualization

Core sources:

- Rerun getting started
- Rerun GitHub

Positioning:

Rerun is the closest developer-experience neighbor: simple logging calls, multimodal data, temporal visualization, live/file workflows, and robotics/Physical AI positioning. Unity2Foxglove's distinction is Unity-specific: `[FoxRun]` field/property attributes, AOT-safe generated publisher paths, Foxglove protocol compatibility, and MCAP recording/replay inside a Unity package.

### MCAP Ecosystem And Replay

Core sources:

- MCAP getting started
- Foxglove SDK WebSocket Server guide

Positioning:

MCAP already has a broad robotics ecosystem and readers/writers in several languages. Unity2Foxglove brings MCAP recording/replay into Unity workflows rather than defining the format itself. Its current replay model is snapshot-oriented scene reproduction and WebSocket playback, not deterministic execution replay.

### Unity Capture, Analytics, And XR Replay

Core sources:

- Unity Analytics events
- Unity Recorder package
- XREcho
- Recording and replaying psychomotor user actions in VR

Positioning:

Unity already has capture, analytics, and replay-adjacent tooling, but these tools target game/product analytics, media capture, or XR user-session replay. They do not provide live Foxglove WebSocket telemetry, official robotics schemas, services/parameters, MCAP records, or Foxglove playback controls.

### AOT, Source Generation, And Reflection Avoidance

Core sources:

- Unity scripting restrictions
- Unity Roslyn analyzers/source generators
- System.Text.Json source generation
- MessagePack-CSharp AOT generation

Positioning:

AOT-safe source generation is a known and important pattern. Unity2Foxglove's narrower contribution is applying a dual-host/shared-emitter generation model to Unity telemetry publishing: Editor Roslyn generation for ergonomics plus physical `.g.cs` fallback for IL2CPP player builds.

## Claim-To-Source Map

| Claim Area | Sources To Use | Notes |
| --- | --- | --- |
| IL2CPP/AOT makes reflection-heavy or runtime-codegen telemetry fragile | Unity scripting restrictions; Unity Roslyn source generators; System.Text.Json; MessagePack-CSharp | Unity provides the platform-specific constraint; .NET and MessagePack show source generation as an established AOT strategy. |
| Unity supports source generators but package/player workflow has Unity-specific constraints | Unity Roslyn analyzers/source generators | Supports why FoxRun has both Editor generator and physical fallback instead of relying only on an analyzer DLL. |
| Unity data streaming exists, but not as a Foxglove/MCAP telemetry package | psiUnity; XREcho | Compare scope: XR/HoloLens/`\psi` and XR behavior recording versus Foxglove/MCAP/robotics visualization. |
| Unity robotics pipelines commonly rely on ROS/ROS2 middleware | Unity ROS-TCP-Connector; Unity Robotics Hub; SimNav-XR; Unity and ROS as Digital and Communication Layer | These support the "external middleware/bridge" contrast. |
| Foxglove and ROS bridges already exist | Foxglove WebSocket Server guide; `foxglove_bridge`; `rosbridge_suite` | Positions Unity2Foxglove as an in-Unity bridge, not as a replacement for ROS bridges. |
| MCAP and Foxglove are existing ecosystems | MCAP getting started; Foxglove WebSocket Server guide; public repository evidence | Unity2Foxglove integrates with these ecosystems from Unity. |
| Rerun is the closest declarative logging comparison | Rerun docs/GitHub | Frames FoxRun as a Unity/Foxglove-specific declarative telemetry layer, not as the first declarative visualization API. |
| Unity capture/replay tools exist but target different artifacts | Unity Recorder; XREcho; psychomotor VR replay paper | Distinguishes media/XR replay from robotics telemetry replay and Foxglove playback. |
| Unity2Foxglove contribution is a combined package, not isolated techniques | All verified sources + public repository evidence below | Tie the novelty claim to integration: in-process Foxglove server + AOT FoxRun + MCAP + backpressure. |

## Public Repository Evidence To Pair With Citations

The following tracked files provide project-side evidence for claims made in this note.

- `PAPER.md` - current research positioning and related-work boundary.
- `README.md` - public positioning, feature list, validation summary, release/test commands, and limitations.
- `docs/research-shared-emitter-architecture.md` - FoxRun shared-emitter and dual-host generation architecture.
- `docs/research-remote-timeline-scene-reproduction.md` - replay/timeline scene reproduction research note.
- `docs/research-related-work-evidence.md` - this citation map.
- `Packages/dev.unity2foxglove.sdk/README.md` - package-level user-facing feature and setup summary.
- `Packages/dev.unity2foxglove.sdk/Documentation~/en/10_Architecture.md` - runtime, protocol, MCAP, replay, FoxRun, transport, and security architecture.
- `Packages/dev.unity2foxglove.sdk/Documentation~/en/13_Schema_Coverage.md` - public schema coverage and smoke MCAP explanation.
- `Packages/dev.unity2foxglove.sdk/Documentation~/en/15_Secure_WSS.md` - local WSS/browser workflow and security boundary.
- `Packages/dev.unity2foxglove.sdk/Tests/Runtime/Program.cs` - runtime validation entry point and phase coverage.
- `Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase44Validation.cs` - official schema coverage validation.
- `Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase52Validation.cs` - WSS/origin/token/certificate validation.

## Claim Boundary

The research positioning is scoped to reviewed public evidence:

> The reviewed public literature, official documentation, and publicly accessible project documentation did not identify another Unity-native package that combines these four properties.

This statement is a scoped literature/project-survey claim. It is not a claim that Unity2Foxglove is an official Foxglove project, a replacement for Foxglove's SDK ecosystem, or the inventor of Foxglove, MCAP, WebSocket transport, or source generation.
