# Unity2Foxglove v1.9.0 Release Notes

Release date: 2026-05-22

Unity2Foxglove v1.9.0 is a larger boundary release: the core SDK stays ROS-free by default, while the optional ROS2 For Unity path now has a real adapter/runtime package split for Jazzy Windows x64 experiments. It also tightens FoxRun schema identity, MCAP replay guards, replay pose ownership, and IL2CPP-oriented generation checks.

## Highlights

- **Optional ROS2 For Unity runtime package:** `dev.unity2foxglove.ros2forunity.runtime.jazzy.win64` carries the Jazzy Windows x64 runtime files, package-path patch, manifest, checksum, file inventory, and third-party notices.
- **Adapter/runtime separation:** `dev.unity2foxglove.ros2forunity` remains source-only and can compile without runtime binaries; live ROS2-node behavior requires the runtime package or an external R2FU import.
- **FoxRun schema identity:** canonical manifests, runtime schema info, MCAP metadata, schema evidence sidecars, and replay schema checks are now part of the release surface.
- **Replay hardening:** replay pose ownership is behavior-based, schema identity modes are explicit, and tick caps no longer split messages with the same `LogTime`.
- **Generator/analyzer hardening:** FoxRun unsupported-type diagnostics and generated-source equivalence checks are covered for the IL2CPP/release path.

## Compatibility Notes

- Existing Unity scenes keep serialized Inspector values unless changed manually.
- The core `dev.unity2foxglove.sdk` package is versioned as v1.9.0. The optional ROS2 For Unity adapter and Jazzy runtime packages remain preview-scoped package lines.
- Normal Foxglove WebSocket, MCAP recording, replay, and FoxRun workflows do not require ROS2, ROS2 For Unity, Python, or the optional runtime package.
- The packaged R2FU runtime path is Jazzy Windows x64 only. Linux, macOS, Humble, Lyrical, alternate RMW implementations, and production multi-runtime conflict handling remain outside this release.
- WSL2 NAT is not a reliable ROS2 acceptance topology for R2FU DDS discovery; use Windows ROS2 Jazzy for local smoke or a real LAN, VPN, physical Linux host, or bridged VM for remote Linux acceptance.

## Verification

Preparation verification:

```bash
python Scripts/release/bump_version.py 1.9.0 --date 2026-05-22 --dry-run
dotnet run --no-restore --project Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj
python Scripts/release/validate_package.py
```

Observed results:

- Version synchronization dry-run reported all 1.9.0 references already aligned.
- Runtime validation completed with `All checks passed`.
- Release package validation completed with 31 checks passed.

Manual release acceptance should still cover Unity Play Mode, Foxglove, IL2CPP, MCAP replay, and the optional R2FU Jazzy Windows x64 adapter/runtime smoke path before publishing a public tag.
