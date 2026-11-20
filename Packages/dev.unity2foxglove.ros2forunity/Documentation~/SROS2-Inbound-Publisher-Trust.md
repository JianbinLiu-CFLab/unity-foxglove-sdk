# SROS2 inbound publisher trust

This guide is an opt-in deployment procedure for the ROS2 For Unity adapter. It restricts which authenticated ROS2 enclaves may publish to the inbound topics consumed by Unity. It applies to DDS RMWs such as `rmw_fastrtps_cpp` and `rmw_cyclonedds_cpp`. `rmw_zenoh_cpp` uses Zenoh session/router configuration instead of DDS Security, so this guide does not claim SROS2 enforcement for the Lyrical Zenoh mode.

SROS2 authorizes a publisher. It does not replace the RMW sample-size controls described in Phase201. Fast DDS datagram settings and Zenoh link settings are not per-topic CDR sample limits. Cyclone DDS `Internal/MaxSampleSize` is process-wide and must be selected separately.

## 1. Create a keystore and enclaves

Run these commands from a ROS2 installation that contains the `sros2` CLI. The Windows commands use PowerShell syntax. The same commands work from a POSIX shell after replacing the path.

```powershell
$evidenceRoot = Join-Path (Get-Location) 'build/phase201'
$keystore = Join-Path $evidenceRoot 'sros2/keystore'
New-Item -ItemType Directory -Force (Split-Path $keystore) | Out-Null
ros2 security create_keystore $keystore
ros2 security create_enclave $keystore /unity2foxglove/unity
ros2 security create_enclave $keystore /unity2foxglove/publisher
```

Keep the keystore `private` directory and CA private keys on the operator-controlled host. Each deployed process must receive its own enclave directory, including that enclave's `key.pem`, through a protected channel. Copy only the required `public` material and the corresponding enclave files to each Unity or publisher machine. Never copy the CA private keys.

## 2. Restrict the publisher permissions

Use a policy file for the two enclaves. The Unity enclave needs `subscribe` access to the inbound topics. Each trusted publisher enclave needs `publish` access to the topics it is allowed to send. Do not grant a wildcard publisher rule when a fixed topic list is sufficient.

The direct bridge defaults are:

```text
/points                     sensor_msgs/msg/PointCloud2
/camera/image/compressed    sensor_msgs/msg/CompressedImage
/imu/data                   sensor_msgs/msg/Imu
```

FoxRun generated subscriptions use the topic strings in their `[FoxRun(...)]` attributes. Add those exact topics to the policy, including any Phase181 custom interface topics used by the scene.

The policy format is the SROS2 `policy` XML format. Start from the maintained [SROS2 sample policy](https://github.com/ros2/sros2/blob/rolling/sros2/test/policies/sample.policy.xml), add an enclave profile for `/unity2foxglove/unity` with `subscribe` rules, and add one profile per trusted publisher enclave with only the required `publish` rules. Then create signed permissions for each enclave:

```powershell
ros2 security create_permission $keystore /unity2foxglove/unity .\phase201.policy.xml
ros2 security create_permission $keystore /unity2foxglove/publisher .\phase201.policy.xml
```

The generated enclave directory must contain `key.pem`, `cert.pem`, `permissions.p7s`, `governance.p7s`, `identity_ca.cert.pem`, and `permissions_ca.cert.pem` before starting a node.

## 3. Start Unity with security enabled

Set the environment before the first ROS2 For Unity native context is created. The Editor runtime-selection lease writes `ROS_DISTRO` and `RMW_IMPLEMENTATION` before native startup. The Player must receive the same values in its process environment before launch.

```powershell
$env:ROS_SECURITY_KEYSTORE = $keystore
$env:ROS_SECURITY_ENABLE = 'true'
$env:ROS_SECURITY_STRATEGY = 'Enforce'
$env:ROS_SECURITY_ENCLAVE_OVERRIDE = '/unity2foxglove/unity'
$env:RMW_IMPLEMENTATION = 'rmw_fastrtps_cpp'
```

For Cyclone DDS, select a user-owned XML file before Unity starts:

```powershell
$env:CYCLONEDDS_URI = 'file:///absolute/path/cyclonedds-phase201.xml'
```

If the XML contains an opt-in `Internal/MaxSampleSize`, remember that the value applies to the whole Cyclone process, including discovery and every topic. The Unity bridge's 4 MiB PointCloud2 and CompressedImage paths must fit below that process-wide value.

For Fast DDS, a user-owned profile can be selected with:

```powershell
$env:FASTRTPS_DEFAULT_PROFILES_FILE = 'C:\path\to\phase201-fastdds.xml'
```

This selects Fast DDS transport/entity profiles. It does not create a per-reader sample-size admission limit.

## 4. Start a trusted publisher

Use the publisher enclave path when launching the external ROS2 node. The node must use the same ROS domain and the same DDS vendor as Unity.

```powershell
$env:ROS_SECURITY_KEYSTORE = $keystore
$env:ROS_SECURITY_ENABLE = 'true'
$env:ROS_SECURITY_STRATEGY = 'Enforce'
ros2 topic pub /imu/data sensor_msgs/msg/Imu "{header: {frame_id: imu_link}}" --qos-reliability best_effort --qos-durability volatile --ros-args --enclave /unity2foxglove/publisher
```

Use the topic and message type that match the Unity bridge under test. For PointCloud2 and CompressedImage, use a publisher that can produce a known serialized payload size. The permission file must contain that topic before the publisher is started.

## 5. Verify the negative case

Create a second enclave without a `publish` rule for the inbound topic. Start the same publisher with that enclave and `ROS_SECURITY_STRATEGY=Enforce`. The publisher must fail to match or publish, and Unity must not record a new inbound frame. A permissive launch, a missing enclave, or a CLI process without `--enclave` is not evidence of authorization.

For secured CLI inspection, use an enclave override and disable the ROS daemon:

```powershell
$env:ROS_SECURITY_ENCLAVE_OVERRIDE = '/unity2foxglove/publisher'
ros2 topic list --no-daemon --spin-time 4
```

## 6. Failure modes and boundaries

- `ROS_SECURITY_ENABLE` defaults to disabled. `ROS_SECURITY_STRATEGY=Enforce` is required when missing or invalid security artifacts must fail closed.
- Every participant that communicates through a secured DDS domain needs compatible governance, identity, and permissions artifacts. A peer without them will not interoperate in enforce mode.
- SROS2 authorization does not cap native allocations. Keep the Phase200 managed guards and use Cyclone `Internal/MaxSampleSize` only as an explicit process-wide deployment choice.
- Zenoh mode is outside DDS Security. Use `ZENOH_SESSION_CONFIG_URI` and Zenoh's own authentication/access-control configuration when operating `rmw_zenoh_cpp`; this guide does not claim a Zenoh security result.

References: [ROS 2 security environment variables](https://github.com/ros2/ros2_documentation/blob/rolling/source/Developer-Tools/Introspection-and-analysis/Security/About-Security.rst), [SROS2 Windows procedure](https://github.com/ros2/sros2/blob/rolling/SROS2_Windows.md), and [ROS 2 access-control policy design](https://github.com/ros2/design/blob/gh-pages/articles/181_ros2_access_control_policies.md).
