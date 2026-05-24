# Unity2Foxglove v1.9.2 Release Notes

Release date: 2026-05-24

Unity2Foxglove v1.9.2 extends the MCAP compatibility track with official conformance coverage, writer/reader parity improvements, decoded DataLoader payload inspection, and ROS2 CDR typed decode v1. It also hardens repository hygiene by making `Plan/` and `Developer/` local-only workspace folders with automated gates.

## Highlights

- **Official MCAP conformance runner:** The MCAP conformance workflow now uses a project-owned Python wrapper and writes generated reports under repo-level `build/` paths.
- **MCAP writer option parity:** Writer profiles cover chunking, compression, CRC, summary behavior, and direct-message output so Unity-authored MCAP files can be tested against more ecosystem shapes.
- **Streaming reader/query parity:** The reader path now has coverage for indexed and sequential fallback behavior, including direct-message and summary-less files.
- **Decoded DataLoader view:** Local MCAP DataLoader reads can classify decoded JSON, decoded Protobuf, unsupported encodings, and malformed payloads while preserving raw message iteration.
- **ROS2 CDR typed decode v1:** Supported ROS2 message CDR payloads can be decoded through generated deserializers for typed inspection.
- **Private workspace boundary gate:** `Plan/` and `Developer/` are enforced as local-only folders through `.gitignore`, Phase16 validation, and a dedicated GitHub Actions check.

## Compatibility Notes

- Existing Unity scenes keep serialized Inspector values unless changed manually.
- The core `dev.unity2foxglove.sdk` package is versioned as v1.9.2. The optional ROS2 For Unity adapter and Jazzy runtime package lines remain preview-scoped from v1.9.0.
- Normal Foxglove WebSocket, MCAP recording, replay, and FoxRun workflows do not require ROS2, ROS2 For Unity, Python, or the optional runtime package.
- MCAP conformance reports and generated conformance artifacts are build/test outputs. They belong under repo-level `build/` paths and must not be emitted into Unity package directories.
- `Plan/` and `Developer/` remain useful local working folders, but they are not part of the public repository contract and are blocked from git tracking.

## Verification

Preparation verification:

```bash
python Scripts/release/bump_version.py 1.9.2 --date 2026-05-24 --dry-run
dotnet run --no-restore --project Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj
python Scripts/release/validate_package.py
python Scripts/performance/run_baseline.py --quick --output build/performance/release
git diff --check
git ls-files -- 'Plan/**' 'Developer/**'
```

Observed results:

- Version synchronization prepared v1.9.2 package metadata, README references, changelog, and release notes.
- Version synchronization dry-run reported all v1.9.2 references already aligned.
- Runtime validation completed with `All checks passed`.
- Release package validation completed with 31 checks passed.
- Quick performance baseline completed under `build/performance/release`.
- Git whitespace check passed.
- Private workspace tracking check returned no `Plan/` or `Developer/` paths.
- GitHub Actions on the release PR passed docs check, package check, runtime tests, analyzer freshness, and repository boundary check.
