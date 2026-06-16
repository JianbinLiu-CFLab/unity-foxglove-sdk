# Unity2Foxglove v1.9.5 Release Notes

Release date: 2026-06-14

Unity2Foxglove v1.9.5 is a substantial sensor, replay, ROS2, performance, and release-tooling upgrade over v1.9.4. It adds a richer virtual sensor stack, remote MCAP playback workflows, optional Foxglove replay synchronization controls, refreshed ROS2 For Unity packaging, and broad hot-path hardening across the SDK.

The core SDK remains ROS-free by default. ROS2 For Unity support is still optional and package-based.

## Highlights

- **Virtual sensor and demo stack:** Added the virtual LiDAR maze demo path, multi-vendor LiDAR profiles, high-rate Virtual IMU, full-fidelity LiDAR scan scheduling, PointCloud2 mirror/native output, camera native DDS output, TF native publishing, LiDAR deskew/motion-compensated output, and updated sample scene assets.
- **Remote MCAP and replay workflow:** Added Remote DataLoader HTTP support, remote MCAP file playback, Foxglove deep-link/file URL workflow, and a Unity cursor endpoint for Foxglove-driven replay control.
- **Unity Replay Sync panel:** Added the optional Foxglove extension panel with endpoint/token state, cursor-rate control, ACK-paced cursor backpressure, timeout recovery, and an experimental `Follow Unity replay` mode for heavy Unity scenes where Foxglove should not outrun Unity.
- **Hot-path performance reductions:** Reduced allocations and copy churn across camera JPEG publishing, H.264/OpenH264 sidecar conversion, point-cloud encoding, IMU covariance publishing, WebSocket transport, MCAP recording/replay/readers, protobuf builders, schema registries, ROS2 bridge/CDR paths, validation harnesses, and release/smoke scripts.
- **ROS2 For Unity Jazzy runtime refresh:** Refreshed the optional Jazzy Windows x64 runtime package from a new runtime artifact, regenerated runtime inventory, updated validators, and added repeatable sync tooling under `Scripts/ros2forunity/`.
- **Structure and maintainability:** Split large manager, point-cloud publisher, Virtual IMU sample queue, sensor publisher helper, camera, lidar, and replay-related implementation units while preserving user-facing serialized Inspector behavior.
- **Test migration and validation hardening:** Introduced and expanded the xUnit unit-test track, optimized source-shape/runtime validations, hardened CI restore/output boundaries, and kept Unity/ROS2/Foxglove/Desktop/socket acceptance in the runtime/smoke layer.
- **Diagnostics:** Added publish cadence diagnostics, frame-stall diagnostics, replay enable diagnostics, OpenH264 install diagnostics, and clearer cursor ownership behavior for explaining visualization jitter and timeline ownership issues.

## Compatibility Notes

- Existing Unity scenes keep serialized Inspector values unless changed manually.
- The core `dev.unity2foxglove.sdk` package is versioned as v1.9.5.
- The core SDK remains usable without ROS2 For Unity. Foxglove WebSocket, MCAP recording, replay, and generated-schema workflows do not require ROS2 packages.
- ROS2 For Unity support remains optional and package-based: use the adapter package for the facade layer and the Jazzy Windows x64 runtime package for the bundled runtime.
- The Unity Replay Sync `Follow Unity replay` mode is experimental. Foxglove's extension context exposes `seekPlayback`, but the available panel API does not expose play/pause control, so Follow should be used as its own pacing mode rather than together with Foxglove free-run playback.
- Foxglove point-cloud panels can still flicker under Follow because the Foxglove UI is driven by throttled seek operations. Unity is the smooth view in this mode; leave Follow off for the smoothest native Foxglove playback in light scenes.

## Verification

Release preparation was validated with:

```bash
python Scripts/release/bump_version.py 1.9.5 --date 2026-06-14
python Scripts/release/run_ci.py
python -m pytest Scripts -p no:cacheprovider
python -m compileall -q Scripts
cd Tools/foxglove-extensions/unity-cursor-bridge && npm test -- --run
cd Tools/foxglove-extensions/unity-cursor-bridge && npm run build
git diff --check
```

`run_ci.py` covers the runtime validation runner, xUnit unit tests, source-generator build, package validator, ROS2 For Unity adapter validator, Jazzy runtime package validator, and package-boundary check.

Manual acceptance during the release cycle covered:

- Unity Profiler GC allocation checks for camera JPEG publishing, point-cloud publishing, IMU covariance publishing, and replay diagnostics.
- Foxglove Desktop live visualization for camera, Draco point cloud, IMU data, TF, replay cursor sync, and Unity-paced follow.
- RViz2/Jazzy Windows validation for PointCloud2 native DDS output, refreshed ROS2 For Unity runtime import, and TF/PointCloud2 graph evidence.
- OpenH264 runtime install diagnostics and H.264 camera publishing.

Observed results:

- Runtime and xUnit validation passed.
- Release package, ROS2 For Unity runtime package, and ROS2 For Unity adapter package validators passed.
- Extension vitest tests and extension build passed.
- Python release/tooling tests and compile checks passed.
- Git whitespace checks passed.
