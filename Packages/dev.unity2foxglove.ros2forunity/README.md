# Unity2Foxglove ROS2 For Unity

This package is the optional ROS2 For Unity boundary for Unity2Foxglove.

Phase 107 contains package metadata, documentation, attribution, and compliance records. ROS2 For Unity runtime binaries are not bundled here.

Phase 108 facade is an API boundary only. The package is not end-user ready for ROS2 publishing yet, and the R2FU-backed implementation starts later behind an explicit integration gate.

Phase 109 adds a demo-project manual smoke adapter for one bidirectional `std_msgs/msg/String` topic pair. The adapter lives under the Unity demo project's `ManualAcceptance` scripts and is not bundled as production package runtime code.

Phase 110 productizes that smoke path as the `ROS2 For Unity External Adapter` importable sample. Users still import external ROS2 For Unity under `Assets/Ros2ForUnity`; this package only ships source code that connects the Unity2Foxglove ROS2 facade to that external ROS2 For Unity runtime.

Use the core package when you want normal Unity-to-Foxglove workflows:

```text
Packages/dev.unity2foxglove.sdk
```

The core SDK supports Foxglove WebSocket streaming, MCAP recording, replay, FoxRun, and the existing optional sidecar ROS2 bridge without depending on this package.

This optional package is reserved for users who later want Unity to participate as a ROS2 node through RobotecAI ROS2 For Unity while keeping the core SDK lightweight and ROS-free by default.

## Current Status

```text
bundleStatus: not_bundled
adapterStatus: external_assets_sample
runtimeAsset: Ros2ForUnity_humble_standalone_windows11.zip
```

Phase 106 proved that the R2FU standalone route can exchange simple `std_msgs/msg/String` topics with Windows ROS2 Jazzy while Unity itself is not launched from a local ROS2 environment.

Phase 106B showed that WSL2 NAT remains a problematic DDS discovery topology for this Humble standalone asset. Future remote Linux acceptance should use a real LAN, VPN, physical Linux host, or bridged Ubuntu VM rather than WSL2 NAT.

Phase 110 uses Windows ROS2 Jazzy as the local live acceptance peer. WSL2 NAT is not a GREEN gate for the external adapter sample.

## External Adapter Sample

Import `ROS2 For Unity External Adapter` from Package Manager after importing external ROS2 For Unity into:

```text
Assets/Ros2ForUnity
```

Then enable:

```text
UNITY2FOXGLOVE_ROS2_FOR_UNITY
```

The sample exposes one bidirectional `std_msgs/msg/String` smoke pair:

```text
/unity2foxglove/ros2forunity/string/out
/unity2foxglove/ros2forunity/string/in
```

Standard ROS2 visualization mapping starts in deferred 171+ phases after the external adapter sample reaches live GREEN.

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

- a runtime bundle inventory;
- per-platform runtime validation;
- explicit DDS network-profile troubleshooting;
- real LAN or bridged Linux acceptance evidence.

Any future binary bundling must update the adoption manifest and add a complete transitive inventory before distribution.
