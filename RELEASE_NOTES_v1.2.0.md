# Unity2Foxglove v1.2.0 Release Notes

Release date: 2026-05-11

Unity2Foxglove v1.2.0 focuses on typed sensor publisher parity for the most common Foxglove robotics schemas after the camera protobuf work in v1.1.0.

## Highlights

- Added dedicated `FoxglovePointCloudPublisher`, `FoxgloveLaserScanPublisher`, and `FoxgloveCameraCalibrationPublisher` components.
- These publishers support both JSON and protobuf through the existing global/per-publisher encoding policy.
- `foxglove.PointCloud` now has complete JSON Schema metadata for packed field items, so Foxglove Desktop can parse JSON mode.
- PointCloud publishing supports the current demo child-transform path and a decoded-frame boundary for later Ouster/real-sensor integration.

## Compatibility Notes

- Existing Unity scenes keep serialized Inspector values unless changed manually.
- Protobuf remains the preferred default for new sensor publishers, with JSON available for compatibility and inspection.
- Ouster UDP/PCAP decoding is not included in this release; Phase 49 defines the typed publisher boundary that decoded point cloud frames can feed into.

## Verification

Validated before release:

```powershell
dotnet run --no-restore --project Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj
python Scripts/validate_release_package.py
```

Manual smoke:

- FullDemo test project entered Play Mode without compile errors.
- Foxglove Desktop parsed `/unity/point_cloud`, `/unity/laser_scan`, and `/unity/camera/calibration` in Protobuf mode.
- Foxglove Desktop parsed the same typed sensor topics after switching the Manager to JSON mode.
- PointCloud packed data was verified as little-endian IEEE754 `float32` values using `point_stride = 12` and fields `x`, `y`, `z`.
