# ROS2 For Unity External Adapter

This sample imports a source-only adapter that connects the Unity2Foxglove ROS2 facade to a ROS2 For Unity runtime.

Recommended setup:

```text
dev.unity2foxglove.ros2forunity
dev.unity2foxglove.ros2forunity.runtime.humble.win64
dev.unity2foxglove.ros2forunity.runtime.jazzy.win64
dev.unity2foxglove.ros2forunity.runtime.lyrical.win64
```

Keep candidate runtime packages under the repository root `Packages/` directory. In the Foxglove Manager Inspector, use the `ROS2 For Unity Runtime` active runtime dropdown to choose the single runtime package that should be resolved by `Unity2Foxglove/Packages/manifest.json`. Switching runtimes triggers a Unity package reimport and script compilation. If this Editor session already entered Play Mode with a different ROS2 runtime, restart Unity before entering Play Mode again; native ROS2 runtime DLLs stay in the Editor process until it exits.

Lyrical exposes an additional `Communication Mode` dropdown when its Zenoh payload is present. Use `FastDDS (default)` for the normal path, or select `Zenoh (rmw_zenoh_cpp)` before Play Mode when validating a Zenoh router topology.

When one runtime package is active, the adapter package enables the sample compile symbol automatically for the Standalone build target:

```text
UNITY2FOXGLOVE_ROS2_FOR_UNITY
```

For an external, non-package ROS2 For Unity import, add that symbol manually.

## Unity Setup

1. Install the `dev.unity2foxglove.ros2forunity` package.
2. Keep at least one runtime candidate package under repository-root `Packages/`, such as `dev.unity2foxglove.ros2forunity.runtime.humble.win64`, `dev.unity2foxglove.ros2forunity.runtime.jazzy.win64`, or `dev.unity2foxglove.ros2forunity.runtime.lyrical.win64`.
3. Import this package sample: `ROS2 For Unity External Adapter`.
4. Add the ROS2 For Unity string smoke component to a scene object.
5. Enter Play Mode.

The component creates a ROS2 For Unity backed context, publishes one `std_msgs/msg/String` each second, and subscribes to one inbound string topic.

## Windows ROS2 Jazzy Smoke

Use Windows ROS2 Jazzy as the local acceptance peer. WSL2 ROS2 peers are valid once Windows Firewall allow rules are configured for Fast DDS UDP ports (see report 20).

In PowerShell:

```powershell
$root = "ros2-windows\ros2_jazzy"
$pixi = "$root\.pixi\envs\default"
$env:Path = "$root\bin;$root\Scripts;$pixi;$pixi\Library\bin;$pixi\Scripts;C:\Windows\system32;C:\Windows;C:\Windows\System32\Wbem;C:\Windows\System32\WindowsPowerShell\v1.0"
$env:COLCON_PYTHON_EXECUTABLE = "$pixi\python.exe"
. "$root\local_setup.ps1"
$env:ROS_DOMAIN_ID = "0"
$env:RMW_IMPLEMENTATION = "rmw_fastrtps_cpp"
$env:ROS_AUTOMATIC_DISCOVERY_RANGE = "SUBNET"
Remove-Item Env:ROS_LOCALHOST_ONLY -ErrorAction SilentlyContinue
Remove-Item Env:ROS_DISCOVERY_SERVER -ErrorAction SilentlyContinue
```

Receive from Unity:

```powershell
& "$pixi\python.exe" "$root\Scripts\ros2-script.py" topic echo --once /unity2foxglove/ros2forunity/string/out std_msgs/msg/String --no-daemon --spin-time 15
```

Expected:

```text
data: unity2foxglove string tick <counter>
```

Publish to Unity:

```powershell
& "$pixi\python.exe" "$root\Scripts\ros2-script.py" topic pub --once /unity2foxglove/ros2forunity/string/in std_msgs/msg/String "{data: 'hello Unity2Foxglove'}"
```

Expected Unity Console log:

```text
[Ros2ForUnityStringSmoke] received: hello Unity2Foxglove
```

## Scope

This is a productization gate for one bidirectional `std_msgs/msg/String` topic pair. It is not a generic ROS2 message bridge.

Standard ROS2 visualization mapping, PointCloud2, MarkerArray, TF, clock, RViz2 acceptance, MCAP fanout, and rosbag2 work start after this external R2FU path is stable.
