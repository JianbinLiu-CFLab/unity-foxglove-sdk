# ROS2 For Unity External Adapter

This sample imports a source-only adapter that connects the Unity2Foxglove ROS2 facade to an externally imported ROS2 For Unity runtime.

The sample does not bundle ROS2 For Unity, ros2cs, generated ROS2 message assemblies, native plugins, or ROS2 runtime binaries. Import RobotecAI ROS2 For Unity separately under:

```text
Assets/Ros2ForUnity
```

Then enable the scripting define:

```text
UNITY2FOXGLOVE_ROS2_FOR_UNITY
```

## Unity Setup

1. Install the `dev.unity2foxglove.ros2forunity` package.
2. Import the external ROS2 For Unity standalone runtime into `Assets/Ros2ForUnity`.
3. Import this package sample: `ROS2 For Unity External Adapter`.
4. Add the ROS2 For Unity string smoke component to a scene object.
5. Enter Play Mode.

The component creates a ROS2 For Unity backed context, publishes one `std_msgs/msg/String` each second, and subscribes to one inbound string topic.

## Windows ROS2 Jazzy Smoke

Use Windows ROS2 Jazzy as the local acceptance peer. WSL2 NAT is not a GREEN gate for this sample; it remains a separate DDS discovery investigation.

In PowerShell:

```powershell
& "C:\ros2_jazzy\ros2-windows\local_setup.ps1"
$env:ROS_DOMAIN_ID = "0"
$env:RMW_IMPLEMENTATION = "rmw_fastrtps_cpp"
$env:ROS_AUTOMATIC_DISCOVERY_RANGE = "SUBNET"
Remove-Item Env:ROS_LOCALHOST_ONLY -ErrorAction SilentlyContinue
Remove-Item Env:ROS_DISCOVERY_SERVER -ErrorAction SilentlyContinue
ros2 daemon stop
```

Receive from Unity:

```powershell
ros2 topic echo --once /unity2foxglove/ros2forunity/string/out std_msgs/msg/String
```

Expected:

```text
data: unity2foxglove string tick <counter>
```

Publish to Unity:

```powershell
ros2 topic pub --once /unity2foxglove/ros2forunity/string/in std_msgs/msg/String "{data: 'hello Unity2Foxglove'}"
```

Expected Unity Console log:

```text
[Ros2ForUnityStringSmoke] received: hello Unity2Foxglove
```

## Scope

This is a productization gate for one bidirectional `std_msgs/msg/String` topic pair. It is not a generic ROS2 message bridge.

Standard ROS2 visualization mapping, PointCloud2, MarkerArray, TF, clock, RViz2 acceptance, MCAP fanout, and rosbag2 work start after this external R2FU path is stable.
