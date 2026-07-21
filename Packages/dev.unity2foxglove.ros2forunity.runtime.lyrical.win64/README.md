# Unity2Foxglove ROS2 For Unity Runtime - Lyrical Win64

This package is an optional Windows x64 runtime for the Unity2Foxglove ROS2 For Unity integration. It carries the ROS2 For Unity runtime files, generated message assemblies, native ROS2 Lyrical DLLs, Fast DDS/RMW files, optional Zenoh RMW files, ros2cs files, metadata, inventory, and notices.

## Package Role

Install this package when a Unity project needs to run as a ROS2 node through ROS2 For Unity on Windows x64.

This package is independent from `dev.unity2foxglove.sdk` and can import by itself. It does not provide the high-level Unity2Foxglove facade or samples by itself; those live in `dev.unity2foxglove.ros2forunity`.

Recommended combinations:

- `dev.unity2foxglove.ros2forunity.runtime.lyrical.win64` alone: imports runtime files, manifest, notices, and diagnostics.
- `dev.unity2foxglove.ros2forunity` plus this runtime package: enables adapter-backed ROS2 publish/subscribe.
- `dev.unity2foxglove.sdk` plus adapter plus this runtime package: enables the combined Unity2Foxglove workflow.

The runtime package intentionally declares no UPM dependency on the facade package. It is a binary/runtime payload that must remain importable for diagnostics and artifact validation even when the adapter facade is not installed.

## One Runtime Policy

Install only one `dev.unity2foxglove.ros2forunity.runtime.*` package in a Unity project. Multiple ROS2 runtime packages can load conflicting native DLLs or generated message assemblies.

Do not import the old `Assets/Ros2ForUnity` asset folder and this package in the same project. Use either an external asset-folder runtime or this package runtime.

## Runtime Identity

- ROS distro: Lyrical
- Platform: Windows x64
- Build type: standalone
- Default RMW implementation: `rmw_fastrtps_cpp`
- Supported RMW implementations: `rmw_fastrtps_cpp`, `rmw_zenoh_cpp`
- Runtime id: `r2fu-lyrical-win64`
- Artifact source: `Ros2ForUnity_lyrical_standalone_windows_x86_64.zip`
- SHA-256: `1d018510d1bf4e5b901eb9555adec5ca5179acced28685df1192aa615483a096`

The runtime manifest is `RuntimeSupport/runtime-manifest.json`. The file inventory is `RuntimeSupport/r2fu-lyrical-win64-runtime-inventory.json`.

## Package Path Patch

The bundled `ROS2ForUnity.cs` keeps the upstream `Assets/Ros2ForUnity` lookup and adds a package-path fallback so Unity Editor can load this runtime from:

```text
Packages/dev.unity2foxglove.ros2forunity.runtime.lyrical.win64/Runtime/Ros2ForUnity
```

This patch is limited to locating runtime files from a Unity package. It does not change ROS2 For Unity node, publisher, subscriber, or DDS behavior.

## Network Acceptance Notes

WSL2 NAT can hide DDS discovery and should be treated as diagnostic-only for Windows package acceptance. Configure Windows Defender Firewall allow rules for Fast DDS UDP ports, then prefer Windows ROS2 Lyrical or a real remote Linux topology for final external-graph acceptance. Zenoh mode is Lyrical-only and requires selecting `rmw_zenoh_cpp` before ROS2 For Unity initializes, plus a reachable Zenoh router for routed topologies. Zenoh config files are mirrored under `Plugins/Windows/x86_64/share` for native runtime closure and `StreamingAssets/Ros2ForUnity/share` for Unity player access; package validation requires the mirrored files to stay byte-identical.

The bundled Zenoh router config is a development profile. It listens on `tcp/[::]:7447`, exits if port `7447` is already bound, has no authentication or ACLs, enables read-only Zenoh adminspace for topology inspection, and keeps high pending/session limits for large ROS2 graph startup bursts. Use it only on trusted lab networks. For CI, shared office networks, or production-like deployments, copy the router config to a localhost-only or ACL-protected profile with lower connection limits and disabled adminspace.

## Support Boundary

This is a prototype runtime package. Fresh-project install acceptance and public release readiness are separate gates. Linux, macOS, Jazzy, Humble, and Ubuntu Lyrical runtime packages are not included here.

RobotecAI states that ROS2 For Unity is officially supported for AWSIM/Autoware users and that the Robotec team cannot support and maintain the project for the general community. Unity2Foxglove-specific packaging and support belong to Unity2Foxglove, not RobotecAI.
