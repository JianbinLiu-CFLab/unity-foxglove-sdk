# Scripts

Project-level helper scripts. Scripts under this directory are Python entry points so they can run on Windows, Ubuntu/Linux, and macOS without requiring PowerShell. All paths are resolved relative to each script location or the workspace root; no hardcoded absolute paths are required.

Directory layout:

```text
Scripts/
  build_tools/  Unity Player build entry points
  performance/  Benchmark and performance baselines
  release/      Version bump and package release checks
  samples/      Sample-content synchronization
  smoke/        Manual smoke fixtures and protocol clients
```

## Package Version Bump

Entry script:

```text
Scripts/release/bump_version.py
```

Purpose:

- Synchronize the Unity package version across `package.json`, runtime metadata validation, README badges, package README notes, changelog, and release-note stubs.
- Support a dry run before editing files.
- Avoid git commits, tags, GitHub releases, or DOI changes. Those remain manual release steps.

Basic usage:

```bash
python Scripts/release/bump_version.py 1.2.0 --date 2026-05-11 --dry-run
python Scripts/release/bump_version.py 1.2.0 --date 2026-05-11
```

## Unity IL2CPP Build

Entry script:

```text
Scripts/build_tools/unity_il2cpp.py
```

Purpose:

- One command to start a Unity batchmode IL2CPP build.
- Supports Windows, Linux, and macOS standalone targets.
- Creates a timestamped directory per build, keeping the log and Player output together.
- Invokes Unity-side `FoxgloveBuild.BuildIl2CppFromCommandLine`.

Basic usage from the workspace root:

```bash
python Scripts/build_tools/unity_il2cpp.py
```

The default target is selected based on the current system:

- Windows: `win64`
- Linux: `linux64`
- macOS: `macos`

Specifying a target:

```bash
python Scripts/build_tools/unity_il2cpp.py --target win64
python Scripts/build_tools/unity_il2cpp.py --target linux64
python Scripts/build_tools/unity_il2cpp.py --target macos
```

Dry run:

```bash
python Scripts/build_tools/unity_il2cpp.py --target win64 --dry-run
```

Specifying Unity:

```bash
python Scripts/build_tools/unity_il2cpp.py --target win64 --unity "/path/to/Unity"
```

Or set `UNITY_EXE` / `UNITY_PATH` in the shell before running the script.

Specifying output locations:

```bash
python Scripts/build_tools/unity_il2cpp.py --target win64 --log build/Unity/manual-win64-il2cpp.log
python Scripts/build_tools/unity_il2cpp.py --target win64 --build-dir build/Unity/manual-win64
python Scripts/build_tools/unity_il2cpp.py --target win64 --output build/Unity/manual-win64/WindowsIL2CPP/FoxgloveDemo.exe
```

Cross-platform notes:

- Building for Linux/macOS requires the corresponding Unity Build Support modules.
- A Windows host may not produce fully signed or releasable macOS Players; build macOS on macOS when possible.
- IL2CPP builds are time-consuming. Use `--dry-run` first to validate parameters.
- Close the Unity Editor before building to avoid Library/script compilation state conflicts.

## Performance Baseline

Entry script:

```text
Scripts/performance/run_baseline.py
```

Purpose:

- Run SDK performance scenarios through the .NET performance project.
- Support quick and full modes.
- Write JSON summaries under `build/performance/` by default.

Basic usage:

```bash
python Scripts/performance/run_baseline.py --quick
python Scripts/performance/run_baseline.py --full
python Scripts/performance/run_baseline.py --quick --output build/performance/
```

## Full Demo Sample Sync

Entry script:

```text
Scripts/samples/sync_full_demo.py
```

Purpose:

- Synchronize the project demo assets into the package `Samples~` layout.
- Keep package sample contents reproducible from the workspace.

Basic usage:

```bash
python Scripts/samples/sync_full_demo.py --dry-run
python Scripts/samples/sync_full_demo.py
```

## Release Package Validation

Entry script:

```text
Scripts/release/validate_package.py
```

Purpose:

- Validate package metadata, required files, sample declarations, sample assets, and forbidden generated artifacts before release.

Basic usage:

```bash
python Scripts/release/validate_package.py
```

## Smoke: Phase 34 Attachment MCAP

Entry script:

```text
Scripts/smoke/phase34_attachment_mcap.py
```

Purpose:

- Generate a small MCAP file containing a schema, channel, message chunk, attachment, attachment index, chunk index, statistics, summary offsets, footer, and CRCs.
- Self-check the generated file so it can be used as an interop smoke fixture.

Basic usage:

```bash
python Scripts/smoke/phase34_attachment_mcap.py
python Scripts/smoke/phase34_attachment_mcap.py --output build/test_mcap/phase34_attachment_smoke.mcap
```

## Smoke: Phase 40 Slow Camera Client

Entry script:

```text
Scripts/smoke/phase40_slow_camera_client.py
```

Purpose:

- Connect to a local Foxglove WebSocket server.
- Subscribe to `/unity/camera`, or fall back to a channel ID range when advertise is not observed.
- Stop reading after subscribing so camera backpressure behavior can be observed.

Basic usage:

```bash
python Scripts/smoke/phase40_slow_camera_client.py
python Scripts/smoke/phase40_slow_camera_client.py --advertise-timeout-seconds 15 --hold-seconds 120
python Scripts/smoke/phase40_slow_camera_client.py --no-fallback
```

Defaults:

- Host: `127.0.0.1`
- Port: `8765`
- Fallback channel IDs: `1..128`

## Smoke: TF WebSocket Client

Entry script:

```text
Scripts/smoke/tf_websocket_smoke.py
```

Purpose:

- Connect to a local Foxglove WebSocket server.
- Subscribe to the demo `/tf` channel.
- Print decoded binary MessageData and Time frames for manual inspection.

Basic usage:

```bash
python Scripts/smoke/tf_websocket_smoke.py
python Scripts/smoke/tf_websocket_smoke.py --port 8765 --max-frames 20
```

## Smoke: fetchAsset Client

Entry script:

```text
Scripts/smoke/fetch_asset_smoke.py
```

Purpose:

- Connect to a local Foxglove WebSocket server.
- Send a `fetchAsset` request for a demo asset URI.
- Validate the binary fetchAsset response and optionally save the payload.

Basic usage:

```bash
python Scripts/smoke/fetch_asset_smoke.py
python Scripts/smoke/fetch_asset_smoke.py --uri asset://demo/Scripts/FoxgloveDemoSetup.cs --output build/smoke/fetched_demo.cs
```

## Smoke: Phase 44 All Schemas MCAP

Entry script:

```text
Scripts/smoke/phase44_all_schemas_mcap.py
```

Purpose:

- Generate a protobuf all-schema MCAP smoke fixture through the runtime validation project.
- Keep manual smoke generation and automated validation on the same code path.

Basic usage:

```bash
python Scripts/smoke/phase44_all_schemas_mcap.py
python Scripts/smoke/phase44_all_schemas_mcap.py --output build/test_mcap/phase44_all_schemas_smoke.mcap
```
