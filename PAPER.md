# Unity2Foxglove Research Positioning

Working title:

**Unity2Foxglove: A Unity-Native Real-Time Telemetry and MCAP Replay Pipeline for Robotics Visualization**

## Reference Contribution Statement

Unity2Foxglove introduces an AOT-safe dual-host source generation architecture with a shared emitter for telemetry publishing that avoids CLR reflection-based member discovery and field/property access in Unity Editor and IL2CPP Player builds.

## Abstract

Unity is widely used for robotics simulation, digital twins, and human-in-the-loop visualization, but moving runtime state from Unity into robotics visualization tools often depends on external bridge processes, manual publisher code, or reflection-heavy runtime inspection. These approaches can be fragile under Unity player builds and IL2CPP ahead-of-time compilation.

Unity2Foxglove explores a local-first alternative: a Unity package that streams runtime telemetry to Foxglove over an in-process WebSocket server, records and replays MCAP data, and generates AOT-safe publisher code for declarative telemetry fields. The system combines generated FoxRun publishers, schema-aware JSON/protobuf publication, bounded transport backpressure, MCAP integrity checks, and repeatable runtime/performance validation.

To the best of our knowledge, Unity2Foxglove is the first publicly documented Unity-native implementation we have found that combines an in-process Foxglove WebSocket server, AOT-safe attribute-driven telemetry generation, MCAP recording/replay, and slow-client transport hardening in a single Unity package. This claim is intentionally scoped to the reviewed public literature and open-source ecosystem; it is a contribution-positioning statement rather than a claim to invent WebSocket transport, MCAP, source generation, or Foxglove itself.

## Thesis

Unity can act as a reliable robotics telemetry source when the bridge is built as a Unity-native package with:

- AOT-safe publisher generation instead of runtime reflection.
- Bounded transport behavior for slow or stalled clients.
- MCAP recording/replay with integrity validation.
- Public, repeatable tests and performance evidence.

## Contributions

1. **Unity-native Foxglove telemetry pipeline**

   Unity2Foxglove runs inside Unity Editor and player builds, exposes a Foxglove-compatible WebSocket endpoint, and avoids requiring a separate ROS bridge or external relay process for local visualization workflows.

2. **FoxRun declarative telemetry generation**

   `[FoxRun]` enables field/property-level telemetry with generated publisher code. The implementation uses a Roslyn generator for Editor-time ergonomics and a physical source fallback for IL2CPP builds, with shared emitter logic to reduce drift between the two paths.

3. **MCAP recording and replay integration**

   The package records Unity telemetry into MCAP, supports replay-oriented reading, and validates chunk CRC, attachment CRC, and summary CRC where available. Replay is intentionally snapshot-oriented and does not claim deterministic physics reproduction.

4. **Transport robustness and observability**

   The managed WebSocket backend uses bounded queues, control/data prioritization, slow-client drop behavior, and read-only transport health snapshots to protect healthy clients from stalled consumers.

5. **Evidence-oriented engineering workflow**

   Runtime tests, performance baselines, manual smoke tests, and release closeout notes are treated as part of the project artifact rather than as separate informal validation.

## Technical Note

The source-generation mechanism behind `[FoxRun]` is described in more detail in [`docs/research-shared-emitter-architecture.md`](docs/research-shared-emitter-architecture.md). That note frames the implementation as a shared-emitter, dual-host AOT code-generation architecture: Roslyn and build-time physical file generation are separate hosts, while `FoxgloveSourceEmitter` remains the single source of generation semantics.

## Novelty Boundary

Unity2Foxglove does not claim to invent Foxglove, MCAP, WebSocket transport, protobuf schemas, or source generators. Its contribution is the integration and validation of these ideas inside Unity's runtime and IL2CPP constraints, with a declarative telemetry mechanism designed for Unity developers.

The project is also not an official Foxglove SDK or a replacement for Foxglove's multi-language SDK ecosystem. It targets Unity workflows and prioritizes a Unity-native developer experience.

The strongest novelty framing is therefore comparative and scoped: existing work covers Unity-ROS bridges, XR data-streaming frameworks, AOT source generation, general MCAP libraries, and Foxglove SDKs separately. Unity2Foxglove contributes a single Unity package that brings these concerns together for local robotics visualization and debugging without requiring a separate ROS bridge, external relay process, or reflection-heavy runtime telemetry scanner.

## Related Work Boundary

Relevant comparison points include Foxglove's official SDKs and bridge tools, Unity robotics bridge approaches that rely on ROS or separate middleware processes, Rerun-style declarative logging, AOT-oriented C# source-generation systems, and general MCAP libraries.

The expanded citation map groups related work into six buckets: Foxglove/ROS bridges, Unity-ROS digital-twin integration, declarative multimodal logging, MCAP recording/replay ecosystems, Unity capture/analytics/XR replay tools, and AOT/source-generation systems. This grouping keeps the paper's novelty claim focused on the combination that Unity2Foxglove provides, rather than on any individual technique.

Verified related-work anchors:

| Source | What it supports | Boundary for Unity2Foxglove |
| --- | --- | --- |
| [Unity scripting restrictions](https://docs.unity.cn/Manual/ScriptingRestrictions.html) | Unity documents AOT restrictions, reflection-stripping risks, and lack of `System.Reflection.Emit` support on AOT platforms. | Supports the need for generated telemetry publishers instead of runtime code generation or reflection-heavy member access. |
| [Unity Roslyn analyzers and source generators](https://docs.unity.cn/Manual/roslyn-analyzers.html) | Unity supports Roslyn analyzers/source generators as part of script compilation, with Unity-specific packaging and analyzer-label constraints. | Supports Editor-time generation feasibility; Unity2Foxglove adds the IL2CPP physical-file fallback and shared-emitter validation. |
| [Foxglove SDK WebSocket Server guide](https://docs.foxglove.dev/docs/sdk/websocket-server) | Defines the official SDK WebSocket server surface, including local default connection, status messages, time, and PlaybackControl. | Primary protocol target for parity; not a Unity implementation. |
| [foxglove_bridge](https://docs.ros.org/en/iron/p/foxglove_bridge/index.html) | Provides a high-performance ROS 1/ROS 2 WebSocket bridge using the Foxglove protocol. | Strong ROS/Foxglove comparison, but it is a C++ ROS bridge process rather than an in-Unity package. |
| [rosbridge_suite](https://github.com/robotwebtools/rosbridge_suite) | Provides a JSON API to ROS over WebSocket/TCP for topics, services, and parameters. | Classic external ROS bridge; not Foxglove schema/MCAP/Unity-native telemetry. |
| [Unity ROS-TCP-Connector](https://github.com/Unity-Technologies/ROS-TCP-Connector/tree/main/com.unity.robotics.ros-tcp-connector) and [Unity Robotics Hub setup](https://github.com/Unity-Technologies/Unity-Robotics-Hub/blob/main/tutorials/ros_unity_integration/setup.md) | Show mainstream Unity robotics integration through ROS/ROS2 connector setup. | Unity-native integration surface, but still ROS middleware dependent. |
| [psiUnity: A Platform for Multimodal Data-Driven XR](https://arxiv.org/abs/2511.05304) | Bridges Microsoft `\psi` data streams with Unity/MRTK3/HoloLens 2 for multimodal XR research. | Related Unity data-streaming work, but not Foxglove/MCAP and not an in-process Foxglove telemetry server. |
| [SimNav-XR: ROS2 and Unity3D mobile robot simulation](https://www.frontiersin.org/journals/robotics-and-ai/articles/10.3389/frobt.2026.1708161/full) | Uses ROS2 as the backbone and connects Unity through ROS-TCP-Connector. | Useful contrast for ROS-middleware-based Unity robotics workflows. |
| [Unity and ROS as a Digital and Communication Layer for Digital Twin Application](https://www.mdpi.com/1424-8220/24/17/5680) | Presents Unity and ROS as digitalization/communication layers for a robotic-arm digital twin. | Useful contrast for Unity-ROS digital-twin systems that do not provide Foxglove-native telemetry and MCAP replay as a Unity package. |
| [MCAP getting started](https://mcap.dev/guides/getting-started) | Positions MCAP for robotics workflows and lists official readers/writers across Python, C++, Go, Swift, TypeScript, and Rust. | Supports MCAP as existing ecosystem; Unity2Foxglove contributes Unity workflow integration. |
| [Rerun getting started](https://docs.rerun.io/dev/getting-started/) and [Rerun GitHub](https://github.com/rerun-io/rerun) | Provide multimodal, time-aware robotics/Physical AI logging, visualization, and recording/query workflows. | Closest developer-experience neighbor for declarative logging, but not a Unity IL2CPP Foxglove/MCAP package. |
| [Unity Analytics events](https://docs.unity.com/en-us/analytics/events/events) | Shows Unity event telemetry for player behavior, product metrics, dashboards, and funnels. | Useful negative comparison: product analytics rather than live robotics visualization and client control. |
| [Unity Recorder](https://docs.unity.cn/2022.1/Documentation/Manual/com.unity.recorder.html), [XREcho](https://github.com/liris-xr/XREcho), and [VR action replay](https://arxiv.org/abs/2205.00923) | Show Unity capture/replay and XR user-session recording/replay. | Related capture/replay work, but not Foxglove WebSocket telemetry, official robotics schemas, or MCAP replay. |

Two additional MDPI 2026 Unity-ROS/digital-twin papers are candidate comparison points, but they should be manually rechecked through DOI or publisher access before being cited in the final manuscript because publisher pages may present access challenges in automated verification.

The maintained citation map for these sources lives in [`docs/research-related-work-evidence.md`](docs/research-related-work-evidence.md).

The closest technical neighbors are source-generation systems that reduce runtime reflection or support AOT builds:

| System / practice | Relevance | Boundary |
| --- | --- | --- |
| [`System.Text.Json` source generation](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/reflection-vs-source-generation) | Shows source generation replacing reflection-heavy metadata collection for trimming and Native AOT scenarios | Serialization contracts, not Unity telemetry publishers |
| [MessagePack-CSharp AOT generation](https://github.com/MessagePack-CSharp/MessagePack-CSharp) | Shows ahead-of-time C# generation for Unity/IL2CPP-style strict-AOT environments | Formatter/resolver generation, not Foxglove telemetry or Unity build-hook publisher generation |
| [Unity Netcode for Entities source generators](https://docs.unity.cn/Packages/com.unity.netcode%401.4/manual/source-generators.html) | Shows Unity using Roslyn generation to avoid runtime reflection in networking code | DOTS/ECS networking, not MonoBehaviour telemetry and MCAP |
| [Refitter MSBuild generation](https://refitter.github.io/articles/msbuild.html) and [Refit NativeAOT guidance](https://github.com/reactiveui/refit#native-aot--trimming-guidance) | Show build-time/source-generator-first patterns for .NET clients | REST/OpenAPI/interface clients, not runtime scene telemetry |
| [ReactiveMarbles ObservableEvents source generator](https://www.nuget.org/packages/ReactiveMarbles.ObservableEvents.SourceGenerator/) | Shows C# event-to-observable boilerplate generated at compile time | Event wrapper generation, not telemetry publisher generation |
| [Rerun logging APIs](https://ref.rerun.io/docs/python/0.31.2/common/logging_functions/) | Show declarative visualization logging as a developer experience | Primary SDKs are not Unity/C# IL2CPP telemetry packages |

Unity2Foxglove should be evaluated against these systems as a Unity-focused bridge and validation pipeline, not as a universal robotics middleware or general-purpose MCAP library. Its source-generation claim is also intentionally narrow: the runtime path is zero **CLR reflection** for telemetry member discovery and field/property access. Unity scene queries may still be used to find generated telemetry sources.

The shared-emitter design prevents host drift after the generation model is resolved, but it also makes the emitter a high-leverage component: escaping, culture-invariant formatting, publish-mode precedence, and model-equivalence tests must be part of the validation story. The detailed technical note tracks these boundaries in [`docs/research-shared-emitter-architecture.md`](docs/research-shared-emitter-architecture.md).

## Evidence Table

| Evidence | Purpose |
| --- | --- |
| Runtime validation suite | Checks protocol, schema, MCAP, backpressure, FoxRun, and publisher behavior. |
| Quick performance baseline | Captures repeatable publish, MCAP write/replay, transport, and camera policy measurements. |
| Unity/Foxglove manual smoke tests | Confirms Editor playback, live streaming, camera publishing, and transport health behavior. |
| IL2CPP build smoke | Confirms that generated code and preservation paths work in player builds. |
| MCAP attachment smoke file | Provides a small MCAP artifact for attachment and summary validation in Foxglove Desktop. |
| Release evidence tag | Intended to pin the exact commit, tests, and artifacts used for research citation. |

## Citation Note

Software citation metadata is provided in [`CITATION.cff`](CITATION.cff), using the Zenodo Concept DOI [`10.5281/zenodo.20112833`](https://doi.org/10.5281/zenodo.20112833) for the evolving software record. The archived evidence release DOI is [`10.5281/zenodo.20112834`](https://doi.org/10.5281/zenodo.20112834), corresponding to the `paper-evidence-2026-05-10` release.

Recommended evidence workflow:

1. Create a GitHub release from the evidence tag.
2. Archive that release through Zenodo.
3. Use the DOI-backed release as the reference implementation for technical reports or future paper submissions.
