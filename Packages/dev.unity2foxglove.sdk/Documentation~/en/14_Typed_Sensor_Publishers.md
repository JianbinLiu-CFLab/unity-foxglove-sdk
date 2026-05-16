## 1. Purpose

Use this page when you want to publish typed sensor-style Foxglove messages from Unity without writing the low-level schema and byte-packing code yourself.

Phase 49 adds dedicated Unity components for:

- `foxglove.PointCloud`
- `foxglove.LaserScan`
- `foxglove.CameraCalibration`

Each raw typed sensor component supports JSON and protobuf. Protobuf is preferred for high-rate sensor streams because binary arrays are sent as protobuf bytes or repeated numeric fields instead of JSON text.

## 2. Encoding Behavior

The components follow the same publisher encoding policy as the rest of the package:

- If `FoxgloveManager` uses `Protobuf`, these publishers advertise protobuf schemas and publish protobuf payloads.
- If `FoxgloveManager` uses `Json`, they advertise JSON schemas and publish JSON payloads.
- If per-publisher overrides are enabled, each component can override the manager default.

This means you can switch raw sensor publishing between JSON and protobuf from the Inspector without changing scene scripts. Optional compressed modes may be protobuf-only when their Foxglove schema requires binary payloads.

## 3. Point Cloud Publisher

Add `FoxglovePointCloudPublisher` to a GameObject when you need a Unity-side point-cloud topic.

`Point Cloud Output Mode` selects the wire schema:

| Mode | Default topic | Schema | Encoding | Dependency |
|---|---|---|---|---|
| `Raw` | `/unity/point_cloud` | `foxglove.PointCloud` | JSON or protobuf | none |
| `Draco` | `/unity/point_cloud_draco` | `foxglove.CompressedPointCloud` | protobuf only | bundled Windows native plugin |

Raw mode is the default and dependency-free path.

Raw default topic:

```text
/unity/point_cloud
```

Draco default topic:

```text
/unity/point_cloud_draco
```

The component can build a simple point cloud from assigned transforms or from child transforms. This is useful for quick visualization smoke tests.

For real sensors, feed decoded points into the programmatic API:

```csharp
var frame = new PointCloudFrame
{
    FrameId = "os_sensor",
};

frame.Points.Add(new PointCloudPoint
{
    X = 1.0f,
    Y = 0.0f,
    Z = 0.5f,
    Intensity = 42.0f,
    Reflectivity = 0.8f,
    Ring = 3,
    TimeOffset = 0.00012f,
});

pointCloudPublisher.SetFrame(frame);
```

In Draco mode, the same sampled `PointCloudFrame` is encoded by the bundled Windows native plugin `Unity2FoxgloveDracoNative.dll`. The publisher emits `foxglove.CompressedPointCloud` with format = `draco`. If the native plugin is missing or incompatible, Draco mode logs a warning and publishes nothing until the plugin is restored or the component is switched back to raw mode.

Phase 89 keeps native Draco encode synchronous in the Unity publish/update path. Large frames can block publishing while they encode. Use QoS budgets, test with `Check Draco`, and keep raw mode available for dependency-free or unsupported-platform point clouds.

### 3.1 Field Layout

The minimal point layout is:

| Field | Type | Offset |
|---|---|---:|
| `x` | `float32` | 0 |
| `y` | `float32` | 4 |
| `z` | `float32` | 8 |

When optional Ouster-style fields are present, the layout becomes:

| Field | Type | Offset |
|---|---|---:|
| `x` | `float32` | 0 |
| `y` | `float32` | 4 |
| `z` | `float32` | 8 |
| `intensity` | `float32` | 12 |
| `reflectivity` | `float32` | 16 |
| `ring` | `uint16` | 20 |
| `time_offset` | `float32` | 22 |

This is an Ouster-ready boundary after packet decoding. Phase 49 does not decode Ouster UDP packets, PCAP files, or ROS messages directly.

## 4. Laser Scan Publisher

Add `FoxgloveLaserScanPublisher` when you need a 2D range scan topic.

Default topic:

```text
/unity/laser_scan
```

The Inspector exposes:

- frame id;
- start and end angle in degrees;
- range values;
- optional intensity values;
- a synthetic sample count for smoke testing when no range array is provided.

The publisher converts degrees to radians before publishing because `foxglove.LaserScan` uses radians.

If intensities are provided, their count must match the range count. A mismatch is treated as invalid input and the frame is skipped with a warning.

## 5. Camera Calibration Publisher

Add `FoxgloveCameraCalibrationPublisher` when camera intrinsics should be published alongside image data.

Default topic:

```text
/unity/camera/calibration
```

The component can derive width, height, focal length, and principal point from a Unity `Camera`. You can also provide non-zero override values in the Inspector.

The default distortion model is:

```text
plumb_bob
```

Distortion coefficients default to an empty array. If you need calibrated physical camera distortion, provide those values from your calibration workflow in a custom integration script.

## 6. Foxglove Desktop Smoke Test

1. Add one or more typed sensor publishers to the scene.
2. Set `FoxgloveManager` `Publisher Encoding` to `Protobuf`.
3. Enter Play Mode.
4. Connect Foxglove Desktop to:

```text
ws://127.0.0.1:8765
```

5. Check the Topics panel for:

```text
/unity/point_cloud
/unity/laser_scan
/unity/camera/calibration
```

6. Check the Problems panel. There should be no protobuf schema parsing errors and no unsupported encoding warnings.

7. Switch `Publisher Encoding` back to `Json` and confirm the same topics still advertise and publish.

## 7. Relationship to Real Ouster Workflows

This phase is the Unity-to-Foxglove typed publisher layer. It is the right place to send decoded Ouster-style point data after another component has parsed packets or generated simulated lidar returns.

For a real-time Ouster workflow, the usual architecture is:

1. Decode Ouster UDP packets, PCAP frames, or simulated rays into points.
2. Convert the points into `PointCloudFrame`.
3. Include optional Ouster-style fields such as `ring`, `intensity`, `reflectivity`, and `time_offset` when available.
4. Let `FoxglovePointCloudPublisher` publish the result as `foxglove.PointCloud`.

Packet decoding and Ouster sensor simulation are intentionally separate from this component so the publisher stays focused on official Foxglove schema parity.
