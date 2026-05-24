# Unity2Foxglove ROS2 For Unity

This package is the optional ROS2 For Unity boundary for Unity2Foxglove.

It provides facade/API boundaries, documentation, attribution records, and a source-only `ROS2 For Unity External Adapter` sample. ROS2 For Unity runtime binaries are not bundled here.

The facade is an API boundary only when no runtime package is active. It compiles and reports missing runtime gracefully, but it is not end-user ready for ROS2 publishing until a runtime package or external ROS2 For Unity import provides the backing implementation.

The current product direction is Jazzy-first for Windows x64 runtime work. The `dev.unity2foxglove.ros2forunity.runtime.jazzy.win64` package owns the R2FU Jazzy standalone runtime files, manifests, checksums, inventory, and notices. This adapter package stays lightweight and compiles without a runtime package.

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
recommendedRuntimeCandidate: Ros2ForUnity_jazzy_standalone_windows_x86_64.zip
runtimePackage: dev.unity2foxglove.ros2forunity.runtime.jazzy.win64
legacyRuntimeAsset: Ros2ForUnity_humble_standalone_windows11.zip
```

The rebuilt Jazzy standalone route has exchanged simple `std_msgs/msg/String` topics bidirectionally with Windows ROS2 Jazzy while Unity itself is not launched from a local ROS2 environment.

The current Jazzy Windows x64 runtime package has its runtime manifest, generated file inventory, checksum, and artifact-specific notices under `Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64`. The adapter package keeps compatibility records under `Compliance/` without bundling runtime binaries itself.

The old Humble standalone asset remains historical/fallback evidence, but it is not the recommended new-user runtime line after the Jazzy rebuild and retest.

WSL2 NAT remains a problematic DDS discovery topology and is not a GREEN gate for the external adapter sample or future runtime package. Future remote Linux acceptance should use a real LAN, VPN, physical Linux host, or bridged Ubuntu VM rather than WSL2 NAT.

R2FU Jazzy graph snapshots can be intermittent in `ros2 topic list`; use actual publish/subscribe data flow as the current acceptance signal.

## Package Composition

| Install set | Expected behavior |
|---|---|
| `dev.unity2foxglove.sdk` | Fully usable by itself for normal Foxglove WebSocket, MCAP, Replay, and FoxRun workflows. |
| `dev.unity2foxglove.ros2forunity` | Installs and compiles by itself. Reports missing runtime gracefully. |
| `dev.unity2foxglove.ros2forunity.runtime.jazzy.win64` | Installs runtime files and exposes metadata/diagnostics by itself. |
| Adapter + runtime | Enables Unity-as-ROS2-node publish/subscribe through ROS2 For Unity. |
| SDK + adapter + runtime | Full combined Unity2Foxglove workflow. |

Dependency direction is intentionally one-way:

```text
dev.unity2foxglove.sdk does not depend on ROS2 For Unity packages.
dev.unity2foxglove.ros2forunity can compile without runtime packages.
dev.unity2foxglove.ros2forunity.runtime.* packages must not force the core SDK to load ROS2.
Only one runtime package should be active in a Unity project unless a future conflict resolver exists.
```

Runtime packages are expected to be package/release artifacts. They should carry their own manifest, checksum, file inventory, third-party notices, and license inventory.

## External Adapter Sample

Install the adapter package and the Jazzy Win64 runtime package:

```text
dev.unity2foxglove.ros2forunity
dev.unity2foxglove.ros2forunity.runtime.jazzy.win64
```

The adapter package automatically enables the Standalone build-target symbol when it detects the Jazzy Win64 runtime package:

```text
UNITY2FOXGLOVE_ROS2_FOR_UNITY
```

For an external, non-package ROS2 For Unity import, add that symbol manually.

The sample exposes one bidirectional `std_msgs/msg/String` smoke pair:

```text
/unity2foxglove/ros2forunity/string/out
/unity2foxglove/ros2forunity/string/in
```

Standard ROS2 visualization mapping starts after the external adapter sample and runtime package path are stable.

## RViz2 Standard Visualization Acceptance

The `RViz2 Standard Visualization Acceptance` sample is the first narrow standard-message acceptance kit for the ROS2 For Unity path. It publishes:

```text
/tf
/scan
```

The frame tree is:

```text
map -> base_link -> laser
```

This sample is intentionally limited to `tf2_msgs/msg/TFMessage` and `sensor_msgs/msg/LaserScan`. It does not add PointCloud2, MarkerArray, Camera/Image, MCAP replay fanout, rosbag2, or any core SDK ROS2 dependency. Import it from Package Manager only when the project has a ROS2 For Unity runtime package or an external ROS2 For Unity import and the `UNITY2FOXGLOVE_ROS2_FOR_UNITY` symbol is active.

## RViz2 PointCloud2 Acceptance

The `RViz2 PointCloud2 Acceptance` sample extends the narrow standard-message acceptance route with generic unorganized point clouds. It publishes:

```text
/tf
/points
```

The `/points` topic uses:

```text
sensor_msgs/msg/PointCloud2
```

The frame tree is:

```text
map -> base_link -> point_cloud_sensor
```

This sample uses Unity2Foxglove's existing packed point-cloud layout through `PointCloudFrame` and `PointCloudPackedDataBuilder`, then maps that packed layout to `sensor_msgs/msg/PointCloud2` for RViz2. It is generic and not vendor-specific. It does not claim organized clouds, PointCloud2 subscription, LiDAR vendor presets, MarkerArray, Camera/Image, MCAP replay fanout, rosbag2, or any core SDK ROS2 dependency.

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

Future work may add:

- per-platform runtime validation;
- explicit DDS network-profile troubleshooting;
- real LAN or bridged Linux acceptance evidence.

Any future runtime package or binary refresh must update the adoption manifest and include a complete transitive inventory, third-party notices, license inventory, checksums, and fresh Unity project acceptance before distribution.
