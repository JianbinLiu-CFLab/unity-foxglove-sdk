# Unity2Foxglove ROS 2 Bridge Sidecar

This is an experimental Phase 94 sidecar. It listens on loopback TCP, receives `U2R2` bridge frames from Unity2Foxglove, and republishes their CDR payload bytes through `rclcpp::GenericPublisher`.

It is intentionally not a product bridge:

- localhost only;
- no QoS UI;
- no health panel;
- no automatic ROS 2 install;
- no Windows-native ROS 2 support;
- Gate B validates only `/unity/tf`, `/unity/laser_scan`, and `/unity/point_cloud`.

Normal Unity2Foxglove Foxglove WebSocket use does not require ROS.

## Prerequisites

Use Ubuntu or WSL with ROS 2 Humble or Jazzy:

```bash
source /opt/ros/$ROS_DISTRO/setup.bash
sudo apt install ros-$ROS_DISTRO-foxglove-msgs
sudo apt install nlohmann-json3-dev
ros2 interface show foxglove_msgs/msg/FrameTransform
ros2 interface show foxglove_msgs/msg/LaserScan
ros2 interface show foxglove_msgs/msg/PointCloud
```

If `ros-$ROS_DISTRO-foxglove-msgs` is unavailable in your apt setup, add `foxglove_msgs` to a source workspace and build it there. Phase 94 does not include an automatic installer.

When using WSL, prefer running both the sidecar and the `.NET` sender inside WSL. Do not bind the sidecar to `0.0.0.0` to work around Windows-to-WSL forwarding.

## Build

Place or symlink this package inside a ROS 2 workspace `src` directory, then run:

```bash
source /opt/ros/$ROS_DISTRO/setup.bash
colcon build --packages-select unity2foxglove_ros2_bridge
source install/setup.bash
```

## Run

Default mode forwards the Phase 91/93 CDR encapsulation header unchanged:

```bash
ros2 run unity2foxglove_ros2_bridge unity2foxglove_ros2_bridge --host 127.0.0.1 --port 8767 --payload-format cdr-with-encapsulation
```

If `ros2 topic echo` sees the topics but cannot decode plausible values, retry the diagnostic body-only mode:

```bash
ros2 run unity2foxglove_ros2_bridge unity2foxglove_ros2_bridge --host 127.0.0.1 --port 8767 --payload-format cdr-body-only
```

Record which mode works for your ROS 2 distro and RMW implementation.

## Send Smoke Messages

From the Unity2Foxglove repository root:

```bash
dotnet run --no-restore --project Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj -- --phase94-bridge-send 127.0.0.1 8767
```

Expected sender output:

```text
[phase94] connected 127.0.0.1:8767
[phase94] sent /unity/tf foxglove_msgs/msg/FrameTransform count=20
[phase94] sent /unity/laser_scan foxglove_msgs/msg/LaserScan count=20
[phase94] sent /unity/point_cloud foxglove_msgs/msg/PointCloud count=20
[phase94] PASS bridge send smoke
```

## Inspect ROS 2

```bash
ros2 topic list
ros2 topic info /unity/tf
ros2 topic info /unity/laser_scan
ros2 topic info /unity/point_cloud
ros2 topic echo --once /unity/tf
ros2 topic echo --once /unity/laser_scan
ros2 topic hz /unity/point_cloud
```

Gate B passes when the three topics appear with the expected `foxglove_msgs` types and echo/hz output is plausible.
