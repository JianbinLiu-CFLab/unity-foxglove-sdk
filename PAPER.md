# Unity2Foxglove Research Positioning

Working title:

**Unity2Foxglove: A Unity-Native Real-Time Telemetry and MCAP Replay Pipeline for Robotics Visualization**

## Abstract

Unity is widely used for robotics simulation, digital twins, and human-in-the-loop visualization, but moving runtime state from Unity into robotics visualization tools often depends on external bridge processes, manual publisher code, or reflection-heavy runtime inspection. These approaches can be fragile under Unity player builds and IL2CPP ahead-of-time compilation.

Unity2Foxglove explores a local-first alternative: a Unity package that streams runtime telemetry to Foxglove over an in-process WebSocket server, records and replays MCAP data, and generates AOT-safe publisher code for declarative telemetry fields. The system combines generated FoxRun publishers, schema-aware JSON/protobuf publication, bounded transport backpressure, MCAP integrity checks, and repeatable runtime/performance validation.

To the best of our knowledge, Unity2Foxglove is the first public Unity-native pipeline we have found that combines declarative field/property-level telemetry, dual-path AOT-safe code generation, in-process Foxglove streaming, MCAP recording/replay, and slow-client transport hardening under Unity IL2CPP constraints.

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

## Novelty Boundary

Unity2Foxglove does not claim to invent Foxglove, MCAP, WebSocket transport, protobuf schemas, or source generators. Its contribution is the integration and validation of these ideas inside Unity's runtime and IL2CPP constraints, with a declarative telemetry mechanism designed for Unity developers.

The project is also not an official Foxglove SDK or a replacement for Foxglove's multi-language SDK ecosystem. It targets Unity workflows and prioritizes a Unity-native developer experience.

## Related Work Boundary

Relevant comparison points include:

- Foxglove's official SDKs and bridge tools, which define the protocol and ecosystem targets.
- Unity robotics bridge approaches that rely on ROS or separate middleware processes.
- Rerun and other visualization/logging tools that offer declarative data logging in other runtime environments.
- AOT-oriented C# source-generation systems used for serialization or dependency injection.
- General MCAP libraries that provide broader file-format APIs outside Unity.

Unity2Foxglove should be evaluated against these systems as a Unity-focused bridge and validation pipeline, not as a universal robotics middleware or general-purpose MCAP library.

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

Software citation metadata is provided in [`CITATION.cff`](CITATION.cff). The initial evidence release is intended to use a tag such as `paper-evidence-2026-05-09`. A DOI can be added after archiving the GitHub release through Zenodo or another software archive.
