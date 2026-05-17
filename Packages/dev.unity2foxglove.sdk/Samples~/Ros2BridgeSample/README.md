# ROS2 Bridge Sample

This sample demonstrates the optional Unity2Foxglove ROS2 Bridge path. Unity publishes normal Foxglove WebSocket data as usual and mirrors supported publisher payloads to a local ROS2 sidecar when the bridge is enabled.

## Run

1. Import **ROS2 Bridge Sample** from Package Manager.
2. In a ROS2 shell, build and source the sidecar workspace.
3. Start the sidecar:

```bash
ros2 launch unity2foxglove_ros2_bridge unity2foxglove_bridge.launch.py host:=127.0.0.1 port:=8767 payload_format:=cdr-with-encapsulation
```

4. Open `Scenes/Ros2BridgeSample.unity` and press Play.
5. In Unity, select the `Foxglove` object and confirm the ROS2 Bridge status shows `Connected`, increasing `Sent Frames`, and zero dropped/failed frames.
6. Verify topics:

```bash
ros2 topic list | grep unity2foxglove
ros2 topic echo --once /unity2foxglove/tf
ros2 topic echo --once /unity2foxglove/laser_scan
ros2 topic echo --once /unity2foxglove/point_cloud
ros2 topic hz /unity2foxglove/tf
```

## Expected Topics

Required bridge topics:

- `/unity2foxglove/tf` as `foxglove_msgs/msg/FrameTransform`
- `/unity2foxglove/scene` as `foxglove_msgs/msg/SceneUpdate`
- `/unity2foxglove/camera` as `foxglove_msgs/msg/CompressedImage`
- `/unity2foxglove/camera_calibration` as `foxglove_msgs/msg/CameraCalibration`
- `/unity2foxglove/laser_scan` as `foxglove_msgs/msg/LaserScan`
- `/unity2foxglove/point_cloud` as `foxglove_msgs/msg/PointCloud`

Optional topic:

- `/unity2foxglove/point_cloud_draco` as `foxglove_msgs/msg/CompressedPointCloud`

The Draco topic only publishes when the bundled native Draco plugin is available. When it is unavailable, the sample skips compressed point-cloud output instead of sending placeholder compressed bytes.

## Foxglove Layout

Import `FoxgloveRos2BridgeLayout.json` in Foxglove when viewing the Unity WebSocket connection. The layout uses the direct Unity topics such as `/tf`, `/camera`, and `/point_cloud`, with Foxglove schema names such as `foxglove.FrameTransform`.

The ROS2 Bridge namespace `/unity2foxglove` only applies to ROS2 sidecar topics. For example, Unity publishes `/tf` to Foxglove and mirrors it to ROS2 as `/unity2foxglove/tf`.

RViz2-native `sensor_msgs`, `tf2_msgs`, and `visualization_msgs` compatibility is outside this sample.

## Troubleshooting

| Symptom | First check |
| --- | --- |
| No ROS2 topics appear | Confirm the sidecar is listening on `127.0.0.1:8767`. The optional Manager Inspector **ROS2 Bridge Health** check can help diagnose ROS2 CLI or sidecar setup. |
| Topics appear but `echo` cannot decode | Restart the sidecar with `payload_format:=cdr-with-encapsulation`, then retry. |
| Draco topic missing | Run **Check Draco** in the point-cloud Inspector; raw point cloud should still publish. |
| Wrong Foxglove layout paths | Confirm the Foxglove layout uses direct topics such as `/tf`; the `/unity2foxglove` prefix is only for ROS2 sidecar topics. |
