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

## 2. Preflight

In Unity:

1. Select the `Foxglove` object in `Ros2BridgeSample.unity`.
2. Open the Manager Inspector.
3. Run **ROS2 Bridge Health**.

The health check verifies the ROS2 CLI, `foxglove_msgs`, bundled interfaces, and the local sidecar health ping when live mode is available.

This preflight uses the Phase 97 ROS2 Bridge health diagnostics; if it is not Ready, fix that result before judging the sample scene.

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
4. Keep the sidecar terminal open.

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

The layout references `/unity2foxglove/...` topics and is intended for the ROS2 Bridge sample path.

## 8. Troubleshooting

| Symptom | Action |
| --- | --- |
| Sidecar does not start | Source the ROS2 workspace and run `ros2 pkg prefix unity2foxglove_ros2_bridge`. |
| Health check says ROS2 CLI missing | Choose the `ros2` executable in the Manager Inspector or run Unity from a shell with ROS2 on PATH. |
| Health check says sidecar is not running | Start the sidecar on `127.0.0.1:8767` and retry. |
| Topics appear but values decode incorrectly | Restart with `payload_format:=cdr-with-encapsulation`; record the result for your ROS2 distro and RMW. |
| Draco topic does not appear | Use raw `/unity2foxglove/point_cloud`; Draco is optional. |
| RViz2 does not show native markers | This sample publishes `foxglove_msgs`; native ROS2 visualization messages are covered by later compatibility work. |
