# Unity2Foxglove v1.6.0 Release Notes

Release date: 2026-05-16

Unity2Foxglove v1.6.0 focuses on higher-throughput visualization workflows and release-ready local tooling. It adds indexed MCAP reader surfaces, subscription-aware heavy-topic QoS, stable live publish cadence, camera video output backends, and the first point-cloud budget/LOD layer.

## Highlights

- **MCAP replay preflight:** indexed reader summaries are now exposed through the demo Inspector flow, including chunk/message/index metadata and validation smoke coverage.
- **Stable publisher cadence:** publisher throttling now uses next-due scheduling so configured rates remain stable when Unity frame timing varies.
- **Camera video pipeline:** camera output can remain JPEG by default or switch to `foxglove.CompressedVideo` through H.264, H.265/HEVC, OpenH264, or experimental Windows Media Foundation H.264 modes.
- **Subscription-aware heavy topics:** expensive camera, scene, laser, transform, and point-cloud payload work is skipped until a live subscriber or MCAP recorder needs it.
- **Point-cloud QoS layer:** point clouds now support max-point budgets, packed-byte budgets, first-point sampling, uniform-stride sampling, voxel-grid LOD, and a 1000-point demo smoke source.
- **Smoke tooling:** protocol-level probes can validate live topic rates and point-cloud payload sizes without relying on Foxglove UI smoothing.

## Compatibility Notes

- Existing Unity scenes keep serialized Inspector values unless changed manually.
- JPEG remains the default camera output mode and requires no external encoder.
- FFmpeg video modes require a user-provided FFmpeg executable and are intentionally not auto-installed by the SDK.
- OpenH264 mode downloads the pinned Cisco runtime only when the user explicitly runs the installer from the Inspector; OpenH264 binaries are not bundled in the SDK package.
- Windows Media Foundation H.264 mode is experimental and Windows-only.
- The point-cloud QoS layer keeps the raw `foxglove.PointCloud` schema; this release does not introduce Draco or `foxglove.CompressedPointCloud`.

## Verification

Completed before publishing the release:

```bash
dotnet run --project Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj
python Scripts/release/validate_package.py
```

Manual smoke coverage included:

- MCAP indexed-reader replay preflight against the newest demo recording.
- Topic-rate probes for fixed-rate live publishing.
- Camera H.264/H.265/OpenH264 playback in Foxglove where supported by the client platform.
- Point-cloud probe and Foxglove 3D panel validation using the 1000-point smoke source.
