# ROS2 Bridge Provider and Sample

`dev.unity2foxglove.ros2bridge` is the optional localhost sidecar transport
Provider. It depends directly on `dev.unity2foxglove.sdk` and has no package or
assembly dependency on `dev.unity2foxglove.ros2forunity`.

## Installation combinations

| Goal | Unity packages |
| --- | --- |
| Foxglove WebSocket, MCAP, replay, core FoxRun | `dev.unity2foxglove.sdk` |
| Sidecar ROS2 publish and subscribe | SDK + `dev.unity2foxglove.ros2bridge` |
| Direct in-process ROS2 | SDK + `dev.unity2foxglove.ros2forunity` + one matching runtime |
| Independent Bridge and native fanout | SDK + Bridge + R2FU + one matching R2FU runtime |

Bridge never needs an R2FU runtime or custom-typesupport binary inside Unity.
R2FU never needs the Bridge package. Installing both adds two independent
Providers; it does not merge their lifecycle or wire representation.

## One Manager, stable Provider IDs

The one `FoxgloveManager` Inspector owns routing:

- Publish selects zero or more stable Provider IDs;
- Subscribe selects exactly one Provider when subscriptions are enabled;
- a configured unavailable Provider fails closed with no fallback;
- uninstalled and unconfigured Providers are silent.

The stable IDs are:

```text
foxglove.websocket
unity2foxglove.r2fu
unity2foxglove.ros2bridge
```

Foxglove Publish Encoding and Subscribe Encoding are independent and apply
only to `foxglove.websocket`. Bridge always uses Provider-owned ROS type
mapping plus XCDR1 little-endian CDR. R2FU uses its own typed ROS path. Neither
ROS Provider consumes `FoxRunEncoding`.

## What the sample proves

The imported **ROS2 Bridge Sample** keeps ordinary sensor publishers and adds
three `foxglove_msgs/msg/Log` FoxRun contracts:

| Direction | Topic | Expected observation |
| --- | --- | --- |
| Publish | `/ros2_bridge_sample/publish` | ROS2 receives changing Unity values |
| Subscribe | `/ros2_bridge_sample/subscribe` | Unity displays remote A on the main thread |
| `PublishAndSubscribe` | `/ros2_bridge_sample/duplex` | Remote A is not immediately echoed; a later local B publishes once |

The sample scene is saved by `Ros2BridgeSampleSceneBuilder`, not handwritten
YAML. `Scripts/samples/sync_ros2_bridge_sample.py --dry-run` proves the package
sample and checked-in demo import are byte-identical, including Unity meta
files.

## Sidecar preflight and launch

Unity does not install, launch, restart, or update the sidecar. Build and
source it in a separate ROS2 shell:

```bash
source /opt/ros/$ROS_DISTRO/setup.bash
mkdir -p ~/u2f_ros2_ws/src
cd ~/u2f_ros2_ws/src
ln -s <repo>/Tools/ros2_bridge/unity2foxglove_ros2_bridge unity2foxglove_ros2_bridge
cd ~/u2f_ros2_ws
colcon build --packages-select unity2foxglove_ros2_bridge --symlink-install
source install/setup.bash
ros2 pkg prefix unity2foxglove_ros2_bridge
ros2 interface show foxglove_msgs/msg/Log
```

Then launch it explicitly:

```bash
ros2 launch unity2foxglove_ros2_bridge unity2foxglove_bridge.launch.py host:=127.0.0.1 port:=8767 payload_format:=cdr-with-encapsulation
```

Phase186 is IPv4-loopback-only (`127.0.0.1`). The sidecar validates the
accepted peer family and address. Arbitrary remote hosts, wildcard/LAN/public
peers, TLS, ROS services, ROS actions, and ROS parameters are outside this
product boundary.

## Protocol, CDR, and bounds

The package and C++ sidecar consume the same U2R2 v1/v2 byte authority. v1
publish and health compatibility remains frozen. v2 adds correlated publisher
and subscription operations, one data-session lease, bounded concurrent
health/provenance probes, fair per-contract data queues, reserved control
capacity, replay high-water marks, removal tombstones, and immutable reconnect
lease snapshots.

Inbound messages require exact `encoding: "cdr"`, representation
`xcdr1-le`, and the `00 01 00 00` encapsulation prefix. Official Foxglove ROS2
messages use generated registries; supported FoxRun DTOs use the Bridge
physical emitter and an exact generated ROS interface identity. Unsupported
types or representations are rejected rather than reinterpreted.

All header, payload, queue, contract, replay, tombstone, transient/in-flight,
handshake, read/write, join, and shutdown limits are immutable and listed in
[U2R2_PROTOCOL.md](U2R2_PROTOCOL.md).

## Lifecycle and diagnostics

The hidden serialized `Ros2BridgeTransportProvider` companion is created only
after explicit setup/selection. The Manager freezes selection into the next
session; Play Mode edits do not mutate the active session.

Observed neutral states are `Starting`, `Ready`, `Degraded`, `Reconnecting`,
`Failed`, and `Stopped`. Publish and Subscribe readiness are separate. A
connected socket cannot manufacture Subscribe readiness. Stable bounded
diagnostics expose contract/queue/resource counters and retired worker exits;
configuration alone is never reported as runtime success.

Shutdown stops admission, cancels reconnect/read work, closes the socket,
invalidates the generation, and joins workers while worker-reachable resources
remain alive. A delayed worker moves into the bounded retirement owner rather
than having its resources disposed underneath it.

## Provenance boundary

U2R2 and its implementation are original. The project reviewed architectural
ideas from Unity-Technologies/ROS-TCP-Connector; no implementation code or
comments were copied. The exact inspected revision, files, license blob, and
`materialCopied: false` result are frozen in
`Tools/ros2_bridge/unity2foxglove_ros2_bridge/PROVENANCE.json`. No additional
third-party notice is claimed for material that was not reused.

See [PHASE186_BREAKING_UPGRADE.md](PHASE186_BREAKING_UPGRADE.md) before opening
pre-Phase186 scenes or rebuilding scripts.
