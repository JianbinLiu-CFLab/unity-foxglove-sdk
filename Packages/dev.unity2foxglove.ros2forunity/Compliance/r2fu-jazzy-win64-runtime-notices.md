# R2FU Jazzy Win64 Runtime Artifact Notices

This notice describes the local runtime artifact candidate:

```text
Ros2ForUnity_jazzy_standalone_windows_x86_64.zip
```

The artifact is a candidate input for a future Unity runtime package. It is not published by this adapter package, it is not committed to git, and it is not a completed legal audit.

## Artifact Identity

| Field | Value |
|---|---|
| Runtime id | `r2fu-jazzy-win64` |
| ROS distro | `jazzy` |
| Platform | Windows x64 |
| Build type | standalone |
| RMW | `rmw_fastrtps_cpp` |
| SHA-256 | `f20f20047d1a2087aad1d9e280c7a04943935d9019793b3f11d399ec54899232` |
| Inventory | `Compliance/r2fu-jazzy-win64-runtime-inventory.json` |

## Attribution Boundary

Unity2Foxglove does not claim authorship of RobotecAI ROS2 For Unity, ros2cs, generated ROS2 message assemblies, generated native message support libraries, ROS2 Jazzy native libraries, Fast DDS, Fast CDR, RMW FastRTPS, or transitive runtime DLLs.

The adapter package remains source-only. Runtime binaries belong in a separate runtime package or release artifact that carries its own manifest, checksum, file inventory, third-party notices, and license inventory.

## Known Upstream Components

| Component | Relationship |
|---|---|
| RobotecAI ROS2 For Unity | Unity integration surface for ROS2 node behavior |
| ros2cs | ROS2 C# binding stack used by ROS2 For Unity |
| ROS2 Jazzy native runtime | `rcl`, `rcutils`, `rmw`, message type support, and related runtime DLLs |
| Fast DDS / Fast CDR | DDS and CDR runtime dependency family used by the FastRTPS RMW path |
| RMW FastRTPS | `rmw_fastrtps_cpp` runtime path used by the current Windows artifact |
| Generated message support | Managed message assemblies plus native ROSIDL/type-support DLLs |

## Critical Runtime Closure

The artifact must contain these runtime closure DLLs:

```text
rcl.dll
yaml.dll
spdlog.dll
fmt.dll
```

If the transitive DLLs are missing, Unity can report `UnsatisfiedLinkError: rcl.dll` even when `rcl.dll` itself is present.

## Redistribution Caveats

- This artifact is a candidate until a runtime package is produced and accepted in a fresh Unity project.
- The inventory is an engineering inventory generated from the local zip, not a complete legal audit.
- A future runtime package must include complete transitive license attribution before public distribution.
- WSL2 NAT is diagnostic-only for DDS discovery; Windows ROS2 Jazzy or a real remote Linux topology should be used for acceptance.
