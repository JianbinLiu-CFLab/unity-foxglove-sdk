# Draco Point Cloud Probe Helper

This folder contains source for the Phase 87 `foxglove.CompressedPointCloud`
Draco feasibility spike. It is not a production SDK binary and must be built
locally.

## Local Source Boundary

Clone Google Draco into the ignored third-party workspace:

```powershell
git clone https://github.com/google/draco.git third-party\draco
```

Record the exact Draco source tag or commit used for every smoke result. The
repository ignores `third-party/`, so Draco source, build outputs, and helper
executables remain local.

## Initial Encoder Settings

- Geometry type: `POINT_CLOUD`, not mesh.
- Position attribute: one 3-component `POSITION` vector attribute.
- Position quantization: 11-bit.
- Initial compression level 7 target. In the C++ API this helper maps that initial
  target to `SetSpeedOptions(3, 3)`, because Draco speed options are inverse
  to CLI-style compression level: lower speed values spend more CPU for better
  compression.
- Attributes: XYZ only. Normals, colors, texture coordinates, intensity, ring,
  and per-point time remain future work.

## Helper Protocol

All integers and floats are little-endian.

Stdin frame:

- `uint32 point_count`
- `point_count` records of:
  - `float32 x`
  - `float32 y`
  - `float32 z`

Stdout response:

- `uint32 payload_length`
- `payload_length` bytes of Draco point-cloud payload

Stderr is diagnostic text only and must never be parsed as data. Every complete
stdin frame must produce exactly one stdout response or the helper should write
a clear diagnostic to stderr and exit non-zero.

## Build Notes

Build Draco locally first using its documented CMake flow, then compile this
helper against the local headers and libraries. Exact library names vary by
platform and Draco build options, so treat this as the expected shape:

```powershell
cl /EHsc /std:c++17 `
  /I third-party\draco\src `
  Scripts\native\draco_probe\draco_probe_encoder.cpp `
  third-party\draco\build\Release\draco.lib `
  /OUT:third-party\draco_probe_encoder.exe
```

Do not commit compiled helpers, Draco libraries, or vendored Draco source under
`Packages/` or `Unity2Foxglove/Assets/`.

## Manual Smoke

1. Build the helper under an ignored path such as `third-party/`.
2. Open the `Unity2Foxglove` demo project.
3. Add or enable the experimental compressed point-cloud publisher.
4. Set the helper executable path to the locally built helper.
5. Enter Play Mode and connect Foxglove.
6. Subscribe to `/unity/point_cloud_draco`.
7. Confirm Foxglove 3D displays the compressed point cloud.
8. Record MCAP and reopen it in Foxglove.
9. Compare payload sizes against `/unity/point_cloud`.

## Result States

- GREEN: Foxglove 3D displays `/unity/point_cloud_draco`, MCAP replay preserves
  it, and payload size is smaller than raw point cloud for the smoke source.
- YELLOW: Draco bytes publish and record, but Foxglove display requires a
  metadata mapping or client-support clarification.
- RED: tested metadata strategies do not render, or helper integration is too
  brittle to productize.
- BLOCKED: Draco cannot be built or linked with the available local toolchain.

## Licensing Boundary

Google Draco is Apache-2.0, but this spike does not bundle Draco source or
binaries. Productization still needs a dedicated dependency, notices, and
distribution decision.
