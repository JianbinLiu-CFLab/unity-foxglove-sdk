# Unity2Foxglove ROS2 For Unity

This package is the optional ROS2 For Unity boundary for Unity2Foxglove.

Phase 107 contains only package metadata, documentation, attribution, and compliance records. ROS2 For Unity runtime binaries are not bundled here yet, and this package does not include a future adapter implementation in this phase.

Phase 108 facade is an API boundary only. The package is not end-user ready for ROS2 publishing yet, and the R2FU-backed implementation starts later behind an explicit integration gate.

Phase 109 adds a demo-project manual smoke adapter for one bidirectional `std_msgs/msg/String` topic pair. The adapter lives under the Unity demo project's `ManualAcceptance` scripts and is not bundled as production package runtime code.

Use the core package when you want normal Unity-to-Foxglove workflows:

```text
Packages/dev.unity2foxglove.sdk
```

The core SDK supports Foxglove WebSocket streaming, MCAP recording, replay, FoxRun, and the existing optional sidecar ROS2 bridge without depending on this package.

This optional package is reserved for users who later want Unity to participate as a ROS2 node through RobotecAI ROS2 For Unity while keeping the core SDK lightweight and ROS-free by default.

## Current Status

```text
bundleStatus: not_bundled
adapterStatus: not_implemented
runtimeAsset: Ros2ForUnity_humble_standalone_windows11.zip
```

Phase 106 proved that the R2FU standalone route can exchange simple `std_msgs/msg/String` topics with Windows ROS2 Jazzy while Unity itself is not launched from a local ROS2 environment.

Phase 106B showed that WSL2 NAT remains a problematic DDS discovery topology for this Humble standalone asset. Future remote Linux acceptance should use a real LAN, VPN, physical Linux host, or bridged Ubuntu VM rather than WSL2 NAT.

## Attribution Boundary

RobotecAI ROS2 For Unity is an upstream Apache-2.0 project:

```text
https://github.com/RobotecAI/ros2-for-unity
```

Unity2Foxglove does not claim authorship of RobotecAI ROS2 For Unity, ros2cs, generated ROS2 message assemblies, native ROS2 runtime libraries, Fast DDS/RMW components, or future extracted runtime files.

The copied upstream license is stored at:

```text
Upstream/LICENSE.AL2
```

See `THIRD_PARTY_NOTICES.md` and `Compliance/ros2-for-unity-adoption-manifest.json` before adding any runtime artifacts.

## Future Work

Later phases may add:

- a real R2FU-backed context behind the facade;
- a guarded adapter facade over ROS2 For Unity;
- a runtime bundle inventory;
- per-platform runtime validation;
- explicit DDS network-profile troubleshooting;
- real LAN or bridged Linux acceptance evidence.

Any future binary bundling must update the adoption manifest and add a complete transitive inventory before distribution.
