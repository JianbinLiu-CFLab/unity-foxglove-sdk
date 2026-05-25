# ROS2 Standard Message Expansion Evidence

## Environment

```text
commit:
package version:
Unity version:
ROS2 distro:
RMW implementation:
ROS_DOMAIN_ID:
ROS_AUTOMATIC_DISCOVERY_RANGE:
ROS2 root:
ROS2 For Unity source:
runtime root:
runtime root is package:
asset runtime present:
```

## Unity Setup

```text
sample imported:
UNITY2FOXGLOVE_ROS2_FOR_UNITY active:
scene object:
enabled source count:
camera_info publish count:
image publish count:
imu publish count:
odometry publish count:
pose publish count:
navsatfix publish count:
last error:
```

## Helper Command

```text
python Scripts\smoke\phase132_standard_messages_acceptance.py --ros2-root C:\ros2_jazzy\ros2-windows
```

## Topic Evidence

```text
/camera/camera_info publisher:
/camera/image_raw publisher:
/imu/data publisher:
/odom publisher:
/pose publisher:
/fix publisher:
CameraInfo echo summary:
Image echo summary:
IMU echo summary:
Odometry echo summary:
PoseStamped echo summary:
NavSatFix echo summary:
```

## Known Limitations Checked

```text
R2FU default QoS caveat recorded:
No /tf published by this sample:
Conventional topic-name collision warning reviewed:
NavSatFix synthetic-constant note reviewed:
Optional RViz2 checks, if any:
```

## Verdict

```text
PASS / PASS WITH NOTED LIMITATIONS / BLOCKED / SKIPPED:
notes:
```
