# Unity2Foxglove v1.9.4 Release Notes

Release date: 2026-05-27

Unity2Foxglove v1.9.4 is a stability and release-readiness update focused on stronger validation, cleaner ROS2 For Unity package boundaries, and more repeatable tooling for MCAP, RViz2, native encoder, and package workflows.

## Highlights

- **Stronger public validation boundary:** Repository checks now avoid private workspace notes, ignored files, and machine-local sibling repositories as evidence. A clean open-source checkout can run the validation gates without depending on local operator folders.
- **ROS2 For Unity package hardening:** Runtime package detection, optional adapter behavior, sample availability, and imported Unity sample parity were tightened so missing runtime state is reported clearly instead of causing hidden side effects.
- **RViz2 and standard ROS2 sample coverage:** The optional ROS2 For Unity sample line now has broader coverage for standard visualization flows, standard message publishing, helper scripts, and synchronized imported sample assets.
- **MCAP and replay stability:** MCAP reader, writer, conformance, replay, and DataLoader checks were expanded around malformed records, CRC behavior, direct-message layouts, summary-less files, and bounded fallback paths.
- **Native tooling diagnostics:** OpenH264, Draco, and related helper scripts now fail more clearly on version, process, pipe, timeout, and cleanup problems.
- **Release and CI reliability:** Package validators, docs checks, repository-boundary checks, Python script checks, performance baselines, and source-generator freshness checks were tightened for release use.

## Compatibility Notes

- Existing Unity scenes keep serialized Inspector values unless changed manually.
- The core SDK remains usable without ROS2 For Unity. Foxglove WebSocket, MCAP recording, replay, and generated-schema workflows do not require ROS2 packages.
- ROS2 For Unity support remains optional and package-based: use the adapter package for the facade layer and the Jazzy Windows x64 runtime package for the bundled runtime.
- MCAP recording file names now use UTC timestamps with fractional ticks. Scripts that parse recording names should not assume the older local-time, second-precision suffix.
- MCAP LZ4 defaults now prioritize real-time recording throughput. Set `McapWriterOptions.Lz4CompressionLevel` explicitly when smallest-file compression is more important.
- Replay keeps warning-on-CRC-mismatch behavior by default, with stricter skip or throw modes available for tools that need hard enforcement.

## Verification

Release preparation was validated with:

```bash
python Scripts/release/bump_version.py 1.9.4 --date 2026-05-27
dotnet build Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/FoxgloveLogSourceGenerator.csproj -c Release -o build/SourceGenerators/Release/netstandard2.0
dotnet run --project Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj
python Scripts/release/validate_package.py
python Scripts/release/validate_r2fu_runtime_package.py
python Scripts/release/validate_ros2forunity_package.py
python Scripts/performance/run_baseline.py --quick --output build/performance/release
python -m compileall -q Scripts
git diff --check
```

Observed results:

- GitHub Actions passed for runtime tests, analyzer freshness, package structure checks, and docs checks.
- Runtime validation completed successfully.
- Extended runtime, local evidence, MCAP conformance, ROS2 visualization, and standard message validation gates completed successfully.
- Release package, ROS2 For Unity runtime package, and ROS2 For Unity adapter package validators passed.
- Public docs link/path/encoding checks and repository private-boundary checks passed.
- Quick performance baseline passed.
- Python compile checks and git whitespace checks passed.
