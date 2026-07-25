# Unity2Foxglove ROS 2 Bridge Sidecar

This is the experimental ROS 2 sidecar used by the Phase 94 bridge spike and the Phase 95/96 Unity-side ROS2 Bridge productization path. It listens on loopback TCP, receives `U2R2` bridge frames from Unity2Foxglove, and republishes their CDR payload bytes through `rclcpp::GenericPublisher`.

It remains intentionally narrow:

- localhost only;
- publish-only;
- accepts only canonical ROS 2 message identities in exact
  `package/msg/Type` form;
- requires `rosidl_typesupport_cpp` for that exact type in the environment
  sourced before the sidecar starts;
- in the default `cdr-with-encapsulation` mode, forwards the received
  serialized XCDR1 bytes under the exact topic and type; there is no type,
  encoding, or destination fallback;
- QoS is resolved by Unity2Foxglove from the portable ROS 2 profile and
  reliability, durability, history, and depth policies;
- no automatic ROS 2 install;
- no Windows-native ROS 2 support;
- Phase 94 Gate B validates only `/unity/tf`, `/unity/laser_scan`, and `/unity/point_cloud`;
- Phase 95 adds Unity Inspector controls and background queue status, Phase 96 adds bridge topic namespace/override and QoS metadata, Phase 97 adds a lightweight `U2R2` `health_ping` / `health_pong` check, and Phase 98 adds a guided sample plus launch kit. The sidecar transport and ROS 2 environment are still manual.

ROS 2 publisher type and QoS are fixed when the sidecar creates a topic
publisher. Within one client session, a topic is reused only when its exact
type and every QoS field match. A conflicting contract is rejected without
changing the cached publisher. If typesupport lookup fails, no publisher is
cached and the frame fails closed. Restart this sidecar or use a different
effective bridge topic to change a topic contract.

The `U2R2` transport is a raw TCP frame stream. If the sidecar sees a malformed
binary envelope, it closes the client connection because it cannot safely
resynchronize to the next frame boundary. A well-formed `prepare_publisher`
request is different: invalid contracts, missing typesupport, and contract
conflicts receive a correlated `publisher_ready` error on the same connection,
so Unity can mark only that declaration unavailable. Unity should reconnect
after a transport-level rejection, and the sidecar log will identify it.

Normal Unity2Foxglove Foxglove WebSocket use does not require ROS.

## Prerequisites

Use Ubuntu or WSL with a supported ROS 2 workspace:

```bash
source /opt/ros/$ROS_DISTRO/setup.bash
sudo apt install ros-$ROS_DISTRO-foxglove-msgs
sudo apt install nlohmann-json3-dev
ros2 interface show foxglove_msgs/msg/FrameTransform
ros2 interface show foxglove_msgs/msg/LaserScan
ros2 interface show foxglove_msgs/msg/PointCloud
```

If `ros-$ROS_DISTRO-foxglove-msgs` is unavailable in your apt setup, add `foxglove_msgs` to a source workspace and build it there. Phase 94 does not include an automatic installer.

For a generated FoxRun custom interface, source the same Phase181 interface
workspace used by the ROS 2 peer before building and running the sidecar:

```bash
source /opt/ros/$ROS_DISTRO/setup.bash
source /path/to/phase181_workspace/install/setup.bash
ros2 interface show unity2foxglove_foxrun_interfaces_v1/msg/Phase181State48D288ED82F1Envelope
```

The sidecar does not substitute a Foxglove or standard ROS message when that
custom typesupport is absent.

When using WSL, prefer running both the sidecar and the `.NET` sender inside WSL. Do not bind the sidecar to `0.0.0.0` to work around Windows-to-WSL forwarding.

## Build

Place or symlink this package inside a ROS 2 workspace `src` directory, then run:

```bash
source /opt/ros/$ROS_DISTRO/setup.bash
colcon build --packages-select unity2foxglove_ros2_bridge
source install/setup.bash
```

## Run

Default mode forwards Unity's CDR payload, including the Phase 91/93 CDR encapsulation header, unchanged:

```bash
ros2 run unity2foxglove_ros2_bridge unity2foxglove_ros2_bridge --host 127.0.0.1 --port 8767 --payload-format cdr-with-encapsulation
```

Equivalent launch-file path for the package sample:

```bash
ros2 launch unity2foxglove_ros2_bridge unity2foxglove_bridge.launch.py host:=127.0.0.1 port:=8767 payload_format:=cdr-with-encapsulation
```

The launch file is installed by this package after `colcon build`.

The diagnostic body-only mode is only for test senders that produce CDR bytes without the four-byte CDR encapsulation header. In this mode, the sidecar prepends the little-endian encapsulation header before publishing to ROS 2:

```bash
ros2 run unity2foxglove_ros2_bridge unity2foxglove_ros2_bridge --host 127.0.0.1 --port 8767 --payload-format cdr-body-only
```

Do not use `cdr-body-only` for normal Unity2Foxglove payloads that already include `00 01 00 00`; the sidecar rejects those frames so malformed CDR is not published.

## Health Check

Unity2Foxglove can send a zero-payload `U2R2` `health_ping` to confirm that the process listening on the bridge port is this sidecar and speaks the expected protocol. The sidecar replies with `health_pong` and does not create or mutate ROS 2 publishers for health frames. Normal publish frames still require a non-empty CDR payload.

## Publisher Preparation

Phase184 uses the persistent publish connection for declaration-level
typesupport readiness. Before serializing or sending a declaration's first
sample, Unity sends a zero-payload `prepare_publisher` frame containing a
correlation `requestId`, application `protocolVersion` 1, exact topic,
canonical `package/msg/Type`, `cdr` encoding, and every resolved QoS field.
The sidecar creates or reuses the exact `rclcpp::GenericPublisher` and replies:

- `publisher_ready` with matching `requestId` and `status: "ok"` when the
  declaration is ready;
- `publisher_ready` with `status: "error"`, a stable `errorCode`, and a
  diagnostic message when it is rejected.

Preparation never publishes a ROS 2 sample. Publisher state is scoped to one
TCP client session. The original non-empty `publish` operation remains
compatible and can still lazily create a publisher for maintained legacy
senders.

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

## ROS2 Bridge Sample Launch Kit

The Phase 98 sample uses the `/unity2foxglove` bridge namespace and expects these product topics:

```text
/unity2foxglove/tf
/unity2foxglove/scene
/unity2foxglove/camera
/unity2foxglove/camera_calibration
/unity2foxglove/laser_scan
/unity2foxglove/point_cloud
/unity2foxglove/point_cloud_draco
```

Use the helper script to print preflight results and the exact launch command:

```bash
./scripts/run_bridge_sample.sh
./scripts/run_bridge_sample.sh --run
```

Run the helper scripts from this package folder after sourcing the ROS2 workspace; `ros2 run` is not required for the helpers.

PowerShell equivalent:

```powershell
.\scripts\run_bridge_sample.ps1
.\scripts\run_bridge_sample.ps1 -Run
```

The scripts check `ros2`, `foxglove_msgs`, the six required product sample schemas, and the optional Draco schema. They do not install packages, edit shell profiles, or mutate PATH.

Useful ROS2 checks for the sample:

```bash
ros2 topic list | grep unity2foxglove
ros2 topic info /unity2foxglove/tf --verbose
ros2 topic echo --once /unity2foxglove/tf
ros2 topic echo --once /unity2foxglove/laser_scan
ros2 topic echo --once /unity2foxglove/point_cloud
ros2 topic hz /unity2foxglove/tf
ros2 bag record /unity2foxglove/tf /unity2foxglove/laser_scan /unity2foxglove/point_cloud
```
