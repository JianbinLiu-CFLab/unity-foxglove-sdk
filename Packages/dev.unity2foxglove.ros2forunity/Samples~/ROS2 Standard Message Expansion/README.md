# ROS2 Standard Message Expansion

This source-only sample publishes six conventional ROS2 standard message topics through the optional ROS2 For Unity route:

```text
/camera/camera_info -> sensor_msgs/msg/CameraInfo
/camera/image_raw   -> sensor_msgs/msg/Image
/imu/data           -> sensor_msgs/msg/Imu
/odom               -> nav_msgs/msg/Odometry
/pose               -> geometry_msgs/msg/PoseStamped
/fix                -> sensor_msgs/msg/NavSatFix
```

The sample is a CLI smoke kit, not a new RViz2 productization gate. It does not add image rectification, calibration services, state estimation, Nav2, `/clock`, MCAP fanout, rosbag2, or any core SDK ROS2 dependency.

## Requirements

- A Unity project with `dev.unity2foxglove.ros2forunity`.
- A ROS2 For Unity runtime package or external ROS2 For Unity import.
- The compile symbol `UNITY2FOXGLOVE_ROS2_FOR_UNITY`.
- Windows ROS2 Jazzy at `C:\ros2_jazzy\ros2-windows` for the canonical helper.

## Unity Setup

1. In Package Manager, import the `ROS2 Standard Message Expansion` sample.
2. Add `UNITY2FOXGLOVE_ROS2_FOR_UNITY` if the runtime package did not add it automatically.
3. Add `Phase132StandardMessagesSmoke` to a scene object.
4. Keep the auto-added source components or add these explicitly:

```text
Phase132StandardCameraSource
Phase132StandardImuSource
Phase132StandardOdometrySource
Phase132StandardPoseSource
Phase132StandardNavSatFixSource
```

5. Enter Play Mode and wait for the component status to report ready and publish counts to increase.
6. Record runtime root, enabled source count, per-topic publish counts, and final helper output in the evidence template.

## Source Ownership

Each message family has an explicit source component. The sample does not infer IMU, Odometry, or NavSatFix from arbitrary Unity transforms. `Phase132StandardNavSatFixSource` publishes explicit synthetic constant WGS84 coordinates for smoke testing; it is not real geolocation and does not convert Unity world coordinates to latitude/longitude.

The raw image source generates a tiny deterministic `rgb8` image so echo output remains bounded:

```text
width = 32
height = 24
step = width * 3
data.Length = height * step
```

## QoS And TF Boundaries

This sample uses R2FU default QoS. It does not claim ROS2 `sensor_data` QoS profile parity. Common best-effort sensor subscribers such as RViz2 Image displays, image_view or rqt_image_view-style tools, and driver-oriented pipelines may not match reliable default publishers until later QoS-tuning work.

This sample does not publish `/tf`. Optional RViz2 checks must either set Fixed Frame to the message frame under inspection or run alongside an external TF tree. RViz2 can report "No transform" for frames such as `camera_optical_frame`, `odom`, `base_link`, and `gps_link` when no TF owner is present.

The included `rviz2_phase132_standard_messages.rviz` is a lightweight visual helper, not the pass/fail gate. It opens Grid, PoseStamped `/pose`, and Image `/camera/image_raw` displays with reliable QoS so the helper can bring up RViz2 consistently before the CLI echo checks.

The default topic names are ROS conventions and can collide with Nav2, real drivers, or other samples:

```text
/odom
/pose
/fix
/imu/data
/camera/image_raw
/camera/camera_info
```

Production projects should namespace them, for example `/unity/odom` or `/unity/camera/image_raw`.

## Windows Acceptance Helper

Run Unity first, then execute this canonical command from the repository root:

```text
python Scripts\smoke\phase132_standard_messages_acceptance.py --ros2-root C:\ros2_jazzy\ros2-windows
```

The helper uses:

```text
<ros2-root>\.pixi\envs\default\python.exe <ros2-root>\Scripts\ros2-script.py
```

It uses `ROS_AUTOMATIC_DISCOVERY_RANGE=LOCALHOST` for same-machine Unity acceptance, collects one message from all six topics as the pass/fail gate, and launches RViz2 by default afterward as the visual helper. Add `--no-launch-rviz` for CLI-only diagnostics. Add `--graph-diagnostics` when you need slower `ros2 node/topic` graph probes. Add `--rmw rmw_cyclonedds_cpp` if your Unity/R2FU runtime is using Cyclone DDS instead of the default Fast DDS setting. Use `--domain-id` or another `--discovery-range` only when matching a different Unity runtime environment.

To launch only the visual helper without the six-topic CLI checks:

```text
python Scripts\smoke\launch_phase132_rviz2.py
```

## Secondary Diagnostics

Use bare ROS2 commands only after the ROS2 environment is already known-good. On Windows, keep the helper path above as the primary acceptance route.

```text
ros2 topic info /camera/camera_info
ros2 topic info /camera/image_raw
ros2 topic info /imu/data
ros2 topic info /odom
ros2 topic info /pose
ros2 topic info /fix
ros2 topic echo --once /camera/camera_info
ros2 topic echo --once /camera/image_raw
ros2 topic echo --once /imu/data
ros2 topic echo --once /odom
ros2 topic echo --once /pose
ros2 topic echo --once /fix
```

## PASS Criteria

- `/camera/camera_info`, `/camera/image_raw`, `/imu/data`, `/odom`, `/pose`, and `/fix` each have a publisher endpoint from `unity2foxglove_phase132_standard_messages`.
- Echo checks decode representative messages and validate non-zero defaults such as CameraInfo focal terms, IMU gravity/orientation, Odometry orientation, and NavSatFix coordinates.
- Image echo is not truncated and contains the expected `32x24 rgb8` payload.
- Evidence records commit hash, package version, Unity version, ROS2 distro, RMW implementation, runtime root, helper output, limitations, and final verdict.
