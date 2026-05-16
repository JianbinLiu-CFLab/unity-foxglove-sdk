# Unity2Foxglove v1.7.0 Release Notes

Release date: 2026-05-17

Unity2Foxglove v1.7.0 productizes Draco-compressed point-cloud output. The raw `foxglove.PointCloud` path remains the default, while point-cloud publishers can now opt into `foxglove.CompressedPointCloud` protobuf output through a bundled Windows native Draco plugin.

## Highlights

- **Point-cloud output mode selector:** `FoxglovePointCloudPublisher` now exposes Raw and Draco output modes.
- **CompressedPointCloud publishing:** Draco mode publishes `/unity/point_cloud_draco` as `foxglove.CompressedPointCloud` with `format = draco`.
- **Bundled Windows Draco plugin:** Phase 89 uses `Unity2FoxgloveDracoNative.dll` through `DracoPointCloudNativeEncoder`; users no longer need to build or select a helper executable for the productized path.
- **Inspector validation:** `Check Draco` performs a tiny XYZ encode smoke against the bundled native plugin and reports the Draco version/commit and payload size.
- **Evidence tooling:** Phase 87-88 spike and evidence tools remain available for protocol-level smoke testing and MCAP inspection.

## Compatibility Notes

- Existing Unity scenes keep serialized Inspector values unless changed manually.
- Raw point-cloud publishing remains the default and keeps existing JSON/protobuf behavior.
- Draco mode is Windows-native in this release and is synchronous in the publish path.
- Missing or incompatible native Draco plugin binaries log a warning and publish nothing; the SDK does not silently fall back to raw on the Draco topic.
- Google Draco source and build outputs are not vendored into the SDK package. The bundled native plugin is covered in `THIRD_PARTY_NOTICES.md`.

## Verification

Completed before preparing this release:

```bash
dotnet run --no-restore --project Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj
python Scripts/release/validate_package.py
git diff --check
```

Targeted validation:

- Phase 87: 24 checks passed.
- Phase 88: 20 checks passed.
- Phase 89: 35 checks passed.
- Full runtime suite: all checks passed.
- Package validation: 28 checks passed.

Manual Foxglove validation confirmed:

- Unity Inspector `Check Draco` reports the bundled plugin as available.
- `/unity/point_cloud_draco` is advertised as `foxglove.CompressedPointCloud`.
- Foxglove 3D renders the compressed point cloud.
