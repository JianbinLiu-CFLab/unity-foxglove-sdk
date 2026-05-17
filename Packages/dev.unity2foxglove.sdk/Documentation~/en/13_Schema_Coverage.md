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

The runtime can advertise ROS 2 schema channels with `messageEncoding = cdr` and `schemaEncoding = ros2msg`, and MCAP records preserve the same schema/channel metadata.

This is schema parity only. The SDK does not yet include a CDR payload writer or ROS 2 CDR publisher output mode. Use existing JSON or protobuf publishers for working payloads unless your own integration supplies valid CDR bytes for the registered schema channel.

## 4. Generic Parity vs Dedicated Components

Generic protobuf support is the parity layer. It proves that all bundled official schemas can travel through the SDK's schema, publish, and MCAP paths.

Dedicated Unity components are the UX layer. They provide Inspector fields, lifecycle integration, and Unity-specific convenience for common workflows. Phase 44 does not add one custom `MonoBehaviour` for every schema.

Current dedicated or polished Unity paths include:

- `foxglove.FrameTransform`
- `foxglove.SceneUpdate`
- `foxglove.CompressedImage` through the camera publisher, with JSON and protobuf encoding support
- `foxglove.PointCloud` through `FoxglovePointCloudPublisher`, with JSON and protobuf encoding support
- `foxglove.LaserScan` through `FoxgloveLaserScanPublisher`, with JSON and protobuf encoding support
- `foxglove.CameraCalibration` through `FoxgloveCameraCalibrationPublisher`, with JSON and protobuf encoding support
- `foxglove.Log`

Other schemas can still be used through generic protobuf channels and generated protobuf message classes.

Publisher Encoding defaults to Protobuf for new `FoxgloveManager` components. Publishers that support both encodings, such as the camera publisher, use protobuf unless the Manager or component override selects JSON. JSON-only publishers fall back to JSON automatically.

For `foxglove.CompressedImage`, the JSON path stores JPEG data as base64 text because JSON has no binary field. The protobuf path stores the same JPEG payload as raw bytes in the official `bytes data` field, so it is the preferred path for camera streaming.

ROS 2 `.msg` catalog entries do not imply dedicated Unity CDR publisher components. Dedicated ROS 2 CDR output modes should be added only after payload serialization is implemented and validated.

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

## 6. Follow-Up Typed Publisher Candidates

Potential future dedicated publishers include:

- Odometry
- LocationFix
- RawImage
- RawAudio

These should be added when a real demo, dataset, or user workflow needs a polished Unity Inspector experience.
