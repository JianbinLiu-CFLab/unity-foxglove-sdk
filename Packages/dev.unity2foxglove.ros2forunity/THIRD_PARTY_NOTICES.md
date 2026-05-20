# Third-Party Notices

This optional package provides the ROS2 For Unity adapter boundary for Unity2Foxglove.

This package does not bundle the ROS2 For Unity runtime asset, generated message assemblies, native ROS2 libraries, Fast DDS/RMW components, or ros2cs binaries. Runtime binaries belong in separate runtime packages such as `dev.unity2foxglove.ros2forunity.runtime.jazzy.win64`.

## RobotecAI ROS2 For Unity

- **Project**: RobotecAI ROS2 For Unity
- **Repository**: https://github.com/RobotecAI/ros2-for-unity
- **License**: Apache-2.0
- **Copied license**: `Upstream/LICENSE.AL2`
- **Historical upstream release evidence**: `Ros2ForUnity_humble_standalone_windows11.zip`, version `1.3.0`
- **Current runtime package candidate**: `Ros2ForUnity_Jazzy_standalone_windows10.zip`, produced by the local Jazzy rebuild path
- **Bundle status**: not bundled

RobotecAI states that ROS2 For Unity is officially supported for AWSIM/Autoware users and that the Robotec team cannot support and maintain the project for the general community. Unity2Foxglove must preserve that caveat and must not imply upstream community support for Unity2Foxglove-specific integration or redistributed bundles.

Unity2Foxglove does not claim authorship of RobotecAI ROS2 For Unity C# scripts, generated ROS2 messages, native ROS2 runtime libraries, Fast DDS/RMW components, or future extracted runtime files.

## ros2cs

- **Project**: ros2cs
- **Repository**: https://github.com/RobotecAI/ros2cs
- **Usage relationship**: ROS2 For Unity uses/pulls ros2cs as part of its ROS2 C# binding stack.
- **Bundle status**: not bundled in this adapter package

Unity2Foxglove does not claim authorship of ros2cs. If a later runtime package bundles ROS2 For Unity runtime artifacts, the package must include a complete transitive inventory covering ros2cs and every redistributed native or managed dependency.

## Future Binary Bundling Boundary

Before any Unity2Foxglove runtime package redistributes a runtime binary, generated metadata file, generated message assembly, or extracted ROS2 For Unity asset, a future update must refresh:

- `Compliance/ros2-for-unity-adoption-manifest.json`;
- this notice file;
- the runtime file inventory;
- license and attribution records for all transitive dependencies.

Until then, this adapter package is source-only and runtime artifacts remain outside this package.
