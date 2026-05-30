---
title: "Phase 138E - Burst Job + NativeArray Point Cloud Pipeline"
aliases:
  - Phase 138E Burst Point Cloud
tags:
  - plan
  - phase138e
  - pointcloud
  - nativearray
  - burst
  - lidar
status: in_progress
updated: 2026-05-30
---

# Phase 138E - Burst Job + NativeArray Point Cloud Pipeline

## Goals

- Replace per-frame managed-point heavy path in `VirtualLidar` with Burst job preprocessing.
- Keep shared schema layer as managed (`PointCloudFrame.Points: List<PointCloudPoint>`).
- Introduce a clear valid-point count contract using `PointCloudFrame.ValidCount` for downstream payload limits and sampling.
- Keep Unity-free schema/build pipeline runnable in tests by guarding Unity/Burst-only files.

## Completed implementation notes

### Data model

- `PointCloudPoint` remains class-based (per existing schema contract).
- `PointCloudFrame` now includes:
  - `int ValidCount` with default `-1` (meaning `Points.Count`).
- Added `PointCloudFrameExtensions.GetPointCount()`:
  - returns `ValidCount` when non-negative and clamped to available list count;
  - fallback to `Points.Count` when `ValidCount < 0`.

### VirtualLidar Burst path

- Added `Runtime/Sensors/Lidar/VirtualLidarBuildPointsJob.cs` (`IJobParallelFor` + Burst).
- Added reusable native buffers in `VirtualLidar`:
  - `NativeArray<RaycastCommand>`, `NativeArray<RaycastHit>` for batched rays;
  - `NativeArray<float>`, `NativeArray<ushort>` for time offsets and ring index;
  - `NativeArray<VirtualLidarHitData>` as intermediate hit cache;
  - `NativeArray<VirtualLidarPointData>` as job output.
- Job computes valid point payload and range validation in worker context; main thread only copies back and assembles final managed frame.
- `frame.Points` remains managed; `frame.ValidCount` records effective valid count.

### Math conversion / coordinate helper

- Added `Runtime/Sensors/CoordinateConverterFloat3.cs` with float3-specific conversion helpers used by Burst path.
- Conversion matrix helper added via `Matrix4x4.ToFloat4x4()`.
- Wrapped with `UNITY_5_3_OR_NEWER` compile guard so dotnet test harness does not include Unity-only code.

### Downstream payload/builders

- All point-cloud packing, QoS, and publisher paths now use `GetPointCount()` as source of truth:
  - `PointCloudPackedDataBuilder`
  - `PointCloudQoS`
  - `DracoPointCloudNativeEncoder`
  - `DracoPointCloudEncoderSidecar`
  - `FoxglovePointCloudPublisher`
  - `FoxgloveCompressedPointCloudPublisher`
- `CloneFrameForBackgroundEncode()` in point-cloud publisher now clones only valid points and sets `ValidCount`.

### Dependency and asmdef changes

- `Runtime/Sensors/Unity.FoxgloveSDK.Sensors.asmdef`
  - added `Unity.Burst`, `Unity.Collections`, `Unity.Mathematics`
  - `allowUnsafeCode: true`
- `package.json`
  - added `com.unity.burst`, `com.unity.collections`, `com.unity.mathematics`

## Notable correctness rules applied

- `miss` guard in Burst path:
  - `ColliderInstanceId != 0 && hit.Distance > 0 && Distance within [MinRange, MaxRange]`.
- Rebuild native scan buffers on enable and cleanup on disable/destroy for lifecycle safety.
- No raw `NativeArray` is kept in frame payload; only managed points are stored in schema objects.

## 138E verification plan

1. **Schema consistency**
   - Confirm `Points` layout + `ValidCount` used as effective count in all point-cloud packing and publish paths.
2. **Burst behavior**
   - In editor, inspect `VirtualLidar` frame rate and profiler allocation for large scans (OS-1-128 baseline).
3. **Range/miss validation**
   - Verify no points outside range and miss rays are filtered.
4. **Encoder safety**
   - Confirm Draco payload allocation is per-frame from valid point count.
5. **Unity compile path sanity**
   - Validate there is no functional dependency of non-Unity test build on Burst/Unity-only sensor files.

## Open points (if any)

- Keep an eye on sample-mode behavior if future schema consumers rely on `Points.Count` directly and ignore `ValidCount`.
- If required, later phase can add explicit comments in `PointCloudFrame` to describe ownership guarantees to other plugins.
