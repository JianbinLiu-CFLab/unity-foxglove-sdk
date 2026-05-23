# Unity2Foxglove v1.9.1 Release Notes

Release date: 2026-05-23

Unity2Foxglove v1.9.1 is a focused MCAP compatibility and replay hardening release. It keeps the core SDK local-first and ROS-free by default while adding a DataLoader-shaped local reader surface, summary-less/direct MCAP fallback support, official compatibility fixtures, and documented manual acceptance for Unity replay and Foxglove Desktop local-file workflows.

## Highlights

- **Local MCAP DataLoader facade:** Local files can now expose initialization metadata, channel/schema inventory, message iteration, and latest-at backfill through a DataLoader-shaped API.
- **Summary-less/direct MCAP support:** Unity can inspect and replay no-summary/direct-message MCAP profiles, including zero-chunk fixtures used by the compatibility gate.
- **Replay and preflight evidence:** Unity-authored recordings were manually checked for Zstd and uncompressed MCAP output, paired `.schema/` sidecars, strict FoxRun hash match, replay startup, live-publisher suppression, and Foxglove visualization.
- **Compatibility gate fixtures:** Unity-authored indexed/direct fixtures and official Python-authored chunked/direct fixtures are generated and validated through the local reader gate.
- **DataLoader hardening:** Latest-at backfill avoids unbounded `0..T` materialization, sequential fallback has message/payload caps, CRC mismatch behavior is consistent, and chunk schema/channel decoding avoids avoidable temporary allocations.
- **Remote boundary clarity:** The remote MCAP work is explicitly a local prototype boundary for manifest/data DTOs and authorization behavior, not a production hosted data service.

## Compatibility Notes

- Existing Unity scenes keep serialized Inspector values unless changed manually.
- The core `dev.unity2foxglove.sdk` package is versioned as v1.9.1. The optional ROS2 For Unity adapter and Jazzy runtime package lines remain preview-scoped from v1.9.0.
- Normal Foxglove WebSocket, MCAP recording, replay, and FoxRun workflows do not require ROS2, ROS2 For Unity, Python, or the optional runtime package.
- Generated compatibility fixtures under `build/mcap-compat` are test artifacts and do not necessarily carry recording `.schema/` sidecars. Missing sidecar warnings on those fixtures are non-blocking for local reader/replay compatibility checks.
- Production Foxglove Remote Data Loader hosting, cloud cache/range serving, organization auth, device-token storage, Kubernetes/Helm deployment, multi-file timeline merge, and Remote Access Gateway support remain out of scope.
- Large unindexed MCAP files may still require an intentional second scan when moving from inventory to message reads; the fallback is now bounded by explicit message and payload limits.

## Verification

Preparation verification:

```bash
python Scripts/release/bump_version.py 1.9.1 --date 2026-05-23 --dry-run
dotnet build Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/FoxgloveLogSourceGenerator.csproj -c Release -o build/SourceGenerators/Release/netstandard2.0
dotnet run --no-restore --project Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj
python Scripts/release/validate_package.py
python Scripts/performance/run_baseline.py --quick --output build/performance/release
```

Observed results:

- Version synchronization prepared v1.9.1 package metadata, README references, changelog, and release notes.
- Targeted MCAP indexed reader regression passed with 44 checks.
- Targeted MCAP DataLoader hardening validation passed with 37 checks.
- Analyzer DLL build completed with 0 warnings and 0 errors.
- Runtime validation completed with `All checks passed`.
- GitHub Actions on the release PR passed docs check, package check, runtime tests, and analyzer freshness.

Manual acceptance covered:

- Zstd and uncompressed Unity-authored MCAP recordings.
- Closed recording files with matching `.schema/` evidence directories.
- Strict replay preflight with matching recorded/current FoxRun hashes.
- Unity replay with live publishers disabled and replayed Cube/camera/topic data visible.
- Foxglove Desktop opening a real Unity-authored recording and an indexed compatibility fixture.
- Unity reader/replay acceptance of the summary-less/direct compatibility fixture.
- Code-level confirmation that the remote MCAP work does not expose production remote/cloud/gateway UI or deployment behavior.
