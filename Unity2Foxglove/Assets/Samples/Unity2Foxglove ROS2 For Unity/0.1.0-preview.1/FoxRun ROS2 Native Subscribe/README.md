# FoxRun ROS2 Native Subscribe

This source-only sample demonstrates the existing ROS2 message input path. It lets a Linux or Windows ROS2 peer deliver compiled ROS2 messages directly to Unity through the selected ROS2 For Unity runtime. It is not a Foxglove Desktop feature and does not use Foxglove WebSocket publishing.

## Contract

Add the imported native subscribe sample component to a GameObject in a scene that has a `FoxgloveManager`. Configure the Manager with **Enable FoxRun Subscriptions** enabled and default subscription provider **ROS2 Native (R2FU)**. The component exposes these explicit `SubscribeOnly` contracts; its serialized topic fields retain source-defined defaults and can be inspected or changed in Unity:

| ROS2 type | Inspector topic field | QoS preset |
| --- | --- | --- |
| `std_msgs/msg/String` | String Topic | Reliable |
| `geometry_msgs/msg/Twist` | Twist Topic | Reliable |
| `sensor_msgs/msg/Joy` | Joy Topic | SensorData / BestEffort |
| `sensor_msgs/msg/Imu` | Imu Topic | SensorData / BestEffort |

The generated binding owns each latest typed message copy. The sample only copies bounded strings, scalar values, and the first few Joy elements into regular managed Inspector fields. Do not retain a message received from a ROS2 callback or treat it as valid beyond the generated binding's ownership path.

## Install and run

1. Install this adapter package and resolve exactly one of the Humble, Jazzy, or Lyrical Windows runtime packages in the Unity project manifest.
2. After a runtime package or Lyrical communication mode has been changed after native initialization, restart Unity before entering Play Mode. Runtime DLLs must not be mixed in one Editor process.
3. Set the Unity runtime communication mode before its first ROS2 initialization. FastDDS is DDS; Lyrical Zenoh uses `rmw_zenoh_cpp`. They do not discover one another.
4. Make the Linux peer match Unity's ROS distro, `RMW_IMPLEMENTATION`, `ROS_DOMAIN_ID`, discovery topology, and topic QoS. For example:

```bash
source /opt/ros/<humble|jazzy|lyrical>/setup.bash
export RMW_IMPLEMENTATION=rmw_fastrtps_cpp
export ROS_DOMAIN_ID=0
export ROS_AUTOMATIC_DISCOVERY_RANGE=SUBNET
```

Use a physical Linux host or bridged VM for FastDDS acceptance unless two-sided WSL2 discovery has already been proven. Zenoh needs a reachable, explicitly configured peer/router topology for all participants.

The current native-copy policy is intentionally bounded and latest-wins. `FoxRunRos2NativeCopyBudgetBytes` defaults to 4 MiB and is normalized to the portable 1 KiB–256 MiB range. If a copied message graph exceeds that budget, it is not applied and the subscription reports a bounded `CopyFailed` diagnostic; it never falls back to WebSocket. Small standard messages such as this sample's String, Twist, Joy, and Imu are in the supported scope; do not use this sample as a throughput benchmark for image, point-cloud, or arbitrary large payloads.

## Custom assemblies

If the component lives in a custom asmdef, add references to:

```text
Unity.FoxgloveSDK
Unity2Foxglove.Ros2ForUnity.Native
<the selected runtime and generated ROS message assemblies>
```

`FOXRUN043` (`Native generation requires the optional Native assembly reference`) means the source generator cannot see the optional Native binding assembly. Add the reference above, ensure the one selected runtime is resolved, and allow Unity to recompile. Do not work around the diagnostic by changing the contract to JSON or Protobuf.

## Boundaries

- Foxglove Desktop is unrelated to native ROS2 traffic. These topics do not appear in **FoxRun Publish** and do not require a Foxglove connection.
- ROS domain IDs isolate discovery; they are not authentication. Use the network and ROS2 security controls appropriate to the deployment.
- This sample supports existing compiled `.msg` types and native `SubscribeOnly` only.
- Arbitrary FoxRun DTO-to-custom-ROS2-message generation plus native Publish Data/bidirectional contracts are future work and are not available here.
