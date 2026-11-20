# SROS2 inbound publisher trust

This guide is an opt-in manual procedure for the ROS2 For Unity adapter. It restricts which authenticated ROS2 enclaves may publish to inbound topics consumed by Unity. The advertised product RMW paths are `rmw_fastrtps_cpp` for the packaged Humble, Jazzy, and Lyrical modes, plus `rmw_zenoh_cpp` for the advertised Lyrical Zenoh mode. Zenoh uses its own session and access-control configuration and is outside this DDS Security procedure.

The runtime archives contain Cyclone DDS libraries, but `rmw_cyclonedds_cpp` is not in the advertised supported-implementation lists. The Cyclone section below is a manual, unadvertised path for an operator who has independently verified that RMW in the selected ROS installation. It does not establish product support.

SROS2 authenticates and authorizes publishers. It does not replace the RMW sample-size controls described in Phase201. Fast DDS datagram settings and Zenoh link settings are not per-topic CDR sample limits. Cyclone DDS `Internal/MaxSampleSize` is process-wide and must be selected separately.

## 1. Create a keystore and enclaves

Run the commands from a ROS2 installation that contains the `sros2` CLI. The PowerShell and Bash blocks are equivalent. Keep the `ROS_DOMAIN_ID` value identical for permission generation, Unity, and every external publisher.

### PowerShell

```powershell
$evidenceRoot = Join-Path (Get-Location) 'build/phase201'
$keystore = Join-Path $evidenceRoot 'sros2/keystore'
$policyRoot = Join-Path $evidenceRoot 'sros2/policy'
$policyFile = Join-Path $policyRoot 'phase201.policy.xml'
$env:ROS_DOMAIN_ID = '0'
New-Item -ItemType Directory -Force $policyRoot | Out-Null
ros2 security create_keystore $keystore
ros2 security create_enclave $keystore /unity2foxglove/unity
ros2 security create_enclave $keystore /unity2foxglove/publisher
ros2 security create_enclave $keystore /unity2foxglove/untrusted
```

### Bash

```bash
evidence_root="$(pwd)/build/phase201"
keystore="$evidence_root/sros2/keystore"
policy_root="$evidence_root/sros2/policy"
policy_file="$policy_root/phase201.policy.xml"
export ROS_DOMAIN_ID=0
mkdir -p "$policy_root"
ros2 security create_keystore "$keystore"
ros2 security create_enclave "$keystore" /unity2foxglove/unity
ros2 security create_enclave "$keystore" /unity2foxglove/publisher
ros2 security create_enclave "$keystore" /unity2foxglove/untrusted
```

Keep the keystore `private` directory and CA private keys on the operator-controlled host. Each deployed process receives its own enclave directory, including that enclave's `key.pem`, through a protected channel. Copy only the required `public` material and the corresponding enclave files to each Unity or publisher machine. Never copy CA private keys.

## 2. Create a self-contained policy

Save the following as `$policyFile`. The `ns="/" node="*"` profiles deliberately match the dynamic root-namespace bridge names such as `u2f_imu_inbound_<instanceId>`, `u2f_pointcloud2_inbound_<instanceId>`, and `u2f_compressed_image_inbound_<instanceId>`. The wildcard is limited to the root namespace and to the listed topics. If a Unity scene uses a different namespace, replace `/` with that namespace and regenerate permissions.

The `publisher` and `untrusted` profiles also match the node created by `ros2 topic pub`. The untrusted profile has no publish rule for the inbound topics. Add every exact FoxRun topic used by the scene before generating permissions.

```xml
<?xml version="1.0" encoding="UTF-8"?>
<policy version="0.2.0">
  <enclaves>
    <enclave path="/unity2foxglove/unity">
      <profiles>
        <profile ns="/" node="*">
          <topics publish="ALLOW" subscribe="ALLOW">
            <topic>rosout</topic>
            <topic>/parameter_events</topic>
            <topic>/clock</topic>
            <topic>/points</topic>
            <topic>/camera/image/compressed</topic>
            <topic>/imu/data</topic>
            <!-- Add exact generated FoxRun topics here. -->
          </topics>
          <services request="ALLOW" reply="ALLOW">
            <service>~/describe_parameters</service>
            <service>~/get_parameter_types</service>
            <service>~/get_parameters</service>
            <service>~/list_parameters</service>
            <service>~/set_parameters</service>
            <service>~/set_parameters_atomically</service>
            <service>~/get_type_description</service>
          </services>
        </profile>
      </profiles>
    </enclave>

    <enclave path="/unity2foxglove/publisher">
      <profiles>
        <profile ns="/" node="*">
          <topics publish="ALLOW">
            <topic>rosout</topic>
            <topic>/parameter_events</topic>
            <topic>/points</topic>
            <topic>/camera/image/compressed</topic>
            <topic>/imu/data</topic>
            <!-- Add only the exact generated FoxRun topics this publisher may send. -->
          </topics>
          <services request="ALLOW" reply="ALLOW">
            <service>~/describe_parameters</service>
            <service>~/get_parameter_types</service>
            <service>~/get_parameters</service>
            <service>~/list_parameters</service>
            <service>~/set_parameters</service>
            <service>~/set_parameters_atomically</service>
            <service>~/get_type_description</service>
          </services>
        </profile>
      </profiles>
    </enclave>

    <enclave path="/unity2foxglove/untrusted">
      <profiles>
        <profile ns="/" node="*">
          <topics publish="ALLOW">
            <topic>rosout</topic>
            <topic>/parameter_events</topic>
          </topics>
          <services request="ALLOW" reply="ALLOW">
            <service>~/describe_parameters</service>
            <service>~/get_parameter_types</service>
            <service>~/get_parameters</service>
            <service>~/list_parameters</service>
            <service>~/set_parameters</service>
            <service>~/set_parameters_atomically</service>
            <service>~/get_type_description</service>
          </services>
        </profile>
      </profiles>
    </enclave>
  </enclaves>
</policy>
```

This template avoids the XInclude files used by the upstream `sample.policy.xml`. If you start from that upstream sample instead, copy its complete policy directory and preserve the relative `common/` and `talker_listener.policy.xml` paths. Copying only `sample.policy.xml` is insufficient.

Generate signed permissions after setting the domain ID. Run these commands from the directory containing `phase201.policy.xml`.

```powershell
$env:ROS_DOMAIN_ID = '0'
ros2 security create_permission $keystore /unity2foxglove/unity $policyFile
ros2 security create_permission $keystore /unity2foxglove/publisher $policyFile
ros2 security create_permission $keystore /unity2foxglove/untrusted $policyFile
```

```bash
export ROS_DOMAIN_ID=0
ros2 security create_permission "$keystore" /unity2foxglove/unity "$policy_file"
ros2 security create_permission "$keystore" /unity2foxglove/publisher "$policy_file"
ros2 security create_permission "$keystore" /unity2foxglove/untrusted "$policy_file"
```

Before starting a node, its enclave directory must contain `key.pem`, `cert.pem`, `permissions.p7s`, `governance.p7s`, `identity_ca.cert.pem`, and `permissions_ca.cert.pem`. An `Enforce` process must use the enclave that was signed from the matching profile.

## 3. Start Unity with security enabled

Set the environment before the first ROS2 For Unity native context is created. The Player must receive the same values in its process environment before launch. Open a separate process for every enclave. Do not leave the Unity override set when starting the publisher.

### Fast DDS product path

```powershell
$env:ROS_SECURITY_KEYSTORE = $keystore
$env:ROS_SECURITY_ENABLE = 'true'
$env:ROS_SECURITY_STRATEGY = 'Enforce'
$env:ROS_SECURITY_ENCLAVE_OVERRIDE = '/unity2foxglove/unity'
$env:ROS_DOMAIN_ID = '0'
$env:RMW_IMPLEMENTATION = 'rmw_fastrtps_cpp'
$env:FASTDDS_DEFAULT_PROFILES_FILE = 'C:\path\to\phase201-fastdds.xml'
```

Fast DDS 3.x uses `FASTDDS_DEFAULT_PROFILES_FILE`. Fast DDS 2.x uses `FASTRTPS_DEFAULT_PROFILES_FILE`. Select the variable that matches the runtime actually launched. The profile file changes Fast DDS transport/entity configuration. It does not create a per-reader sample-size admission limit.

### Manual Cyclone DDS path

`rmw_cyclonedds_cpp` is present in some archives but is not an advertised product selection. Use this branch only after independently verifying the selected ROS installation and runtime.

```powershell
$env:ROS_SECURITY_KEYSTORE = $keystore
$env:ROS_SECURITY_ENABLE = 'true'
$env:ROS_SECURITY_STRATEGY = 'Enforce'
$env:ROS_SECURITY_ENCLAVE_OVERRIDE = '/unity2foxglove/unity'
$env:ROS_DOMAIN_ID = '0'
$env:RMW_IMPLEMENTATION = 'rmw_cyclonedds_cpp'
$env:CYCLONEDDS_URI = 'file:///C:/path/to/cyclonedds-phase201.xml'
```

For Cyclone DDS, `Internal/MaxSampleSize` applies to the whole process, including discovery and every topic. The Unity bridge's 4 MiB PointCloud2 and CompressedImage paths must fit below that process-wide value.

### Bash equivalent

```bash
export ROS_SECURITY_KEYSTORE="$keystore"
export ROS_SECURITY_ENABLE=true
export ROS_SECURITY_STRATEGY=Enforce
export ROS_SECURITY_ENCLAVE_OVERRIDE=/unity2foxglove/unity
export ROS_DOMAIN_ID=0
export RMW_IMPLEMENTATION=rmw_fastrtps_cpp
export FASTDDS_DEFAULT_PROFILES_FILE="$HOME/path/to/phase201-fastdds.xml"
```

Start Unity from this process after selecting the RMW and enclave. The secured Unity participant must use the same `ROS_DOMAIN_ID` and RMW as the publisher.

## 4. Start a trusted publisher in a separate process

Use a new PowerShell or Bash process. Set the publisher enclave override before launching the CLI. The override and the explicit `--enclave` value are intentionally identical. An override left at `/unity2foxglove/unity` takes precedence over a different `--enclave` argument.

### PowerShell

```powershell
$env:ROS_SECURITY_KEYSTORE = $keystore
$env:ROS_SECURITY_ENABLE = 'true'
$env:ROS_SECURITY_STRATEGY = 'Enforce'
$env:ROS_SECURITY_ENCLAVE_OVERRIDE = '/unity2foxglove/publisher'
$env:ROS_DOMAIN_ID = '0'
$env:RMW_IMPLEMENTATION = 'rmw_fastrtps_cpp'
$env:FASTDDS_DEFAULT_PROFILES_FILE = 'C:\path\to\phase201-fastdds.xml'
ros2 topic pub /imu/data sensor_msgs/msg/Imu "{header: {frame_id: imu_link}}" --qos-reliability best_effort --qos-durability volatile --ros-args --enclave /unity2foxglove/publisher
```

### Bash

```bash
export ROS_SECURITY_KEYSTORE="$keystore"
export ROS_SECURITY_ENABLE=true
export ROS_SECURITY_STRATEGY=Enforce
export ROS_SECURITY_ENCLAVE_OVERRIDE=/unity2foxglove/publisher
export ROS_DOMAIN_ID=0
export RMW_IMPLEMENTATION=rmw_fastrtps_cpp
export FASTDDS_DEFAULT_PROFILES_FILE="$HOME/path/to/phase201-fastdds.xml"
ros2 topic pub /imu/data sensor_msgs/msg/Imu "{header: {frame_id: imu_link}}" --qos-reliability best_effort --qos-durability volatile --ros-args --enclave /unity2foxglove/publisher
```

Use the topic and message type that match the Unity bridge under test. For PointCloud2 and CompressedImage, use a publisher that can produce a known serialized payload size. The publisher permission must contain that topic before the publisher starts.

## 5. Verify the negative case

Use a separate process and switch the override before starting the negative publisher. The `untrusted` profile above has no publish permission for the inbound topics.

### PowerShell

```powershell
$env:ROS_SECURITY_KEYSTORE = $keystore
$env:ROS_SECURITY_ENABLE = 'true'
$env:ROS_SECURITY_STRATEGY = 'Enforce'
$env:ROS_SECURITY_ENCLAVE_OVERRIDE = '/unity2foxglove/untrusted'
$env:ROS_DOMAIN_ID = '0'
$env:RMW_IMPLEMENTATION = 'rmw_fastrtps_cpp'
$negative = ros2 topic pub --once /imu/data sensor_msgs/msg/Imu "{header: {frame_id: unauthorized}}" --qos-reliability best_effort --qos-durability volatile --ros-args --enclave /unity2foxglove/untrusted 2>&1
$negativeExitCode = $LASTEXITCODE
$negative
if ($negativeExitCode -eq 0) { Write-Host 'The CLI started. Accept the negative case only after secured graph inspection and Unity no-frame evidence show that no publish was authorized.' }
```

### Bash

```bash
export ROS_SECURITY_KEYSTORE="$keystore"
export ROS_SECURITY_ENABLE=true
export ROS_SECURITY_STRATEGY=Enforce
export ROS_SECURITY_ENCLAVE_OVERRIDE=/unity2foxglove/untrusted
export ROS_DOMAIN_ID=0
export RMW_IMPLEMENTATION=rmw_fastrtps_cpp
if ros2 topic pub --once /imu/data sensor_msgs/msg/Imu "{header: {frame_id: unauthorized}}" --qos-reliability best_effort --qos-durability volatile --ros-args --enclave /unity2foxglove/untrusted; then
  echo 'The CLI started. Accept the negative case only after secured graph inspection and Unity no-frame evidence show that no publish was authorized.'
else
  echo 'The CLI was rejected by the local security path. Continue with the Unity no-frame check.'
fi
```

The negative result must show no new inbound frame in Unity and no authorized publisher endpoint in secured graph inspection. A permissive launch, a missing enclave, or a CLI process without `--enclave` is not evidence of authorization. The CLI exit code alone is not the pass criterion because a process can start and fail to match an authorized endpoint.

For secured CLI inspection, use the matching enclave override in a separate process and disable the ROS daemon:

```powershell
$env:ROS_SECURITY_ENCLAVE_OVERRIDE = '/unity2foxglove/publisher'
ros2 topic list --no-daemon --spin-time 4
```

## 6. Failure modes and boundaries

- `ROS_SECURITY_ENABLE` defaults to disabled. `ROS_SECURITY_STRATEGY=Enforce` is required when missing or invalid security artifacts must fail closed.
- Every participant that communicates through a secured DDS domain needs compatible governance, identity, and permissions artifacts. A peer without them will not interoperate in enforce mode.
- The policy wildcard matches root-namespace node names only. It does not authorize topics outside the explicit lists. Change `ns` and the topic lists when the Unity scene uses a different namespace or generated contract.
- SROS2 authorization does not cap native allocations. Keep the Phase200 managed guards and use Cyclone `Internal/MaxSampleSize` only as an explicit process-wide deployment choice.
- Zenoh mode is outside DDS Security. Use `ZENOH_SESSION_CONFIG_URI` and Zenoh's own authentication/access-control configuration when operating `rmw_zenoh_cpp`; this guide does not claim a Zenoh security result.

References: [ROS 2 DDS-Security integration](https://github.com/ros2/design/blob/gh-pages/articles/180_ros2_dds_security.md), [ROS 2 access-control policy design](https://github.com/ros2/design/blob/gh-pages/articles/181_ros2_access_control_policies.md), [SROS2 Windows procedure](https://github.com/ros2/sros2/blob/rolling/SROS2_Windows.md), [Fast DDS 3.x migration](https://github.com/eProsima/Fast-DDS/blob/master/UPGRADING.md), and [Fast DDS environment variables](https://fast-dds.docs.eprosima.com/en/latest/fastdds/env_vars/env_vars.html).
