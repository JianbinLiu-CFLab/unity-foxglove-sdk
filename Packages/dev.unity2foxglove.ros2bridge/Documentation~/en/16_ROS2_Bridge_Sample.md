# ROS2 Bridge Sample

The ROS2 Bridge Sample is a guided path for publishing Unity data into a local ROS2 graph through the optional `unity2foxglove_ros2_bridge` sidecar.

Unity does not launch the sidecar. Keep Unity, ROS2, and the sidecar as separate processes so the bridge is easy to inspect and stop.

## 1. What This Sample Covers

Required ROS2 Bridge topics:

| Topic | ROS2 schema |
| --- | --- |
| `/unity2foxglove/tf` | `foxglove_msgs/msg/FrameTransform` |
| `/unity2foxglove/scene` | `foxglove_msgs/msg/SceneUpdate` |
| `/unity2foxglove/camera` | `foxglove_msgs/msg/CompressedImage` |
| `/unity2foxglove/camera_calibration` | `foxglove_msgs/msg/CameraCalibration` |
| `/unity2foxglove/laser_scan` | `foxglove_msgs/msg/LaserScan` |
| `/unity2foxglove/point_cloud` | `foxglove_msgs/msg/PointCloud` |

Optional topic:

| Topic | ROS2 schema |
| --- | --- |
| `/unity2foxglove/point_cloud_draco` | `foxglove_msgs/msg/CompressedPointCloud` |

The Draco topic is skipped when the bundled native Draco plugin is unavailable.

## 2. Optional Preflight

The sample can be accepted without pressing a health button. The primary acceptance signal is live ROS2 topic output plus Unity bridge status: `Connected`, increasing `Sent Frames`, and zero dropped/failed frames.

The optional Manager Inspector **ROS2 Bridge Health** check can help diagnose ROS2 CLI, `foxglove_msgs`, bundled interfaces, or local sidecar health ping issues when topics do not appear. It is a troubleshooting aid, not a required publish step.

In a ROS2 shell:

```bash
source /opt/ros/$ROS_DISTRO/setup.bash
ros2 interface show foxglove_msgs/msg/FrameTransform
ros2 interface show foxglove_msgs/msg/SceneUpdate
ros2 interface show foxglove_msgs/msg/CompressedImage
ros2 interface show foxglove_msgs/msg/CameraCalibration
ros2 interface show foxglove_msgs/msg/LaserScan
ros2 interface show foxglove_msgs/msg/PointCloud
ros2 interface show foxglove_msgs/msg/CompressedPointCloud
```

## 3. Build The Sidecar

Create or reuse a ROS2 workspace:

```bash
source /opt/ros/$ROS_DISTRO/setup.bash
mkdir -p ~/u2f_ros2_ws/src
cd ~/u2f_ros2_ws/src
ln -s <repo>/Tools/ros2_bridge/unity2foxglove_ros2_bridge unity2foxglove_ros2_bridge
cd ~/u2f_ros2_ws
colcon build --packages-select unity2foxglove_ros2_bridge --symlink-install
source install/setup.bash
```

Replace `<repo>` with the repository path on your machine.

## 4. Start The Sidecar

Recommended launch command:

```bash
ros2 launch unity2foxglove_ros2_bridge unity2foxglove_bridge.launch.py host:=127.0.0.1 port:=8767 payload_format:=cdr-with-encapsulation
```

Equivalent direct command:

```bash
ros2 run unity2foxglove_ros2_bridge unity2foxglove_ros2_bridge --host 127.0.0.1 --port 8767 --payload-format cdr-with-encapsulation
```

## 5. Run Unity

1. Import **ROS2 Bridge Sample** from Package Manager.
2. Open `Scenes/Ros2BridgeSample.unity`.
3. Press Play.
4. Select the `Foxglove` object and confirm the ROS2 Bridge status shows `Connected`, increasing `Sent Frames`, and zero dropped/failed frames.
5. Keep the sidecar terminal open.

The sample Manager uses bridge namespace `/unity2foxglove`, so publisher topics such as `/tf` become ROS2 Bridge topics such as `/unity2foxglove/tf`.

## 6. Verify ROS2 Topics

```bash
ros2 topic list | grep unity2foxglove
ros2 topic info /unity2foxglove/tf
ros2 topic info /unity2foxglove/scene
ros2 topic info /unity2foxglove/camera
ros2 topic info /unity2foxglove/point_cloud
ros2 topic echo --once /unity2foxglove/tf
ros2 topic echo --once /unity2foxglove/laser_scan
ros2 topic echo --once /unity2foxglove/point_cloud
ros2 topic hz /unity2foxglove/tf
```

Expected schema names are ROS2 schema names, for example `foxglove_msgs/msg/FrameTransform`. They are not protobuf names such as `foxglove.FrameTransform`.

RViz2-native `sensor_msgs`, `tf2_msgs`, and `visualization_msgs` compatibility is outside this sample and belongs to later compatibility work.

## 7. Foxglove Layout

Open Foxglove and import:

```text
FoxgloveRos2BridgeLayout.json
```

The layout references the direct Unity WebSocket topics (`/tf`, `/scene`, `/camera`, `/laser_scan`, `/point_cloud`) and Foxglove schema names. The `/unity2foxglove` namespace is only used by the ROS2 sidecar topics inspected with `ros2 topic ...`.

## 8. Troubleshooting

| Symptom | Action |
| --- | --- |
| Sidecar does not start | Source the ROS2 workspace and run `ros2 pkg prefix unity2foxglove_ros2_bridge`. |
| Health check says ROS2 CLI missing | Choose a Windows `ros2` executable only when you want Inspector CLI checks. A WSL sidecar can still be valid when the sidecar is connected and ROS2 topics echo in WSL. |
| Health check says sidecar is not running | Start the sidecar on `127.0.0.1:8767` and retry. |
| Topics appear but values decode incorrectly | Restart with `payload_format:=cdr-with-encapsulation`; record the result for your ROS2 distro and RMW. |
| Draco topic does not appear | Use raw `/unity2foxglove/point_cloud`; Draco is optional. |
| RViz2 does not show native markers | This sample publishes `foxglove_msgs`; native ROS2 visualization messages are covered by later compatibility work. |
