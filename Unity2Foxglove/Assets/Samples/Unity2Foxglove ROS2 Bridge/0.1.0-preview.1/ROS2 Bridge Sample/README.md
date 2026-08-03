# ROS2 Bridge Sample

This sample demonstrates all three FoxRun directions through the optional
`unity2foxglove.ros2bridge` transport Provider:

- Unity publishes a changing `foxglove_msgs/msg/Log` value;
- an external ROS2 peer applies a `foxglove_msgs/msg/Log` value on Unity's
  main thread;
- a full-duplex value accepts remote A without an immediate causal echo, then
  publishes one distinct local B after the operator presses the on-screen
  button.

The scene also retains the ordinary transform, scene, camera, laser-scan, and
point-cloud publishers. The Bridge package depends only on
`dev.unity2foxglove.sdk`; it does not install or require R2FU.

## Before Play Mode

Unity does not install or start ROS2, `foxglove_msgs`, or the sidecar. In a
separate ROS2 shell, build the repository sidecar and source the resulting
workspace. Confirm these preflight commands succeed:

```bash
ros2 pkg prefix unity2foxglove_ros2_bridge
ros2 interface show foxglove_msgs/msg/Log
```

Start the sidecar explicitly on its Phase186 loopback-only endpoint:

```bash
ros2 launch unity2foxglove_ros2_bridge unity2foxglove_bridge.launch.py host:=127.0.0.1 port:=8767 payload_format:=cdr-with-encapsulation
```

The Phase186 sidecar accepts IPv4 loopback peers only. It is not a remote-host
gateway and does not provide TLS.

## Run the Sample

1. Import **ROS2 Bridge Sample** from Package Manager.
2. Open `Scenes/Ros2BridgeSample.unity`.
3. Select `Foxglove`. The one Manager configuration selects Bridge as the
   Publish destination and the single Subscribe source.
4. Start the sidecar, then enter Play Mode.
5. Observe `/ros2_bridge_sample/publish`:

   ```bash
   ros2 topic echo /ros2_bridge_sample/publish foxglove_msgs/msg/Log
   ```

6. Apply a subscribe-only value:

   ```bash
   ros2 topic pub --once /ros2_bridge_sample/subscribe foxglove_msgs/msg/Log "{level: 1, message: 'remote subscribe A', name: 'ros2-peer'}"
   ```

7. In another shell, observe the duplex topic, then publish remote A:

   ```bash
   ros2 topic echo /ros2_bridge_sample/duplex foxglove_msgs/msg/Log
   ros2 topic pub --once /ros2_bridge_sample/duplex foxglove_msgs/msg/Log "{level: 1, message: 'remote duplex A', name: 'ros2-peer'}"
   ```

   Unity displays A without immediately echoing A. Press **Publish distinct
   local duplex B** in the Game view; the ROS2 peer then receives one B.

The Manager Inspector reports observed Publish and Subscribe readiness. A TCP
connection alone is not Subscribe readiness; wait for the Provider to become
`Ready`, or inspect its bounded diagnostic code when it is `Degraded`,
`Reconnecting`, or `Failed`.

## Ordinary Publisher Topics

The scene also publishes these package-owned CDR mappings when their source
components are active:

- `/tf` as `foxglove_msgs/msg/FrameTransform`;
- `/scene` as `foxglove_msgs/msg/SceneUpdate`;
- `/camera` and `/camera_calibration`;
- `/laser_scan`;
- `/point_cloud`;
- optional `/point_cloud_draco` when the native Draco plugin is available.

Import `FoxgloveRos2BridgeLayout.json` only when viewing the independent
Foxglove WebSocket topics. Foxglove publish/subscribe encodings never alter
Bridge CDR.

## Troubleshooting

| Symptom | First check |
| --- | --- |
| Provider is `Failed` before connecting | Confirm the configured source is installed and the sidecar is listening on `127.0.0.1:8767`; there is no fallback Provider. |
| Connected but Subscribe is not `Ready` | Confirm the sidecar negotiated v2 `subscribe` and the exact `foxglove_msgs/msg/Log` type. |
| ROS2 cannot decode a topic | Use `payload_format:=cdr-with-encapsulation` and verify `foxglove_msgs` is sourced in the sidecar workspace. |
| Full duplex repeats A | Stop traffic and capture the sidecar/Manager origin counters; do not mask the loop with another relay. |
| Draco topic is missing | Use raw `/point_cloud`; compressed Draco output is optional. |

The dedicated repository sync check is:

```powershell
python Scripts/samples/sync_ros2_bridge_sample.py --dry-run
```
