# Unity2Foxglove FoxRun ROS2 Interfaces

This is a source-only static interface package. It contains generated `.msg` files, no ros2cs assembly, native DLL, typesupport, CMake build output, or runtime endpoint.

## Linux ROS2 workspace

Copy or symlink `Ros2Package~` into a normal ROS2 workspace `src/` directory, then build the explicit locked revision:

```bash
colcon build --packages-select unity2foxglove_foxrun_interfaces_v1
```

Before copying, verify the checked-in source bytes from the Unity2Foxglove repository root:

```bash
python Scripts/ros2forunity/interfaces/interface_digest.py --package-root Packages/dev.unity2foxglove.foxrun.ros2.interfaces
```

The command must print the lock digest before building. A wire-changing DTO edit requires an explicit `_vN` package revision; generation never silently chooses one.

Every envelope contains `foxrun_origin_id`, `foxrun_sequence`, `foxrun_stamp`, and the generated payload. The package is intentionally not a runtime binary distribution.
