# Unity2Foxglove v1.8.0 Release Notes

Release date: 2026-05-17

Unity2Foxglove v1.8.0 adds an optional ROS2 bridge workflow while keeping the default Unity-to-Foxglove WebSocket and MCAP paths unchanged. The ROS2 bridge is disabled by default and is intended for developers who explicitly want to mirror selected Unity publisher payloads into a local ROS2 graph.

## Highlights

- Optional ROS2 bridge output for selected publishers.
- ROS2 CDR payload support for transforms, scene cubes, camera frames, camera calibration, laser scans, raw point clouds, and Draco point clouds.
- ROS2 bridge namespace controls, per-publisher topic overrides, simple QoS presets, queue/drop/failure counters, and Inspector health diagnostics.
- ROS2 bridge sample scene, Foxglove layout, launch file, and WSL-friendly helper scripts.
- Manual validation evidence for Unity, Foxglove, WSL Ubuntu 24.04, and ROS2 Jazzy.

## Compatibility Notes

- Existing Unity scenes keep serialized Inspector values unless changed manually.
- Normal Foxglove WebSocket streaming, MCAP recording, and MCAP replay do not require ROS2.
- The ROS2 bridge remains optional and localhost-oriented. It mirrors supported CDR payloads to a sidecar under `Tools/ros2_bridge`.
- Windows-local `ros2` CLI setup is treated separately from WSL sidecar operation. A valid WSL sidecar can be used even when Unity cannot find a Windows `ros2` executable.
- Native in-process ROS2 publishing, ROS2 subscriptions into Unity, MCAP replay fanout to ROS2, and RViz2-standard message mirrors are not part of this release. They are being evaluated as possible future directions.

## Verification

Validated before preparing this release:

```bash
dotnet run --no-restore --project Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj
python Scripts/release/validate_package.py
```

Manual smoke validation covered:

- Unity Play Mode without compile errors.
- Foxglove WebSocket connection at `ws://127.0.0.1:8765`.
- ROS2 bridge sidecar launch on `127.0.0.1:8767`.
- ROS2 topic list/info/echo checks under the bridge namespace.
- Topic rate and QoS inspection for bridge-published topics.
- Foxglove 3D scene cube rendering, camera view, transform panel, plot panel, laser scan, and point-cloud topics.
