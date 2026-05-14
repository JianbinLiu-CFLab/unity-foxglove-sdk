# Research Related Work Evidence

Date: 2026-05-14

## Purpose

This note preserves verified related-work links for future Unity2Foxglove paper writing.

Use it as a citation map, not as final manuscript text. The goal is to keep every contribution claim tied to a source that can be reopened, converted to BibTeX/DOI form, and checked again before submission.

## Current Contribution Framing

Safe claim shape:

> To the best of our knowledge, Unity2Foxglove is the first publicly documented Unity-native implementation we have found that combines an in-process Foxglove WebSocket server, AOT-safe attribute-driven telemetry generation, MCAP recording/replay, and slow-client transport hardening in a single Unity package.

Boundary:

- Do not claim to invent WebSocket transport.
- Do not claim to invent MCAP.
- Do not claim to invent Foxglove.
- Do not claim to invent source generation.
- Do not claim official Foxglove project status.
- Frame the contribution as integration and validation inside Unity, under IL2CPP/AOT constraints, for robotics visualization workflows.

## Verified Sources

| Source | Link | Verified On | Supports | Boundary |
| --- | --- | --- | --- | --- |
| Unity Manual: Scripting restrictions | https://docs.unity.cn/Manual/ScriptingRestrictions.html | 2026-05-14 | Unity AOT platforms cannot implement `System.Reflection.Emit`; reflection-only usage can be affected by stripping; IL2CPP/AOT needs compile-time evidence. | Use as the technical basis for FoxRun's generated publisher path. It does not prove novelty by itself. |
| Unity Manual: Roslyn analyzers and source generators | https://docs.unity.cn/Manual/roslyn-analyzers.html | 2026-05-14 | Unity supports Roslyn analyzers/source generators as an additional script-compilation step, with Unity-specific packaging constraints. | Supports the feasibility of Editor-time source generation, but not the IL2CPP physical-file fallback by itself. |
| System.Text.Json reflection versus source generation | https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/reflection-vs-source-generation | 2026-05-14 | Microsoft documents reflection metadata collection, source generation, reduced memory/startup cost, trimming support, and Native AOT constraints. | General .NET source-generation precedent; serialization contracts, not Unity telemetry publishers. |
| MessagePack-CSharp AOT code generation | https://github.com/MessagePack-CSharp/MessagePack-CSharp | 2026-05-14 | Shows Unity/Xamarin AOT support through source-generated formatters and explains that dynamic code generation is limited to Mono/.NET Framework targets. | Strong AOT-source-generation precedent, but focused on serialization rather than runtime telemetry publishing. |
| Foxglove SDK WebSocket Server guide | https://docs.foxglove.dev/docs/sdk/websocket-server | 2026-05-14 | Official WebSocket server capability surface: default `127.0.0.1:8765`, status messages, time, and PlaybackControl semantics. | Official target semantics for Unity2Foxglove parity; not Unity-specific. |
| foxglove_bridge ROS package docs | https://docs.ros.org/en/iron/p/foxglove_bridge/index.html | 2026-05-14 | High-performance ROS 1/ROS 2 WebSocket bridge using the Foxglove protocol, written in C++, with parameters, graph introspection, and ROS schema support. | Strong comparison point: official/ROS bridge path, but requires ROS stack and is not embedded in Unity. |
| rosbridge_suite GitHub | https://github.com/robotwebtools/rosbridge_suite | 2026-05-14 | JSON interface to ROS over WebSocket/TCP for topics, services, and parameters. | Classic external ROS bridge; useful contrast for JSON/ROS bridge workflows and performance/visibility limitations. |
| Unity ROS-TCP-Connector package | https://github.com/Unity-Technologies/ROS-TCP-Connector/tree/main/com.unity.robotics.ros-tcp-connector | 2026-05-14 | Unity package for connecting Unity with ROS through Unity's robotics tooling. | Unity-native integration surface, but it connects to ROS; it is not Foxglove-native and not MCAP-focused. |
| Unity Robotics Hub ROS-Unity setup | https://github.com/Unity-Technologies/Unity-Robotics-Hub/blob/main/tutorials/ros_unity_integration/setup.md | 2026-05-14 | Official tutorial instructs users to install `ROS-TCP-Connector` from Git URL and configure ROS settings/protocol. | Good evidence that mainstream Unity robotics integration expects ROS setup. |
| MCAP getting started | https://mcap.dev/guides/getting-started | 2026-05-14 | MCAP is positioned for robotics workflows; official docs list Python, C++, Go, Swift, TypeScript, and Rust readers/writers and Foxglove schema formats. | Shows MCAP ecosystem breadth and absence of a listed official Unity/C# Unity package. |
| Rerun getting started | https://docs.rerun.io/dev/getting-started/ | 2026-05-14 | Rerun targets robotics/Physical AI multimodal logging, visualization, querying, and recordings. | Strong comparison for declarative multimodal logging; not a Unity/Foxglove/MCAP bridge and primary SDKs differ. |
| Rerun GitHub | https://github.com/rerun-io/rerun | 2026-05-14 | Open-source SDK for logging, storing, querying, and visualizing multimodal/multi-rate data; examples include live stream and file workflows. | Reinforces Rerun as closest developer-experience neighbor, not a Unity IL2CPP Foxglove package. |
| Unity Analytics events | https://docs.unity.com/en-us/analytics/events/events | 2026-05-14 | Unity Analytics events capture player/game actions and parameters for engagement, spending, funnels, dashboards, and product metrics. | Useful negative comparison: game/product analytics, not live robotics visualization or bidirectional runtime debugging. |
| Unity Recorder package | https://docs.unity.cn/2022.1/Documentation/Manual/com.unity.recorder.html | 2026-05-14 | Unity Recorder captures Editor Play Mode animations, video, images, audio, AOVs, and arbitrary output variables. | Capture/export workflow, not Foxglove WebSocket telemetry, MCAP robotics logs, or client control. |
| XREcho GitHub | https://github.com/liris-xr/XREcho | 2026-05-14 | Unity plug-in to record/replay XR interactions, HMD/controller movement, and trajectory visualization; archived in 2024. | Relevant Unity replay/XR evidence, but session-behavior replay rather than robotics telemetry/MCAP/Foxglove. |
| Recording and replaying psychomotor user actions in VR | https://arxiv.org/abs/2205.00923 | 2026-05-14 | Describes a VR recorder/replay system implemented in a modern game engine. | Related Unity/game-engine replay work, not Foxglove, MCAP, or live telemetry bridge. |
| psiUnity: A Platform for Multimodal Data-Driven XR | https://arxiv.org/abs/2511.05304 | 2026-05-14 | Unity data-streaming/XR integration exists; psiUnity bridges Microsoft `\psi` data streams with Unity/MRTK3/HoloLens 2. | Related Unity data-streaming work, but not Foxglove/MCAP and not an in-process Foxglove telemetry server. |
| SimNav-XR: an extended reality platform for mobile robot simulation using ROS2 and Unity3D | https://www.frontiersin.org/journals/robotics-and-ai/articles/10.3389/frobt.2026.1708161/full | 2026-05-14 | ROS2 + Unity3D robotics/XR workflows; ROS2 is the backbone and Unity connects via ROS-TCP-Connector. | Strong contrast for ROS-middleware-based Unity robotics workflows. |
| Unity and ROS as a Digital and Communication Layer for Digital Twin Application | https://www.mdpi.com/1424-8220/24/17/5680 | 2026-05-14 | Unity + ROS digital-twin communication layer for robotic arm/smart manufacturing; Sensors 2024, 24(17), 5680. | Useful digital-twin comparison, but not Foxglove-native telemetry and not MCAP replay as a Unity package. |

## Related-Work Buckets

Use these buckets to structure a Related Work section.

### Foxglove And ROS Bridge Ecosystem

Core sources:

- Foxglove SDK WebSocket Server guide
- `foxglove_bridge`
- `rosbridge_suite`

Argument:

Existing bridge systems make ROS data visible to web or Foxglove clients, usually by running a separate ROS node/process. `foxglove_bridge` is the strongest protocol-level comparison because it uses the Foxglove WebSocket protocol and supports parameters and graph introspection. Unity2Foxglove differs by placing the Foxglove-compatible server directly inside Unity and by targeting Unity publishers, replay, and Inspector workflows without requiring a ROS runtime.

### Unity-ROS And Digital-Twin Integration

Core sources:

- Unity ROS-TCP-Connector
- Unity Robotics Hub ROS-Unity setup
- SimNav-XR
- Unity and ROS as a Digital and Communication Layer

Argument:

Unity robotics systems commonly integrate through ROS/ROS2 middleware and packages such as ROS-TCP-Connector. These works are directly relevant because they show Unity as a robotics visualization or simulation layer. Unity2Foxglove should be contrasted as a local-first Foxglove/MCAP path that can be used without installing or bridging through ROS.

### Declarative Multimodal Logging And Visualization

Core sources:

- Rerun getting started
- Rerun GitHub

Argument:

Rerun is the closest developer-experience neighbor: simple logging calls, multimodal data, temporal visualization, live/file workflows, and robotics/Physical AI positioning. Unity2Foxglove should not claim to invent declarative visualization logging. Its distinction is Unity-specific: `[FoxRun]` field/property attributes, AOT-safe generated publisher paths, Foxglove protocol compatibility, and MCAP recording/replay inside a Unity package.

### MCAP Ecosystem And Replay

Core sources:

- MCAP getting started
- Foxglove SDK WebSocket Server guide
- Basis deterministic replay candidate source

Argument:

MCAP already has a broad robotics ecosystem and readers/writers in several languages. Unity2Foxglove should be framed as bringing MCAP recording/replay into Unity workflows, not as inventing the format. If deterministic replay is discussed, explicitly separate Unity2Foxglove's snapshot-oriented replay from deterministic execution replay systems.

### Unity Capture, Analytics, And XR Replay

Core sources:

- Unity Analytics events
- Unity Recorder package
- XREcho
- Recording and replaying psychomotor user actions in VR

Argument:

Unity already has capture, analytics, and replay-adjacent tooling, but these tools target game/product analytics, media capture, or XR user-session replay. They do not provide live Foxglove WebSocket telemetry, official robotics schemas, services/parameters, MCAP records, or Foxglove playback controls.

### AOT, Source Generation, And Reflection Avoidance

Core sources:

- Unity scripting restrictions
- Unity Roslyn analyzers/source generators
- System.Text.Json source generation
- MessagePack-CSharp AOT generation

Argument:

AOT-safe source generation is a known and important pattern. Unity2Foxglove's narrower contribution is applying a dual-host/shared-emitter generation model to Unity telemetry publishing: Editor Roslyn generation for ergonomics plus physical `.g.cs` fallback for IL2CPP player builds.

## Candidate Sources To Recheck

These sources appeared in search results and are likely relevant, but automated access hit publisher challenge pages or incomplete extraction. Recheck manually through DOI, publisher page, institutional access, or exported citation before using them in a manuscript.

| Candidate | Link | Why It Matters | Required Before Citation |
| --- | --- | --- | --- |
| A Dynamic Digital Twin System with Robotic Vision for Emergency Management | https://www.mdpi.com/2079-9292/15/3/573 | Candidate Unity/ROS/digital-twin comparison; search result reports Electronics 2026, 15(3), 573, DOI `10.3390/electronics15030573`. | Reopen manually, confirm title/authors/DOI, and verify whether ROS/Unity/socket architecture is accurately described. |
| Digital Twin Framework for Robot Path Planning and Real-Time Execution Using Unity-ROS Integration | https://www.mdpi.com/2075-1702/14/4/387 | Candidate Unity-ROS digital-twin comparison; search result reports Machines 2026, 14(4), 387. | Reopen manually, confirm final publication status, DOI, architecture, and whether it uses WebSocket/ROS middleware. |
| Basis deterministic replay | https://www.mintlify.com/basis-robotics/basis/testing/deterministic-replay | Candidate robotics MCAP replay/deterministic testing comparison. Search extraction indicates replay from MCAP through Basis transport and a premium deterministic replay mode. | Reopen manually, confirm product/docs stability, license/commercial boundary, and exact public API before citation. |

## Claim-To-Source Map

| Claim Area | Sources To Use | Notes |
| --- | --- | --- |
| IL2CPP/AOT makes reflection-heavy or runtime-codegen telemetry fragile | Unity scripting restrictions; Unity Roslyn source generators; System.Text.Json; MessagePack-CSharp | Use Unity docs as the primary Unity source, and .NET/MessagePack as precedent that source generation is an established AOT strategy. |
| Unity supports source generators but package/player workflow has Unity-specific constraints | Unity Roslyn analyzers/source generators | Supports why FoxRun has both Editor generator and physical fallback instead of relying only on an analyzer DLL. |
| Unity data streaming exists, but not as a Foxglove/MCAP telemetry package | psiUnity; XREcho | Compare scope: XR/HoloLens/`\psi` and XR behavior recording versus Foxglove/MCAP/robotics visualization. |
| Unity robotics pipelines commonly rely on ROS/ROS2 middleware | Unity ROS-TCP-Connector; Unity Robotics Hub; SimNav-XR; Unity and ROS as Digital and Communication Layer | These support the "external middleware/bridge" contrast. |
| Foxglove and ROS bridges already exist | Foxglove WebSocket Server guide; `foxglove_bridge`; `rosbridge_suite` | Useful for positioning Unity2Foxglove as an in-Unity bridge, not as a replacement for ROS bridges. |
| MCAP and Foxglove are existing ecosystems | MCAP getting started; Foxglove WebSocket Server guide; local parity evidence | Avoid framing MCAP/Foxglove as invented by this project. |
| Rerun is the closest declarative logging comparison | Rerun docs/GitHub | Use to frame FoxRun as a Unity/Foxglove-specific declarative telemetry layer, not as the first declarative visualization API. |
| Unity capture/replay tools exist but target different artifacts | Unity Recorder; XREcho; psychomotor VR replay paper | Use to distinguish media/XR replay from robotics telemetry replay and Foxglove playback. |
| Unity2Foxglove contribution is a combined package, not isolated techniques | All verified sources + local evidence in `Developer/32 Official SDK Parity Matrix.md` | Tie the novelty claim to integration: in-process Foxglove server + AOT FoxRun + MCAP + backpressure. |

## Local Evidence To Pair With Citations

These files may not all be part of the public package. Check distribution scope before citing a path directly.

- `PAPER.md` - current research positioning and related-work boundary.
- `docs/research-shared-emitter-architecture.md` - FoxRun shared-emitter and dual-host generation architecture.
- `Developer/23 Official Schema Coverage.md` - official Foxglove schema coverage evidence.
- `Developer/27 Sensor Typed Publisher Parity.md` - typed publisher parity evidence.
- `Developer/29 Maintainability and Optimization Closure.md` - optimization/maintainability closeout.
- `Developer/31 Secure WSS Local Certificate Generation.md` - local WSS/browser evidence and certificate generation decisions.
- `Developer/32 Official SDK Parity Matrix.md` - official SDK parity matrix and remaining gap map.

## Manuscript Guidance

Prefer this wording:

> To the best of our knowledge...

Avoid this wording:

- "world first"
- "only implementation"
- "nobody has ever..."
- "complete official replacement"

If stronger novelty language is needed, make it conditional and traceable:

> In the reviewed public literature and open-source projects, we did not find another Unity-native package that combines these four properties...

## Next Research Tasks

- [ ] Export BibTeX entries for the verified sources.
- [ ] Recheck candidate MDPI 2026 papers manually.
- [ ] Add official Foxglove SDK and MCAP citations in BibTeX form.
- [ ] Add Rerun citation/reference for declarative visualization logging comparison.
- [ ] Add source-generation/AOT related references if the paper emphasizes FoxRun internals.
- [ ] Decide whether Unity Analytics and Unity Recorder belong in the final manuscript or only in internal positioning notes.
- [ ] Decide whether Basis deterministic replay is citable or should remain a non-academic product comparison.
