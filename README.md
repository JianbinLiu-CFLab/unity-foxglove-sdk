# Unity2Foxglove

[![License](https://img.shields.io/badge/license-Apache%202.0-blue.svg)](LICENSE)
[![Unity](https://img.shields.io/badge/Unity-6000.0%2B-black?logo=unity)](https://unity.com/)
[![.NET tests](https://img.shields.io/badge/.NET%20tests-10.0-purple?logo=dotnet)](https://dotnet.microsoft.com/)
[![Release](https://img.shields.io/badge/release-v1.9.6-green)](https://github.com/JianbinLiu-CFLab/unity-foxglove-sdk/releases)
[![DOI](https://zenodo.org/badge/DOI/10.5281/zenodo.20112833.svg)](https://doi.org/10.5281/zenodo.20112833)
[![Tests](https://github.com/JianbinLiu-CFLab/unity-foxglove-sdk/actions/workflows/dotnet-tests.yml/badge.svg)](https://github.com/JianbinLiu-CFLab/unity-foxglove-sdk/actions/workflows/dotnet-tests.yml)
[![Docs Check](https://github.com/JianbinLiu-CFLab/unity-foxglove-sdk/actions/workflows/docs-check.yml/badge.svg)](https://github.com/JianbinLiu-CFLab/unity-foxglove-sdk/actions/workflows/docs-check.yml)

Unity2Foxglove is a Unity-focused SDK for live Foxglove visualization and control, MCAP recording/replay, generated FoxRun bindings, and optional ROS2 data exchange. The server runs in-process in Unity; the project is independent and is not an official Foxglove product.

![Unity live streaming to Foxglove](Pictures/Foxglove_Real_Streaming.gif)

## Quick Start

Unity2Foxglove does not require ROS for its core WebSocket, MCAP, Replay, or FoxRun workflows.

1. Add the package in Unity Package Manager with `Add package from disk...` and select:

   ```text
   Packages/dev.unity2foxglove.sdk/package.json
   ```

   Or use the Git URL:

   ```text
   https://github.com/JianbinLiu-CFLab/unity-foxglove-sdk.git?path=/Packages/dev.unity2foxglove.sdk
   ```

2. Add `FoxgloveManager` to a scene and enter Play Mode.
3. In Foxglove, open a **Foxglove WebSocket** connection to `ws://127.0.0.1:8765`.
4. Add Topics, Plot, 3D, Camera, Parameters, or Service Call panels for the data you need.

For a ready-made project, open the `Unity2Foxglove` directory in Unity Hub. The Unity2Foxglove demo project contains the full manual-acceptance and combined-package workflows.

## What It Provides

| Area | Current capability |
| --- | --- |
| Live protocol | In-process managed WebSocket/WSS server with schemas, channels, time, status, graph, client publish, assets, parameters, services, and playback control. |
| FoxRun | AOT-safe generated field/property bindings for publish, subscribe, and debug-oriented full duplex; JSON, Protobuf, multi-sink output, single-source input, and main-thread apply. |
| MCAP | Indexed recording with LZ4/Zstd, attachments, schema evidence, bounded reading, seek/history, and Unity scene reproduction. |
| Foxglove replay control | Remote files plus the `Unity Replay Sync` panel let Foxglove Timeline and Plot seek drive the Unity scene. |
| Sensors | Transform, scene primitives, camera, IMU, laser scan, point cloud, camera calibration, raw/compressed point-cloud, and high-rate pipeline controls. |
| Data exchange | Independent Foxglove WebSocket, optional ROS2 Bridge, and optional ROS2 Native output; WebSocket or ROS2 Native input where the generated contract supports it. |

### FoxRun in One Minute

```csharp
using static Unity.FoxgloveSDK.Components.FoxRunFlow;
using static Unity.FoxgloveSDK.Components.FoxRunPolicy;

public partial class RobotStateView : MonoBehaviour
{
    // Default: Publish + FixedRate + 10 Hz.
    [FoxRun("/robot/pose")]
    private PoseState _pose;

    [FoxRun("/robot/state", Mode = Subscribe, Policy = Change, RateHz = 30)]
    private RobotState _state;

    [FoxRun("/debug/state", Mode = PublishAndSubscribe, Policy = FixedRate, RateHz = 10)]
    private DebugState _debugState;
}
```

One subscribed member resolves one input source for a session. Published data can fan out to more than one enabled destination. `RateHz` limits output cadence and/or Unity main-thread apply cadence; it does not change network receive rate or ROS2 QoS.

See [FoxRun shared-emitter architecture](docs/research-shared-emitter-architecture.md) for the complete flow/policy semantics and AOT boundary.

### Replay in One Minute

1. In `FoxgloveManager > MCAP Record & Replay`, choose a replay file.
2. Enable `Foxglove as Replay Timeline` and enter Play Mode.
3. Open the generated Foxglove URL as a Remote file.
4. Add `Unity Replay Sync` and keep sync enabled.
5. Play, scrub the Timeline, or seek from Plot; Unity follows the Foxglove cursor.

Foxglove owns interactive time. Unity applies ordered forward ranges during normal playback and latest-at snapshots for seeks; it does not claim deterministic physics/input simulation. See [Foxglove-owned timeline and scene reproduction](docs/research-remote-timeline-scene-reproduction.md).

## Package Layout

| Package / workspace | Role |
| --- | --- |
| `Packages/dev.unity2foxglove.sdk` | ROS-free core package for WebSocket, MCAP, Replay, FoxRun, publishers, and sensors. |
| `Packages/dev.unity2foxglove.ros2forunity` | Optional ROS2 For Unity facade, diagnostics, generated bindings, and samples. |
| `Packages/dev.unity2foxglove.ros2forunity.runtime.humble.win64` | Optional Humble Windows x64 runtime package. |
| `Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64` | Optional Jazzy Windows x64 runtime package. |
| `Packages/dev.unity2foxglove.ros2forunity.runtime.lyrical.win64` | Optional Lyrical Windows x64 runtime package. |
| `Packages/dev.unity2foxglove.foxrun.ros2.interfaces` | Static custom FoxRun ROS2 interfaces. |
| `Unity2Foxglove` | Demo and manual-acceptance Unity project. |

Exactly one optional ROS2 runtime package should be active in a Unity project. The adapter can compile without a runtime and reports the missing capability instead of making the core SDK depend on ROS2.

The normal Foxglove WebSocket streaming, MCAP recording, or replay path needs no ROS2 package. The `ROS2 Bridge` is an independent localhost sidecar mirror and is disabled by default. ROS2 Native uses the optional RobotecAI ROS2 For Unity package line and preserves its Apache-2.0 attribution and runtime inventory boundary.

### ROS2 Evidence Boundary

The Windows-local Unity Editor matrix has passed Humble/Fast DDS, Jazzy/Fast DDS, Lyrical/Fast DDS, and Lyrical/Zenoh. This is not Windows Player, Linux, macOS, cross-machine, or production redistribution certification.

Windows Firewall can block Fast DDS discovery traffic. Configure an inbound allow rule for the Unity process or use a Fast DDS Discovery Server where appropriate. WSL2 is usable only when the selected network/firewall topology is configured correctly.

## Documentation

| Goal | Document |
| --- | --- |
| Install and use the reusable package | [Package documentation](Packages/dev.unity2foxglove.sdk/Documentation~/README.md) |
| Run the full Unity demo | [Demo project guide](Unity2Foxglove/README.md) |
| Import the minimal sample | [Basic Visualization](Packages/dev.unity2foxglove.sdk/Samples~/BasicVisualization/README.md) |
| Import the full sample | [Full Demo Visualization](Packages/dev.unity2foxglove.sdk/Samples~/FullDemoVisualization/README.md) |
| Understand subsystem boundaries | [Architecture patterns](docs/architecture-patterns.md) |
| See completed and remaining work | [Roadmap](ROADMAP.md) |

Release and compliance: [v1.9.6 release notes](docs/releases/RELEASE_NOTES_v1.9.6.md) · [Changelog](CHANGELOG.md) · [Third-party notices](THIRD_PARTY_NOTICES.md)

## Support Boundaries

- Core WebSocket/MCAP: Windows is verified for v1.9.6; macOS and Linux are intended targets but are not yet certified for this release.
- WebGL is unsupported because the in-process server requires socket APIs unavailable on WebGL.
- Replay reproduces recorded scene state; it does not rewind physics, random state, external services, or arbitrary user scripts.
- The local token gate and development certificates are not a production identity/authorization system.
- ROS2 Bridge remains a manually operated localhost sidecar, not a remote deployment product.
- ROS2 For Unity runtimes are optional preview packages. A local Editor PASS does not imply every Player or network topology.

## Security Defaults

- Bind address defaults to `127.0.0.1:8765`.
- Browser WebSocket origins are rejected unless allowed; Foxglove Desktop's local-file origin is supported.
- WSS can load a project-supplied PFX. Verify generated CA fingerprints before trusting them.
- The shared query token is suitable only as a lightweight trusted-local/LAN gate.
- Do not expose parameter, service, client-publish, or replay-control surfaces to an untrusted network without an external production security boundary.

## Contributor Verification

Run the repository validation suite before changing protocol, generator, MCAP, package, or release behavior:

```bash
python Scripts/release/run_ci.py
```

All generated build, restore, test, Unity, CMake, and colcon output belongs below the repository `build/` root; it is not source-package content.

## Citation and License

For research use, cite [CITATION.cff](CITATION.cff) or the Zenodo Concept DOI [`10.5281/zenodo.20112833`](https://doi.org/10.5281/zenodo.20112833). Use the version-specific DOI from the relevant archived release when exact artifact reproduction matters. Research positioning and evidence boundaries are summarized in [PAPER.md](PAPER.md).

Unity2Foxglove is licensed under the [Apache License 2.0](LICENSE). It is an independent project that grew from experiments at [Construction Future Lab](https://cflab.de).
