# Third-Party Notices

This runtime package redistributes a locally rebuilt ROS2 For Unity Lyrical Windows x64 runtime payload.

Unity2Foxglove does not claim authorship of RobotecAI ROS2 For Unity, ros2cs, generated ROS2 message assemblies, generated native message support libraries, ROS2 Lyrical native libraries, Fast DDS, Fast CDR, RMW FastRTPS, or transitive runtime DLLs.

## Runtime Artifact

| Field | Value |
|---|---|
| Artifact | `Ros2ForUnity_lyrical_standalone_windows_x86_64.zip` |
| Runtime id | `r2fu-lyrical-win64` |
| ROS distro | `lyrical` |
| Platform | Windows x64 |
| Build type | standalone |
| Default RMW | `rmw_fastrtps_cpp` |
| Supported RMW | `rmw_fastrtps_cpp`, `rmw_zenoh_cpp` |
| SHA-256 | `b31f12cccd2c702ec18c5f5ededce9239d8a2bbe244d54b5526606a96a3a5b71` |
| Inventory file count | `1229` |

## Known Upstream Components

| Component | Relationship |
|---|---|
| RobotecAI ROS2 For Unity | Unity integration surface for ROS2 node behavior |
| ros2cs | ROS2 C# binding stack used by ROS2 For Unity |
| ROS2 Lyrical native runtime | `rcl`, `rcutils`, `rmw`, message type support, and related runtime DLLs |
| Fast DDS / Fast CDR | DDS and CDR runtime dependency family used by the default FastRTPS RMW path |
| RMW FastRTPS | `rmw_fastrtps_cpp` default runtime path used by this Windows artifact |
| RMW Zenoh | `rmw_zenoh_cpp` optional runtime path for Lyrical-only routed communication |
| Generated message support | Managed message assemblies plus native ROSIDL/type-support DLLs |

## Critical Runtime Closure

The package includes the transitive runtime DLLs required for Unity to load `rcl.dll`, including:

```text
rcl.dll
yaml.dll
spdlog.dll
fmt.dll
fastdds-3.6.dll
rosidl_buffer_backend_registry.dll
rosidl_dynamic_typesupport_fastrtps.dll
rmw_zenoh_cpp.dll
zenohc.dll
rosgraph_msgs_assembly.dll
```

If these closure DLLs are removed, Unity can report `UnsatisfiedLinkError: rcl.dll` even when `rcl.dll` itself is present.

## Redistribution Caveats

- This package is a prototype until fresh-project acceptance passes.
- The inventory is an engineering inventory generated from the local runtime artifact, not a complete legal audit.
- Public release should refresh transitive license attribution before registry or binary distribution.
- WSL2 NAT can hide DDS discovery and should be treated as diagnostic-only for Windows package acceptance. Configure Windows Defender Firewall allow rules for Fast DDS UDP ports, then prefer Windows ROS2 Lyrical or a real remote Linux topology for final external-graph acceptance.

RobotecAI states that ROS2 For Unity is officially supported for AWSIM/Autoware users and that the Robotec team cannot support and maintain the project for the general community. Unity2Foxglove must preserve that caveat and must not imply upstream community support for Unity2Foxglove-specific packaging.
