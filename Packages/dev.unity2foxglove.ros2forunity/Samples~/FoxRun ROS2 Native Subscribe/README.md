# FoxRun ROS2 Native Subscribe

This source-only sample demonstrates the existing ROS2 message input path. It lets a Linux or Windows ROS2 peer deliver compiled ROS2 messages directly to Unity through the selected ROS2 For Unity runtime. It is not a Foxglove Desktop feature and does not use Foxglove WebSocket publishing.

## Contract

Add the imported native subscribe sample component to a GameObject in a scene that has a `FoxgloveManager`. Configure the Manager with **Enable FoxRun Subscriptions** enabled and default subscription provider **ROS2 Native (R2FU)**. The component exposes these explicit `Subscribe` contracts; its serialized topic fields retain source-defined defaults and can be inspected or changed in Unity:

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

## Four-row Linux interoperability acceptance

### Immediate local Lyrical/FastDDS smoke

For the normal Windows Unity interaction, start this command from the repository root (or run the same file directly from `Scripts/smoke/ros2/`), then click **Play** in the tracked Phase179 acceptance scene:

```powershell
python Scripts/smoke/ros2/phase179_lyrical_fastrtps_acceptance.py
```

It generates its own correlation token, reads only the fresh Editor log, uses the repo-local `ros2-windows/ros2_lyrical` CLI to discover and publish the fixed String, Twist, and Joy contracts, and ends with `PHASE179_LYRICAL_FASTRTPS_WINDOWS_LOCAL_EDITOR_PASS`. No Linux command, manual token, `--role`, or Foxglove connection is part of this local flow. It is deliberately labeled **Windows-local loopback**: it proves the selected Lyrical/FastDDS Unity runtime and the local Windows ROS2 peer, not a separate Linux host.

Use one named Phase179 entry point for the whole acceptance row; do not pass a hand-selected distro or RMW flag to a generic script. The four rows are fixed:

| Profile script | Unity runtime | Linux peer |
| --- | --- | --- |
| `phase179_humble_fastrtps_acceptance.py` | Humble / FastDDS | Humble / `rmw_fastrtps_cpp` |
| `phase179_jazzy_fastrtps_acceptance.py` | Jazzy / FastDDS | Jazzy / `rmw_fastrtps_cpp` |
| `phase179_lyrical_fastrtps_acceptance.py` | Lyrical / FastDDS | Lyrical / `rmw_fastrtps_cpp` |
| `phase179_lyrical_zenoh_acceptance.py` | Lyrical / Zenoh | Lyrical / `rmw_zenoh_cpp` |

All evidence is written under `build/phase179/<profile-id>/`. A Linux, Editor, or Player helper exits with `2` only for its documented, validated half-evidence state; for example the Linux helper reports `PEER_PUBLISH_COMPLETE_UNITY_PROOF_PENDING`. That is deliberately **not** a pass. Only the explicit `--role correlate` command emits a final matrix `PASS` and exits `0`.

### Editor surface: exact manual sequence

1. In Unity, resolve exactly the runtime shown by the selected row. In the `FoxgloveManager` Inspector enable **Enable FoxRun Subscriptions**, choose **ROS2 Native (R2FU)** as the default subscription provider, and use the imported **FoxRun ROS2 Native Subscribe** acceptance scene/component with its String, Twist, and Joy contracts enabled.
2. Before entering Play Mode, start the matching Windows Editor host. It snapshots the current Editor log offset, then waits for a *new* matching READY marker; do not reuse a stale marker or token.

   ```powershell
   $token = "phase179-humble-editor-001"
   python Scripts/smoke/ros2/phase179_humble_fastrtps_acceptance.py `
     --role windows-editor --surface editor --token $token `
     --unity-log "$env:LOCALAPPDATA\Unity\Editor\Editor.log"
   ```

   The host uses the repo-local `ros2-windows/ros2_<distro>` installation only for Windows CLI graph preflight. It does **not** add Windows ROS2 DLL paths to the Unity Editor; the selected R2FU runtime package remains the owner of Unity's native DLL selection.
3. Enter Play Mode. Wait for the host to report that the fresh READY marker and all three Unity subscription endpoints are visible. It then waits for the copied String, Twist, and Joy values after a fresh apply offset.
4. On the matching Linux machine (or a previously proven bridged/WSL topology), source the exact ROS distribution and set the selected RMW/domain/discovery values. Then run the same named script with the same token and `editor` surface:

   ```bash
   source /opt/ros/humble/setup.bash
   export RMW_IMPLEMENTATION=rmw_fastrtps_cpp
   export ROS_DOMAIN_ID=0
   export ROS_AUTOMATIC_DISCOVERY_RANGE=SUBNET
   python3 <repo>/Scripts/smoke/ros2/phase179_humble_fastrtps_acceptance.py \
     --role linux-peer --surface editor --token "$token"
   ```

5. After both helpers produced their summaries, run correlation from the workspace that can read both JSON files:

   ```powershell
   python Scripts/smoke/ros2/phase179_humble_fastrtps_acceptance.py `
     --role correlate --surface editor `
     --linux-summary-json build/phase179/humble-fastrtps/linux-editor.json `
     --windows-summary-json build/phase179/humble-fastrtps/windows-editor.json
   ```

The Player surface uses the same evidence contract. Build the Phase179 Player, start the matching script with `--role windows-player --player <Player.exe> --player-log <Player.log> --token <token>`, run Linux with `--role linux-peer --surface player`, then correlate `player` summaries. The Player helper never inherits `ros2-windows` PATH/DLL state; its R2FU runtime package selects the native runtime.

### Lyrical Zenoh topology

The Lyrical/Zenoh row requires an explicit, non-secret topology identity on every non-correlation role, for example `--zenoh-topology-id phase179-lyrical-zenoh-lab1`, and exactly one ownership choice:

- `--zenoh-router <router executable>` starts a helper-owned router, records only its mode/readiness, waits for the Phase162-compatible `Started` marker, and stops only that owned process tree on exit.
- `--zenoh-router <session.json5>` (or JSON/YAML) selects external-session mode and sets `ZENOH_SESSION_CONFIG_URI` for that helper without starting a router.
- `--no-zenoh-router` is valid only when an external certified Zenoh topology already exists; it never tries to stop an external router.

The Unity Editor or Player must itself be started with the matching Zenoh environment/session configuration **before its first `Ok()`**. The Windows acceptance helper cannot retrofit an already initialized Unity native runtime. Use the same opaque topology id in the Linux and Windows summaries; correlation rejects a mismatch. Zenoh and FastDDS are separate transports, so do not use one row to certify the other.

## Custom assemblies

If the component lives in a custom asmdef, add references to:

```text
Unity.FoxgloveSDK
Unity2Foxglove.Ros2ForUnity.Native
<the selected runtime and generated ROS message assemblies>
```

`FOXRUN212` (`Native generation requires the optional Native assembly reference`) means the source generator cannot see the optional Native binding assembly. Add the reference above, ensure the one selected runtime is resolved, and allow Unity to recompile. Do not work around the diagnostic by changing the contract to JSON or Protobuf.

## Boundaries

- Foxglove Desktop is unrelated to native ROS2 traffic. These topics do not appear in **FoxRun Publish** and do not require a Foxglove connection.
- ROS domain IDs isolate discovery; they are not authentication. Use the network and ROS2 security controls appropriate to the deployment.
- This sample supports existing compiled `.msg` types and native `Subscribe` only.
- Arbitrary FoxRun DTO-to-custom-ROS2-message generation plus native Publish Data/bidirectional contracts are future work and are not available here.
