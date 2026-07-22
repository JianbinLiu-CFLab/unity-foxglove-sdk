# Third-Party Notices

This runtime package redistributes a locally rebuilt ROS2 For Unity Humble Windows x64 runtime payload.

Unity2Foxglove does not claim authorship of RobotecAI ROS2 For Unity, ros2cs, generated ROS2 message assemblies, generated native message support libraries, ROS2 Humble native libraries, Fast DDS, Fast CDR, RMW FastRTPS, or transitive runtime DLLs.

## Runtime Artifact

| Field | Value |
|---|---|
| Artifact | `Ros2ForUnity_humble_standalone_windows_x86_64.zip` |
| Runtime id | `r2fu-humble-win64` |
| ROS distro | `humble` |
| Platform | Windows x64 |
| Build type | standalone |
| RMW | `rmw_fastrtps_cpp` |
| SHA-256 | `6937f348b2abdf40614379173bb81ba55090dc1541cab616d1a0f1e248ceb5b0` |
| Inventory file count | `1131` |

## Known Upstream Components

| Component | Relationship |
|---|---|
| RobotecAI ROS2 For Unity | Unity integration surface for ROS2 node behavior |
| ros2cs | ROS2 C# binding stack used by ROS2 For Unity |
| ROS2 Humble native runtime | `rcl`, `rcutils`, `rmw`, message type support, and related runtime DLLs |
| Fast DDS / Fast CDR | DDS and CDR runtime dependency family used by the FastRTPS RMW path |
| RMW FastRTPS | `rmw_fastrtps_cpp` runtime path used by the current Windows artifact |
| Generated message support | Managed message assemblies plus native ROSIDL/type-support DLLs |

## Critical Runtime Closure

The package includes the transitive runtime DLLs required for Unity to load `rcl.dll`, including:

```text
rcl.dll
yaml.dll
spdlog.dll
```

If these closure DLLs are removed, Unity can report `UnsatisfiedLinkError: rcl.dll` even when `rcl.dll` itself is present.

## Redistribution Caveats

- This package is a prototype until fresh-project acceptance passes.
- The inventory is an engineering inventory generated from the local runtime artifact, not a complete legal audit.
- The current artifact still carries OpenSSL 1.1.x runtime DLLs through transitive ROS2/DDS closure. They are not used by the default FastRTPS visualization path unless DDS security/TLS features are enabled, but OpenSSL 1.1.x is end-of-life and must be replaced with OpenSSL 3.x in a future Humble artifact rebuild before release-hardening.
- Public release should refresh transitive license attribution before registry or binary distribution.
- WSL2 NAT can hide DDS discovery and should be treated as diagnostic-only for Windows package acceptance. Configure Windows Defender Firewall allow rules for Fast DDS UDP ports, then prefer Windows ROS2 Humble or a real remote Linux topology for final external-graph acceptance.

RobotecAI states that ROS2 For Unity is officially supported for AWSIM/Autoware users and that the Robotec team cannot support and maintain the project for the general community. Unity2Foxglove must preserve that caveat and must not imply upstream community support for Unity2Foxglove-specific packaging.
