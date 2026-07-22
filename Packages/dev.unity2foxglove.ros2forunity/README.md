# Unity2Foxglove ROS2 For Unity

This package is the optional ROS2 For Unity boundary for Unity2Foxglove.

It provides facade/API boundaries, documentation, attribution records, and a source-only `ROS2 For Unity External Adapter` sample. ROS2 For Unity runtime binaries are not bundled here.

The facade is an API boundary only when no runtime package is active. It compiles and reports missing runtime gracefully, but it is not end-user ready for ROS2 publishing until a runtime package or external ROS2 For Unity import provides the backing implementation.

The current Windows x64 runtime work uses explicit candidate runtime packages. Humble, Jazzy, and Lyrical are packaged as separate candidates, and exactly one runtime package should be active in a Unity project manifest at a time. Runtime packages own the ROS2 For Unity standalone runtime files, manifests, checksums, inventory, and notices. This adapter package stays lightweight and compiles without a runtime package.

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
runtimePackages: dev.unity2foxglove.ros2forunity.runtime.humble.win64, dev.unity2foxglove.ros2forunity.runtime.jazzy.win64, dev.unity2foxglove.ros2forunity.runtime.lyrical.win64
localRos2Entrypoint: <repo-root>/ros2-windows/ros2_<distro>
localArtifactEntrypoint: <repo-root>/r2fu-runtime-artifacts/<distro>
```

The rebuilt Jazzy standalone route has exchanged simple `std_msgs/msg/String` topics bidirectionally with Windows ROS2 Jazzy while Unity itself is not launched from a local ROS2 environment. Humble and Lyrical are also available as candidate runtime packages and must be validated in a fresh Unity Editor process after switching from another loaded runtime.

The current Windows x64 runtime packages have their runtime manifests, generated file inventories, checksums, and artifact-specific notices under repository-root `Packages/dev.unity2foxglove.ros2forunity.runtime.*` directories. The adapter package keeps compatibility records under `Compliance/` without bundling runtime binaries itself.

Local ROS2 command-line probes should use the repository-local `ros2-windows/` entrypoint, for example `ros2-windows/ros2_humble`, `ros2-windows/ros2_jazzy`, or `ros2-windows/ros2_lyrical`. Local ROS2 For Unity runtime ZIP inputs should use `r2fu-runtime-artifacts/<distro>/...`. These entrypoint directories keep machine-local installs and downloaded artifacts out of the package source tree.

Windows Firewall may block inbound Fast DDS UDP discovery. WSL2, VPN, physical Linux host, or bridged Ubuntu VM are all valid ROS2 peer topologies once appropriate firewall allow rules are in place (see report 20 for root cause and fixes).

ROS2 For Unity graph snapshots can be intermittent in `ros2 topic list`; use actual publish/subscribe data flow as the current acceptance signal.

## Package Composition

| Install set | Expected behavior |
|---|---|
| `dev.unity2foxglove.sdk` | Fully usable by itself for normal Foxglove WebSocket, MCAP, Replay, and FoxRun workflows. |
| `dev.unity2foxglove.ros2forunity` | Installs with `dev.unity2foxglove.sdk`, compiles without a runtime package, and reports missing runtime gracefully. |
| `dev.unity2foxglove.ros2forunity.runtime.*` | Candidate runtime packages kept under the repository root `Packages/`; only the active one is resolved by the Unity project manifest. |
| Adapter + runtime | Enables Unity-as-ROS2-node publish/subscribe through ROS2 For Unity. |
| SDK + adapter + runtime | Full combined Unity2Foxglove workflow. |

Dependency direction is intentionally one-way:

```text
dev.unity2foxglove.sdk does not depend on ROS2 For Unity packages.
dev.unity2foxglove.ros2forunity depends on dev.unity2foxglove.sdk, but can compile without runtime packages.
dev.unity2foxglove.ros2forunity.runtime.* packages must not force the core SDK to load ROS2.
Multiple candidate runtime packages may exist on disk, but exactly one active runtime is resolved in `Unity2Foxglove/Packages/manifest.json` per Unity project.
```

Runtime packages are expected to be package/release artifacts. They should carry their own manifest, checksum, file inventory, third-party notices, and license inventory.

## Local Entrypoints

The repository intentionally keeps local ROS2 and ROS2 For Unity artifact inputs behind lightweight entry directories:

```text
ros2-windows/
  README.md
  ros2_humble
  ros2_jazzy
  ros2_lyrical

r2fu-runtime-artifacts/
  README.md
  humble/
  jazzy/
  lyrical/
```

Only the README files belong in git. The distro entries are local junctions,
symlinks, or restored artifact caches. Scripts should default to these
repo-local paths and allow explicit overrides when a developer or CI machine
uses a different cache location.

## FoxRun Native ROS2 Subscribe

The `FoxRun ROS2 Native Subscribe` Package Manager sample is the direct
Linux/ROS2-to-Unity input route for existing compiled ROS2 message types. It
does not use Foxglove Desktop, Foxglove WebSocket, or FoxRun Publish Data. The
sample exposes four explicit native `Subscribe` contracts. Its serialized
topic fields retain source-defined defaults and remain visible in the Inspector:

| Type | Inspector topic field | QoS |
| --- | --- | --- |
| `std_msgs/msg/String` | String Topic | Reliable |
| `geometry_msgs/msg/Twist` | Twist Topic | Reliable |
| `sensor_msgs/msg/Joy` | Joy Topic | SensorData / BestEffort |
| `sensor_msgs/msg/Imu` | Imu Topic | SensorData / BestEffort |

Resolve exactly one runtime package before entering Play Mode. After a runtime
or communication-mode change that follows native initialization, restart the
Editor; a loaded Windows native ROS2 runtime cannot safely be exchanged in the
same process. The supported Player target is WindowsStandalone64.

Unity and its Linux peer must use the same ROS distro, RMW implementation, ROS
domain, discovery topology, and compatible QoS. FastDDS (`rmw_fastrtps_cpp`)
and Zenoh (`rmw_zenoh_cpp`, Lyrical only) are separate modes, not fallback or
bridge paths. A Player has no Inspector-time runtime switch: choose the runtime
before building, then set `ROS_DISTRO`, `RMW_IMPLEMENTATION`,
`ROS_DOMAIN_ID`, discovery settings, and any Zenoh configuration in the Player
process environment before its first ROS2 initialization.

The generated binding deep-copies each borrowed callback message before its
main-thread apply. Sample Inspector fields show bounded copied scalars, strings
and Joy arrays; do not retain callback message references.
`FoxRunRos2NativeCopyBudgetBytes` defaults to 4 MiB and is normalized to the
portable 1 KiB–256 MiB range. The native-copy budget is latest-wins and
intended for the small standard messages above, not for arbitrary large payload
benchmarks. A copied graph that exceeds it is not applied and is reported as a
bounded `CopyFailed` diagnostic; no WebSocket or alternate-transport fallback
is created.

The Linux acceptance helper treats a ROS2 CLI publication as peer-side
evidence only. With `--unity-log`, it accepts an applied marker only when it was
appended after that specific publication; String runs before Twist so the latter
can reuse the current correlation token. A full expected-negative verdict also
requires `--unity-ready-token` to identify Unity's current READY runtime/RMW
marker and a current positive contract baseline. Graph absence, a no-apply
window, or a matching `ROS_DOMAIN_ID` alone are never interoperability proof.

Custom asmdefs need references to `Unity.FoxgloveSDK`,
`Unity2Foxglove.Ros2ForUnity.Native`, and the selected runtime/message
assemblies. `FOXRUN212` means `Native generation requires the optional Native
assembly reference`; add the Native reference and let Unity recompile instead
of changing the contract to a WebSocket encoding.

ROS domain IDs are discovery isolation, not authentication. Configure network
controls and ROS2 security separately. This packaged-message sample supports
existing compiled `.msg` types and native `Subscribe`; generated custom
FoxRun DTO interfaces use the separate workflow below.

## FoxRun Custom ROS2 Interfaces

The `FoxRun Custom ROS2 Interface` sample supports a locked, generated custom
ROS2 interface for native `Publish`, `Subscribe`, and
`PublishAndSubscribe` contracts. It is an optional R2FU path: the core SDK
remains ROS-free and no custom typesupport is inferred or downloaded at
runtime.

Generate/revise the source package through the Manager's **Data Transport >
ROS 2 Native Runtime (R2FU) — Shared > Custom FoxRun ROS 2 Interface**
preflight controls. Resolve exactly one matching runtime package and exactly
one matching distro-specific typesupport add-on before entering Play Mode. The
source lock digest must match the selected add-on; a stale, multiple, missing,
or incompatible add-on fails closed and creates no custom endpoint.

Use the matching `Scripts/smoke/ros2/phase181_*_acceptance.py` no-argument
helper for a Windows-local Editor bring-up row. It waits for a correlated custom
String subscription before probing nested DTO, sequence, and null/empty values.
Its result is not Linux or Player certification. Matching Linux and Player
rows must be executed with the exact locked source/digest and the same ROS
distro, RMW, domain, discovery configuration, and (for Lyrical Zenoh) explicit
topology.

Custom P&S is deliberately echo-on-apply: same-origin native envelopes drop;
different or empty remote origins apply and may re-publish through the member's
normal policy with a new Unity origin. `FixedRate` feedback topologies are an
operator choice, not a hidden loop-prevention guarantee. Native custom inbound
receipts are not individually recorded to MCAP; they appear in MCAP only when
the normal publish policy later emits the external-facing output representation.
During MCAP replay, the replay-output suppression boundary stops both the live
WebSocket route and custom native ROS2 bus route, so replay does not emit a
second real-time ROS2 stream.

## External Adapter Sample

Install the adapter package and keep candidate runtime packages under the repository root `Packages/` directory:

```text
dev.unity2foxglove.ros2forunity
dev.unity2foxglove.ros2forunity.runtime.humble.win64
dev.unity2foxglove.ros2forunity.runtime.jazzy.win64
dev.unity2foxglove.ros2forunity.runtime.lyrical.win64
```

The Foxglove Manager Inspector exposes one `ROS2 For Unity Runtime` active runtime dropdown. Changing it edits `Unity2Foxglove/Packages/manifest.json` so exactly one runtime package is active, then Unity performs a normal package reimport and script compilation. This is intentionally slower than a scripting-define switch because Unity must not import two sets of ROS2 managed message DLLs or native runtime DLLs at once.

When the active runtime is Lyrical and the package contains the Zenoh payload, the Inspector also exposes a `Communication Mode` dropdown. `FastDDS (default)` sets `RMW_IMPLEMENTATION=rmw_fastrtps_cpp`; `Zenoh (rmw_zenoh_cpp)` sets `RMW_IMPLEMENTATION=rmw_zenoh_cpp` before ROS2 For Unity initializes. Zenoh mode is Lyrical-only and should be selected before entering Play Mode in a fresh Editor session.

FastDDS and Zenoh are separate ROS2 communication modes. They do not discover or exchange topics with each other. Keep Unity, ROS2 CLI probes, RViz2, and any external ROS2 nodes on the same RMW implementation, ROS domain, and discovery topology. For Zenoh validation, run Lyrical peers with `RMW_IMPLEMENTATION=rmw_zenoh_cpp`; FastDDS peers should keep `RMW_IMPLEMENTATION=rmw_fastrtps_cpp`.

Zenoh deployments also need a working Zenoh discovery path before Play Mode. For the local router topology used by the repository smoke tests, start the Lyrical router from the local ROS2 entrypoint, for example `ros2-windows/ros2_lyrical/Lib/rmw_zenoh_cpp/rmw_zenohd.exe`, or configure an equivalent peer/multicast topology for all participants. Selecting `Zenoh (rmw_zenoh_cpp)` in the Inspector only chooses Unity's RMW implementation. It does not start a router and does not bridge FastDDS traffic.

If the current Editor session has not entered Play Mode yet, you can switch runtime packages and enter Play Mode without restarting. After an Editor session has loaded one ROS2 runtime in Play Mode, switching to a different runtime requires a Unity restart before Play Mode. Windows native ROS2 plugins stay loaded until the Editor process exits, so the Inspector blocks unsafe Play Mode entry and offers a restart action instead of letting Humble, Jazzy, and Lyrical DLLs mix in one process.

The adapter package manages only the base Standalone build-target symbol:

```text
UNITY2FOXGLOVE_ROS2_FOR_UNITY
```

The native bridge asmdef includes both `Editor` and `WindowsStandalone64` because Editor Play Mode compiles against the active Standalone build target symbols in this workflow. The base symbol gates the native ROS2 bridge source in Editor Play Mode and Windows Standalone builds; it is not a per-distro runtime selector.

Runtime detection uses the Unity project manifest as the single source of truth. Candidate packages are discovered by convention from repository-root `Packages/dev.unity2foxglove.ros2forunity.runtime.*` directories and are not copied into `Unity2Foxglove/Packages/` as embedded packages.

The adapter runtime and editor asmdefs remain `autoReferenced=true` on purpose. Imported Package Manager samples land in predefined project assemblies, so this convenience keeps the facade interfaces visible without requiring users to add sample asmdefs. The core SDK still has no reference to this optional package.

The external source-only adapter samples may use `UNITY2FOXGLOVE_ROS2_FOR_UNITY` with an external ROS2 For Unity import. Per-distro runtime symbols are not compile gates. The base symbol is reconciled for the Standalone build target only.

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

## RViz2 MarkerArray Acceptance

The `RViz2 MarkerArray Acceptance` sample adds a narrow scene-marker route for RViz2. It publishes:

```text
/markers
```

The `/markers` topic uses:

```text
visualization_msgs/msg/MarkerArray
```

The v1 payload is one animated cube marker in the `map` frame with deterministic positive 31-bit marker IDs, zero marker lifetime, and periodic `DELETE`/`DELETEALL` cleanup messages. It does not claim arbitrary marker types, mesh resources, text markers, interactive markers, PointCloud2 subscription, Camera/Image, MCAP replay fanout, rosbag2, or any core SDK ROS2 dependency.

## RViz2 Standard Visualization v1

The `RViz2 Standard Visualization v1` sample is a docs/config/evidence kit that consolidates the RViz2 workflow from the TF/LaserScan, PointCloud2, and MarkerArray samples. It does not contain publishers by itself. Import the three publisher samples first, then import the v1 kit for the combined RViz2 config and checklist.

The v1 topic matrix is:

```text
/tf      -> tf2_msgs/msg/TFMessage
/scan    -> sensor_msgs/msg/LaserScan
/points  -> sensor_msgs/msg/PointCloud2
/markers -> visualization_msgs/msg/MarkerArray
```

The combined scene must avoid conflicting TF ownership. Let one component own each transform edge, especially `map -> base_link`. The core SDK remains ROS-free; this v1 workflow remains optional and ROS2 For Unity driven.

## ROS2 Standard Message Expansion

The `ROS2 Standard Message Expansion` sample adds CLI-validated source components for:

```text
/camera/camera_info -> sensor_msgs/msg/CameraInfo
/camera/image_raw   -> sensor_msgs/msg/Image
/imu/data           -> sensor_msgs/msg/Imu
/odom               -> nav_msgs/msg/Odometry
/pose               -> geometry_msgs/msg/PoseStamped
/fix                -> sensor_msgs/msg/NavSatFix
```

This sample is not a new RViz2 productization gate. It uses explicit source components for camera, IMU, odometry, pose, and synthetic NavSatFix data, and the primary check is the sample README's Python acceptance helper. It does not publish `/tf`, does not claim ROS2 `sensor_data` QoS parity, and does not add image rectification, calibration services, state estimation, Nav2, `/clock`, MCAP fanout, rosbag2, or any core SDK ROS2 dependency.

The default topics are conventional ROS2 names and can collide with real drivers or Nav2 stacks. Production projects should namespace them, for example `/unity/odom` or `/unity/camera/image_raw`.

## PointCloud2 Native DDS Output

When the adapter package and exactly one Win64 runtime package are installed,
the PointCloud2 Native path is a product setting, not a sample component setup.
In Unity:

1. On `FoxgloveManager`, enable `ROS2 Native (R2FU)`.
2. On `FoxglovePointCloudPublisher`, choose `PointCloud2 Native`.
3. Set the publisher topic and frame id, for example topic `/points` and frame
   `os_lidar`.
4. Leave `Publish PointCloud2 TF Anchor` disabled when your scene, SLAM, or
   robot TF tree already publishes the frame. Enable it only as an RViz fallback
   when no other `/tf` source resolves the PointCloud2 frame; the fallback
   publishes `map -> <Frame Id>`.

The optional R2FU package automatically subscribes to prepared
`PointCloud2 Native` frames and publishes:

```text
/points
/tf
```

The `/points` topic uses:

```text
sensor_msgs/msg/PointCloud2
```

The `/tf` anchor topic uses:

```text
tf2_msgs/msg/TFMessage
```

No extra smoke component is required for the product path. The core SDK prepares
the compacted full-stride payload before the R2FU bridge receives it, so the
bridge does not rebuild the point cloud from `VirtualLidar.LastFrame.Points`. If
ROS2 For Unity requires main-thread publishing, the main-thread work remains the
final generated-message publish call.

Validate from an external ROS2 shell while Unity is in Play mode:

```bash
ros2 topic info /points
ros2 topic hz /points
ros2 topic bw /points
ros2 topic echo /points --once
ros2 topic info /tf
ros2 topic echo /tf --once
```

The `Virtual LiDAR PointCloud2 Digital Twin` sample remains available as an
optional diagnostic harness, but it is not required for product acceptance. This
path does not replace the Draco Foxglove visualization path and does not add a
ROS2 dependency to the core SDK.

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
