# Third-Party Notices

This runtime package redistributes a locally rebuilt ROS2 For Unity Jazzy Windows x64 runtime payload.

Unity2Foxglove does not claim authorship of RobotecAI ROS2 For Unity, ros2cs, generated ROS2 message assemblies, generated native message support libraries, ROS2 Jazzy native libraries, Fast DDS, Fast CDR, RMW FastRTPS, or transitive runtime DLLs.

## Runtime Artifact

| Field | Value |
|---|---|
| Artifact | `Ros2ForUnity_jazzy_standalone_windows_x86_64.zip` |
| Runtime id | `r2fu-jazzy-win64` |
| ROS distro | `jazzy` |
| Platform | Windows x64 |
| Build type | standalone |
| RMW | `rmw_fastrtps_cpp` |
| SHA-256 | `df4806b750435b3a1252f39b46dd2e4e60ddc0eb6ac57989bcf00adb23fe29f3` |
| Inventory file count | `1198` |

## Known Upstream Components

| Component | Relationship |
|---|---|
| RobotecAI ROS2 For Unity | Unity integration surface for ROS2 node behavior |
| ros2cs | ROS2 C# binding stack used by ROS2 For Unity |
| ROS2 Jazzy native runtime | `rcl`, `rcutils`, `rmw`, message type support, and related runtime DLLs |
| Fast DDS / Fast CDR | DDS and CDR runtime dependency family used by the FastRTPS RMW path |
| RMW FastRTPS | `rmw_fastrtps_cpp` runtime path used by the current Windows artifact |
| Generated message support | Managed message assemblies plus native ROSIDL/type-support DLLs |

The current upstream runtime closure includes ROS2 `test_msgs` managed and native type-support artifacts. They are retained only because they are present in the generated `r2fu-jazzy-win64` inventory; production package trimming must happen in the runtime rebuild pipeline, not by manually deleting individual DLLs from this package.

## Security-Relevant Native DLL Hashes

| File | SHA-256 |
|---|---|
| `Runtime/Ros2ForUnity/Plugins/Windows/x86_64/libcrypto-3-x64.dll` | `124c5f40a6cf0e642f9d92784dd66314fd548b4fdf93c543bf478e71e1209f9d` |
| `Runtime/Ros2ForUnity/Plugins/Windows/x86_64/libssl-3-x64.dll` | `4158ad85699d9c5428b62778883b16b6c21b3b7da11fe60a0cc24ea37687a9fd` |

## Critical Runtime Closure

The package includes the transitive runtime DLLs required for Unity to load `rcl.dll`, including:

```text
rcl.dll
yaml.dll
spdlog.dll
fmt.dll
```

If these closure DLLs are removed, Unity can report `UnsatisfiedLinkError: rcl.dll` even when `rcl.dll` itself is present.

## Redistribution Caveats

- This package is a prototype until fresh-project acceptance passes.
- The inventory is an engineering inventory generated from the local runtime artifact, not a complete legal audit.
- Public release should refresh transitive license attribution before registry or binary distribution.
- WSL2 NAT can hide DDS discovery and should be treated as diagnostic-only for Windows package acceptance. Configure Windows Defender Firewall allow rules for Fast DDS UDP ports, then prefer Windows ROS2 Jazzy or a real remote Linux topology for final external-graph acceptance.

RobotecAI states that ROS2 For Unity is officially supported for AWSIM/Autoware users and that the Robotec team cannot support and maintain the project for the general community. Unity2Foxglove must preserve that caveat and must not imply upstream community support for Unity2Foxglove-specific packaging.
