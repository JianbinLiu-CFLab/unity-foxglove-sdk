# Unity2Foxglove ROS2 For Unity Runtime - Humble Win64

This package is an optional Windows x64 runtime for the Unity2Foxglove ROS2 For Unity integration. It carries the ROS2 For Unity runtime files, generated message assemblies, native ROS2 Humble DLLs, Fast DDS/RMW files, ros2cs files, metadata, inventory, and notices.

## Package Role

Install this package when a Unity project needs to run as a ROS2 node through ROS2 For Unity on Windows x64.

This package is independent from `dev.unity2foxglove.sdk` and can import by itself. It does not provide the high-level Unity2Foxglove facade or samples by itself; those live in `dev.unity2foxglove.ros2forunity`.

The runtime package intentionally does not declare a UPM dependency on `dev.unity2foxglove.ros2forunity`: standalone import is useful for package diagnostics, while adapter-backed workflows should install the adapter package explicitly.

Recommended combinations:

- `dev.unity2foxglove.ros2forunity.runtime.humble.win64` alone: imports runtime files, manifest, notices, and diagnostics.
- `dev.unity2foxglove.ros2forunity` plus this runtime package: enables adapter-backed ROS2 publish/subscribe.
- `dev.unity2foxglove.sdk` plus adapter plus this runtime package: enables the combined Unity2Foxglove workflow.

## One Runtime Policy

Install only one `dev.unity2foxglove.ros2forunity.runtime.*` package in a Unity project. Multiple ROS2 runtime packages can load conflicting native DLLs or generated message assemblies.

Do not import the old `Assets/Ros2ForUnity` asset folder and this package in the same project. Use either an external asset-folder runtime or this package runtime.

The script assembly is intentionally named `Unity2Foxglove.Ros2ForUnity.Runtime` across all distro runtime packages. The adapter package references that stable assembly name, while the one-runtime policy and package conflict metadata prevent multiple distro runtimes from being active in the same Unity project.

## Runtime Identity

- ROS distro: Humble
- Platform: Windows x64
- Build type: standalone
- RMW implementation: `rmw_fastrtps_cpp`
- Runtime id: `r2fu-humble-win64`
- Artifact source: `Ros2ForUnity_humble_standalone_windows_x86_64.zip`
- SHA-256: `2b40c05faac7444e61bcb9f0ca3eac4e2316da5fb28648367eb3ca5328808c5f`

The runtime manifest is `RuntimeSupport/runtime-manifest.json`. The file inventory is `RuntimeSupport/r2fu-humble-win64-runtime-inventory.json`.

## Known Artifact Debt

The current Humble artifact still carries OpenSSL 1.1.x runtime DLLs through its transitive ROS2/DDS closure. Those DLLs are not used by the default FastRTPS visualization path unless DDS security/TLS features are enabled, but OpenSSL 1.1.x is end-of-life. Treat this as an artifact refresh requirement: a future Humble runtime rebuild must move the transitive OpenSSL dependency to OpenSSL 3.x before this package is considered release-hardened.

## Package Path Patch

The bundled `ROS2ForUnity.cs` keeps the upstream `Assets/Ros2ForUnity` lookup and adds a package-path fallback so Unity Editor can load this runtime from:

```text
Packages/dev.unity2foxglove.ros2forunity.runtime.humble.win64/Runtime/Ros2ForUnity
```

This patch is limited to locating runtime files from a Unity package. It does not change ROS2 For Unity node, publisher, subscriber, or DDS behavior.

## Network Acceptance Notes

WSL2 NAT can hide DDS discovery and should be treated as diagnostic-only for Windows package acceptance. Configure Windows Defender Firewall allow rules for Fast DDS UDP ports, then prefer Windows ROS2 Humble or a real remote Linux topology for final external-graph acceptance.

## Support Boundary

This is a prototype runtime package. Fresh-project install acceptance and public release readiness are separate gates. Linux, macOS, Jazzy, and Lyrical runtime packages are not included here.

RobotecAI states that ROS2 For Unity is officially supported for AWSIM/Autoware users and that the Robotec team cannot support and maintain the project for the general community. Unity2Foxglove-specific packaging and support belong to Unity2Foxglove, not RobotecAI.
