## 1. Purpose

Use this page to understand how Unity2Foxglove validates official Foxglove schema coverage, and where generic schema parity differs from dedicated Unity publisher UX.

Unity2Foxglove includes the bundled official Foxglove protobuf schema snapshot generated under `Runtime/Schemas/Proto`.

Unity2Foxglove also includes a generated official Foxglove ROS 2 `.msg` schema catalog under `Runtime/Schemas/Ros2Msg`.

## 2. Coverage Definition

In this package, full official schema coverage means every bundled `foxglove.*` protobuf message is:

- present in the explicit schema catalog;
- registered with protobuf descriptor bytes;
- sample-constructible in the runtime validation suite;
- publishable through a protobuf Foxglove channel;
- recordable to MCAP with protobuf schema and channel metadata.

The current bundled snapshot contains 46 official `foxglove.*` messages. Tests derive the expected count from the protobuf registry/descriptor metadata and require the explicit catalog to match it.

## 3. ROS 2 .msg Schema Coverage

The ROS 2 `.msg` catalog registers official Foxglove ROS 2 interface names such as `foxglove_msgs/msg/PointCloud` with `schemaEncoding = ros2msg`.

The bundled catalog is generated from the local `third-party/foxglove-sdk/schemas/ros2` snapshot. The current snapshot contains 41 root `.msg` files. Each generated entry embeds merged `.msg` text, including dependency sections such as `MSG: geometry_msgs/Pose` and `MSG: foxglove_msgs/PackedElementField` where needed.

The runtime can advertise ROS 2 schema channels with `messageEncoding = cdr` and `schemaEncoding = ros2msg`, and MCAP records preserve the same schema/channel metadata. The Inspector label for this path is `ROS2`.

The SDK includes a minimal XCDR1 little-endian writer for smoke payloads under `Runtime/Schemas/Ros2Msg/Cdr`. Payloads include the ROS 2 serialized-payload encapsulation header `00 01 00 00`. The validated smoke builders cover:

- `foxglove_msgs/msg/FrameTransform`
- `foxglove_msgs/msg/CompressedImage`
- `foxglove_msgs/msg/CameraCalibration`
- `foxglove_msgs/msg/LaserScan`
- `foxglove_msgs/msg/PointCloud`
- `foxglove_msgs/msg/CompressedPointCloud`
- `foxglove_msgs/msg/SceneUpdate`

The SDK also provides a productized `ROS2` publisher option for the validated Unity publisher workflows listed below. This is still a Foxglove WebSocket and MCAP path, not a ROS 2 node, DDS transport, or rosbag2 writer.

## 4. Generic Parity vs Dedicated Components

Generic protobuf support is the parity layer. It proves that all bundled official schemas can travel through the SDK's schema, publish, and MCAP paths.

Dedicated Unity components are the UX layer. They provide Inspector fields, lifecycle integration, and Unity-specific convenience for common workflows. Phase 44 does not add one custom `MonoBehaviour` for every schema.

Current dedicated or polished Unity paths include:

- `foxglove.FrameTransform` / `foxglove_msgs/msg/FrameTransform`
- `foxglove.SceneUpdate` / `foxglove_msgs/msg/SceneUpdate` for the built-in scene cube path
- `foxglove.CompressedImage` / `foxglove_msgs/msg/CompressedImage` through the JPEG camera publisher
- `foxglove.PointCloud` / `foxglove_msgs/msg/PointCloud` through `FoxglovePointCloudPublisher` raw mode
- `foxglove.CompressedPointCloud` / `foxglove_msgs/msg/CompressedPointCloud` through `FoxglovePointCloudPublisher` Draco mode
- `foxglove.LaserScan` / `foxglove_msgs/msg/LaserScan` through `FoxgloveLaserScanPublisher`
- `foxglove.CameraCalibration` / `foxglove_msgs/msg/CameraCalibration` through `FoxgloveCameraCalibrationPublisher`
- `foxglove.Log`

Other schemas can still be used through generic protobuf channels and generated protobuf message classes.

Publisher Encoding defaults to Protobuf for new `FoxgloveManager` components. Publishers that support multiple encodings use Protobuf unless the Manager or component override selects JSON or ROS2. JSON-only publishers fall back to JSON automatically, and publishers that do not support ROS2 fall back to their best supported encoding with an Inspector warning.

For `foxglove.CompressedImage`, the JSON path stores JPEG data as base64 text because JSON has no binary field. The protobuf path stores the same JPEG payload as raw bytes in the official `bytes data` field, so it is the preferred path for camera streaming.

ROS 2 `.msg` catalog entries beyond the dedicated list above are available for custom integrations through explicit `ros2msg` schema channels. Product publisher support should still be added schema by schema, with a matching Unity workflow and validation fixture.

## 5. Smoke MCAP

From the repository root:

```bash
python Scripts/smoke/phase44_all_schemas_mcap.py
```

The script writes:

```text
build/test_mcap/phase44_all_schemas_smoke.mcap
```

Open that file in Foxglove Desktop and check the Problems panel. The smoke file is intended to validate protobuf schema parsing and MCAP metadata, not perfect panel rendering for every schema.

To generate a ROS 2 `.msg` + CDR smoke MCAP:

```bash
python Scripts/smoke/ros2_cdr_mcap_inspect.py
```

The script writes:

```text
build/test_mcap/phase91_ros2_cdr_smoke.mcap
```

This fixture validates ROS 2 schema/channel metadata plus CDR payload framing for the seven smoke builders listed above.

To generate the productized ROS2 publisher smoke MCAP:

```bash
dotnet run --project Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj -- --phase92-ros2-product-mcap build/test_mcap/phase92_ros2_product_smoke.mcap
```

This fixture validates the seven user-facing publisher mappings from Inspector `ROS2` mode to `ros2msg` schemas and CDR payloads.

## 6. Follow-Up Typed Publisher Candidates

Potential future dedicated publishers include:

- Odometry
- LocationFix
- RawImage
- RawAudio

These should be added when a real demo, dataset, or user workflow needs a polished Unity Inspector experience.
