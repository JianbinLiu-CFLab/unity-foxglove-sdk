# Unity2Foxglove v1.9.6 Release Notes

Release date: 2026-07-06

Unity2Foxglove v1.9.6 is a large reliability, sensor-throughput, ROS2
runtime, FoxRun/FoxService, and maintainability release over v1.9.5. It covers
the work merged from PR #181 through PR #245, including the 141D-F FoxService
line, Phase145-157 runtime surfaces, Phase159-162 ROS2 runtime packaging,
Phase163-164 hardening and optimization sweeps, Phase165-167 native/R2FU and
driver-style point-cloud stabilization, Phase168-172 Foxglove encoding,
camera, replay/session decomposition, and optional Remote Gateway work.

The core SDK remains ROS-free by default. ROS2 For Unity and Remote Gateway
paths stay optional and package-isolated.

## Highlights

- **FoxService and FoxRun growth:** Added declarative FoxService RPC, schema
  inspector polish, DTO validation convergence, DTO walker de-duplication,
  conditional FoxRun publish gates, SDK-style channel wrappers, protobuf
  channel session guards, aggregate FoxRun message generation, additive sink
  fanout, optional ROS2/R2FU sink boundaries, and guarded subscribe/service
  surfaces.
- **MCAP and sink surfaces:** Added per-sink channel filtering, lazy MCAP
  file-order iterators, MCAP metadata amendments, MCAP private-record support,
  and stronger replay/DataLoader validation coverage.
- **System and profiling instrumentation:** Added a system-info publisher,
  profiler marker infrastructure, and Unity profiler acceptance probes to make
  runtime cost and environment evidence easier to capture.
- **ROS2 runtime packaging:** Added repo-local ROS2 entrypoint hygiene, Humble
  R2FU Win64 runtime import, Jazzy Win64 runtime refresh, Lyrical runtime
  selection, and Lyrical Zenoh prerequisite documentation while preserving the
  core SDK's ROS-free boundary.
- **Security and correctness hardening:** Phase163 reviewed and hardened major
  runtime surfaces including lifecycle, sessions, transports, registries,
  MCAP, replay cursor, DataLoader/R2FU, schema tooling, FoxRun, source
  generator behavior, ROS2 bridge paths, sample validation, package boundaries,
  and unit/runtime validation signals.
- **Performance and validation optimization:** Phase164 reduced allocations,
  copy churn, scanner cost, validation time, and release-check overhead across
  camera, video sidecar, point clouds, ROS2 CDR, WebSocket transport, MCAP
  reader/writer/replay, schema tooling, FoxRun generator paths, sample
  validation, release CI, and runtime validation naming guards.
- **Native/R2FU lifecycle stability:** Hardened R2FU native bridge lifecycle
  behavior during Editor scene transitions, backup-scene/domain-reload windows,
  standalone ROS environment setup, Zenoh router lifecycle, and native
  PointCloud2 publish stalls.
- **Driver-style point-cloud pipeline:** Stabilized the native PointCloud2 path
  with source-side admission, warm buffers, pooled deskew/packed buffers,
  driver-style bounded stages, and clearer timing/back-pressure diagnostics.
  The architecture was informed by public Ouster SDK driver patterns, without
  bundling or translating Ouster SDK code.
- **Foxglove encoding and live-view responsiveness:** Added MsgPack encoding
  support and capped source-driven VirtualLidar native Draco snapshots by
  default to keep Foxglove live visualization responsive.
- **Camera stall attribution and health gate:** Added camera slow-stage
  diagnostics and a balanced camera health admission gate. The gate can skip
  capture before `Camera.Render()` under render/readback/encode/completed
  queue/pixel/video pressure, protecting Editor responsiveness instead of
  growing queues.
- **H.264 stream safety:** H.264 pressure is handled before frames enter the
  encoder. Already encoded access units are not stale-dropped out of order, so
  P-frame continuity is preserved.
- **Optional Remote Gateway package:** Added a default-off Windows x64 package
  shape for mirroring local Foxglove channels into the official Foxglove
  gateway C ABI path. The v1 package is outbound visualization only and does
  not bundle the native gateway DLL.
- **Maintainability decomposition:** Session/replay helpers, MCAP reader
  helpers, FoxRun generator helpers, and manager state/helper classes were
  extracted while preserving public APIs, serialized Inspector fields, and
  replay behavior.

## Compatibility Notes

- Existing Unity scenes keep serialized Inspector values unless changed
  manually, except newly introduced serialized fields use their code defaults.
- The core `dev.unity2foxglove.sdk` package is versioned as v1.9.6.
- Camera health mode defaults to `Balanced` for newly imported/defaulted
  settings. Use `Off` as a diagnostic comparator when investigating camera
  cadence or subjective smoothness.
- JPEG stress can still reduce visible camera cadence under heavy encode load;
  the intended behavior is explainable skipping rather than main-thread stalls
  or unbounded queues.
- Draco visualization now favors Editor responsiveness by default. Set
  `Native LiDAR Max Rate Hz` to `0` to publish every completed source-driven
  native Draco scan.
- Remote Gateway remains optional, default-off, outbound-only, and gated by a
  valid Foxglove Cloud account plan/device-token path. Cloud live-connection
  validation was not completed for this release because the tested accounts did
  not expose remote access.
- Optional ROS2 For Unity runtime packages remain package-based and
  platform/runtime-specific; the core WebSocket, MCAP, FoxRun, and camera paths
  do not require ROS2.

## Verification

Release preparation used:

```bash
python Scripts/release/bump_version.py 1.9.6 --date 2026-07-06
```

Recent Phase172 validation before merge:

```bash
dotnet test Packages/dev.unity2foxglove.sdk/Tests/Unit/FoxgloveSdk.UnitTests.csproj --filter FullyQualifiedName~Camera -p:UseSharedCompilation=false
dotnet run --project Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj -- --phase172
dotnet run --project Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj -- --phase138j
dotnet run --project Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj -- --phase138k
git diff --check
```

Observed Phase172 results:

- Camera unit tests passed: 24 tests.
- `--phase172` passed: 8 checks.
- `--phase138j` passed: 35 checks.
- `--phase138k` passed: 12 checks.
- Git whitespace checks passed.

Representative validation merged during this release cycle included:

- Runtime validation gates for FoxRun/FoxService, MCAP, replay/DataLoader,
  ROS2/R2FU package boundaries, schema generation, source-generator freshness,
  WebSocket/session behavior, camera/video, point-cloud, and release hygiene.
- Package validators for the core Unity package, ROS2 For Unity adapter, Jazzy
  runtime package, local entrypoints, generated schema outputs, and repository
  public/private boundary checks.
- xUnit unit-test coverage for pure helper extraction, camera health policy,
  H.264 output pressure contracts, replay/session helper behavior, and source
  shape guardrails.
- GitHub Actions passed on merged PRs throughout the cycle for docs, package
  checks, analyzer freshness, runtime tests, and repository checks.

Manual Unity/Foxglove acceptance during the late release cycle covered:

- JPEG default path with 10 Hz cadence-gate intent: accepted after the 1 Hz
  cooldown-clamp issue was fixed.
- JPEG pressure path: accepted for safety and diagnostics, with expected
  pressure skips under heavy encode load.
- Camera Health Mode Off comparator: useful as a diagnostic fallback and, in
  one subjective run, felt smoother than Balanced under the tested scene load.
- H.264 FFmpeg path: Unity-side video health remained clean in the inspected
  diagnostic segment, with no dimension mismatch, submit failure, sidecar
  restart, or access-unit continuity issue.
- Remote Gateway Inspector/default-off/no-DLL fallback behavior was validated;
  real Foxglove Cloud remote-access validation remains account-plan gated.

Final release validation should include:

```bash
python Scripts/release/run_ci.py
python -m unittest Scripts.release.regression_checks.test_release_tooling
git diff --check
```
