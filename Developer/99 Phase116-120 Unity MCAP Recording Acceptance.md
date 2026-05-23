---
title: "Phase 116-120 Unity MCAP Recording Acceptance"
aliases:
  - Phase 116-120 Manual Acceptance
  - Unity MCAP Recording Acceptance
tags:
  - phase116
  - phase117
  - phase118
  - phase119
  - phase120
  - mcap
  - unity
  - manual-acceptance
status: pass-selected-local-mcap-workflows
updated: 2026-05-23
---

# Phase 116-120 Unity MCAP Recording Acceptance

## Result

116-120 Manual Acceptance Result: PASS for selected local MCAP workflows across recording/file-integrity step A, replay preflight step B, Unity replay step C, Unity-authored compatibility fixture checks in step D, Foxglove Desktop open step E, and Phase 119 remote-boundary negative confirmation step F.

This note records the local Unity-authored MCAP recording, replay preflight, Unity replay, Unity-authored compatibility fixture replay, Foxglove Desktop open, and Phase 119 remote-boundary negative checks requested on 2026-05-23. Official Python compatibility fixtures remain covered by the automated Phase 120 official tooling/local-reader report unless a separate manual visual pass is added.

## Environment

- Repository: `D:/BaiduSyncdisk/Obsidian Vault/Websocket/00 Inbox`
- Unity project: `D:/BaiduSyncdisk/Obsidian Vault/Websocket/00 Inbox/Unity2Foxglove`
- Recording directory: `D:/BaiduSyncdisk/Obsidian Vault/Websocket/00 Inbox/Unity2Foxglove/Recordings`
- Branch checked during file validation: `feature/phase120b-mcap-dataloader-hardening`
- Commit checked during file validation: `14bb562`

## Validated Recordings

| File | Compression | Size | Messages | Chunks | Schemas | Channels | Chunk CRC | Closed |
|---|---|---:|---:|---:|---:|---:|---|---|
| `phase116_120_manual_20260523_155220.mcap` | `zstd` | 504457 bytes | 2873 | 5 | 4 | 14 | PASS | PASS |
| `phase116_120_manual_20260523_155842.mcap` | `none` | 7286090 bytes | 4884 | 7 | 4 | 14 | PASS | PASS |

Both files contain the expected topics:

- `/debug/115f/array`
- `/debug/115f/list`
- `/debug/115f/nullable`
- `/debug/115f/scalar`
- `/debug/115f/string`
- `/debug/115f/vector`
- `/debug/health`
- `/debug/overlay_smoke`
- `/debug/position`
- `/debug/position2`
- `/scene`
- `/tf`
- `/unity/camera`
- `/unity/client_log`

## Acceptance Criteria

| Criterion | Result | Evidence |
|---|---|---|
| Zstd and None each have at least one usable `.mcap` | PASS | Runtime reader opened both files; `compressions=zstd` for `155220`, `compressions=none` for `155842`. |
| Recording files were closed after stop | PASS | Both files opened with `FileShare.None` in read/write mode: `exclusiveOpen=True`. |
| `.schema/` sidecar can pair with `.mcap` by timestamp/prefix | PASS | Exact prefix matches: `phase116_120_manual_20260523_155220.schema` and `phase116_120_manual_20260523_155842.schema`. |
| `.schema/` evidence is complete | PASS | Both `schema-evidence.json` files record the matching `.mcap`, `identityMode:"Strict"`, `complete:true`, and `warnings:[]`. |
| No crash, CRC, or summary-write errors found | PASS | MCAP chunk CRC checks passed; runtime summary/index read passed; Unity `Editor.log` scan found no MCAP/recording/CRC/summary exceptions in the recording window. |

## Replay Preflight Evidence

| Field | Value |
|---|---|
| Replay | `Recordings/phase116_120_manual_20260523_155842.mcap` |
| Recording Evidence | `Recordings/phase116_120_manual_20260523_155842.schema` |
| Recorded FoxRun Hash | `02f67b8128249d53032fc5cf3ed713bcb23f82049e99374d9ad1f5045f1f8c1c` |
| Current FoxRun Hash | `02f67b8128249d53032fc5cf3ed713bcb23f82049e99374d9ad1f5045f1f8c1c` |
| Status | `Match` |
| Evidence Directory Opened | `D:/BaiduSyncdisk/Obsidian Vault/Websocket/00 Inbox/Unity2Foxglove/Recordings/phase116_120_manual_20260523_155842.schema` |

Replay preflight result: PASS.

The recorded and current FoxRun hashes match exactly in `Strict` identity mode, and the paired recording evidence directory was opened successfully.

## Unity Replay Evidence

Replay target:

- `Recordings/phase116_120_manual_20260523_155842.mcap`

Observed Unity replay state:

- `MCAP Recording > Enable Recording`: off.
- `MCAP Replay > Enable Replay`: on.
- `Replay Auto Play`: on.
- `Disable Live Publishers`: on.
- Console records `[Foxglove] Disabled 4 live publisher(s)`.
- Console records `[Foxglove] Server started on ws://127.0.0.1:8765`.
- The Cube is visible in Play Mode and replayed pose/state is displayed in the Game view.
- Console records pose ownership arbitration as an informational replay message for `Cube`, with `ownerChannel=4`, `skippedChannel=3`, `source=scene`, `topic=/scene`, and `schema=foxglove.SceneUpdate`.

Observed Foxglove live visualization during replay:

- Connection: `ws://127.0.0.1:8765 Unity Foxglove SDK`.
- Topics visible include `/scene`, `/tf`, `/unity/camera`, `/debug/position`, `/debug/position2`, `/debug/health`, and debug 115F topics.
- `/scene` reports `foxglove.SceneUpdate`.
- `/tf` reports `foxglove.FrameTransform` and shows `parent_frame_id:"unity_world"` with `child_frame_id:"Cube"`.
- `/unity/camera` panel displays the replayed Unity camera image.
- 3D panels show the Cube on the grid.
- Raw message/parameter panels are populated.

Unity replay result: PASS.

The Unity local replay path can open the validated recording, disable live publishers, replay the scene/Cube state, and publish replayed visualization data to Foxglove without console hard errors for file open, schema mismatch block, chunk CRC, or summary parse.

## Compatibility Fixture Replay Evidence

### `unity_chunked_all_indexes.mcap`

Replay target:

- `D:/BaiduSyncdisk/Obsidian Vault/Websocket/00 Inbox/build/mcap-compat/unity_chunked_all_indexes.mcap`

Preflight result:

```text
Replay: D:/BaiduSyncdisk/Obsidian Vault/Websocket/00 Inbox/build/mcap-compat/unity_chunked_all_indexes.mcap
Recording Evidence: D:/BaiduSyncdisk/Obsidian Vault/Websocket/00 Inbox/build/mcap-compat/unity_chunked_all_indexes.schema
Recorded FoxRun Hash: (missing)
Current FoxRun Hash: 02f67b8128249d53032fc5cf3ed713bcb23f82049e99374d9ad1f5045f1f8c1c
Status: Missing Evidence
Warning: Recording sidecar is missing.
```

Observed Unity replay state:

- `MCAP Replay` is enabled and running.
- `Disable Live Publishers` is active.
- Console records `[Foxglove] Disabled 4 live publisher(s)`.
- Console records `[Foxglove] Server started on ws://127.0.0.1:8765`.
- Console warning records `Recorded MCAP does not contain FoxRun schema metadata; replay will continue without schema hash enforcement`.
- Cube is visible in Play Mode.

Result: PASS for D indexed Unity-authored compatibility fixture.

The missing `.schema` sidecar is expected for generated compatibility fixtures under `build/mcap-compat`; it is a schema-evidence/preflight limitation, not a local MCAP reader/replay failure. The D criterion for this fixture is that Unity accepts the indexed chunked file and reaches Play Mode without summary/index/metadata/attachment, chunk CRC, or replay hard errors.

### `unity_summaryless_or_direct_fixture.mcap`

Replay target:

- `D:/BaiduSyncdisk/Obsidian Vault/Websocket/00 Inbox/build/mcap-compat/unity_summaryless_or_direct_fixture.mcap`

Preflight result:

```text
Replay: D:/BaiduSyncdisk/Obsidian Vault/Websocket/00 Inbox/build/mcap-compat/unity_summaryless_or_direct_fixture.mcap
Recording Evidence: D:/BaiduSyncdisk/Obsidian Vault/Websocket/00 Inbox/build/mcap-compat/unity_summaryless_or_direct_fixture.schema
Recorded FoxRun Hash: (missing)
Current FoxRun Hash: 02f67b8128249d53032fc5cf3ed713bcb23f82049e99374d9ad1f5045f1f8c1c
Status: Missing Evidence
Warning: Recording sidecar is missing.
```

Observed Unity reader/replay state:

- `Analyze Replay File` accepted the file.
- Reader summary reports `Size: 250 bytes`.
- Reader summary reports `Channels: 1`.
- Reader summary reports `Chunks: 0`.
- Reader summary reports `Messages: 2`.
- Reader summary reports `Raw Time Range: 10 - 20 ns`.
- Reader summary reports `Topic Preview: /phase120/direct`.
- `MCAP Replay` is enabled and Play Mode starts with the fixture selected.
- Console records `[Foxglove] Disabled 4 live publisher(s)`.
- Console records `[Foxglove] Server started on ws://127.0.0.1:8765`.
- Console warning records `Recorded MCAP does not contain FoxRun schema metadata; replay will continue without schema hash enforcement`.

Result: PASS for D summaryless/direct Unity-authored compatibility fixture.

The missing `.schema` sidecar is expected for generated compatibility fixtures under `build/mcap-compat`; it is not a failure for the direct-message compatibility gate. The important acceptance point is that Unity's MCAP reader accepts a zero-chunk, no-summary/direct-message fixture and reports its channel/message/time/topic summary without a hard `No summary section`, chunk index, CRC, or replay exception.

## Foxglove Desktop Local File Evidence

### Unity recording

Foxglove Desktop opened:

- `phase116_120_manual_20260523_155220.mcap`

Observed Foxglove Desktop state:

- Topic list is populated with Unity recording topics including `/scene`, `/tf`, `/unity/camera`, `/debug/position`, `/debug/position2`, `/debug/health`, and debug 115F topics.
- Timeline is populated and positioned around `2026-05-23T15:52:55.005+02:00`.
- 3D panels show the Cube and grid.
- `/unity/camera` panel displays the Unity camera image.
- `/tf` raw message panel shows `parent_frame_id:"unity_world"` and `child_frame_id:"Cube"`.
- Plot panel shows replayed transform-series values.
- Raw message panel for `/debug/position` is populated.

Result: PASS for E Unity-authored recording open in Foxglove Desktop.

### `unity_chunked_all_indexes.mcap`

Foxglove Desktop opened:

- `D:/BaiduSyncdisk/Obsidian Vault/Websocket/00 Inbox/build/mcap-compat/unity_chunked_all_indexes.mcap`

Observed Foxglove Desktop state:

- Topic list is populated with `/phase120/a` and `/phase120/b`.
- Metadata, Attachments, and Source tabs are visible for the opened MCAP file.
- Timeline opens at the fixture time range around `1970-01-01T01:00:00.000+01:00`.
- Topic Graph panel is populated for the fixture topics.
- 3D/raw/publish panels using the full demo layout show expected missing-data messages when they point at topics not present in this small fixture; this is a layout/topic selection mismatch, not an MCAP file-open failure.

Result: PASS for E indexed compatibility fixture open in Foxglove Desktop.

Foxglove Desktop local-file open result: PASS for selected local MCAP compatibility workflows.

The original `foxglove-desktop-manual-open` limitation in `build/mcap-compat/phase120-report.json` is closed for the manually checked Unity-authored recording and Unity-authored indexed compatibility fixture. This does not claim production Remote Data Loader, Remote Access Gateway, or cloud-backed data loading.

## Phase 119 Remote Boundary Negative Confirmation

Code inspection result: PASS.

Inspector and component surface:

- Search scope: `Packages/dev.unity2foxglove.sdk/Runtime/Components`, `Packages/dev.unity2foxglove.sdk/Editor/Manager`, and `Packages/dev.unity2foxglove.sdk/Editor/Ros2Bridge`.
- No `RemoteMcap*`, production `Remote Data Loader`, `Remote Access Gateway`, cloud cache, Kubernetes, Helm, organization auth, device-token, or remote MCAP server fields are exposed through `FoxgloveManager` or its custom Inspector.
- Matching search hits in the Manager/Inspector surface are the existing local WebSocket shared-token/auth labels, not Phase 119 remote data-source UI.

Remote prototype boundary:

- `Runtime/IO/Mcap/Remote/RemoteMcapDataSourcePrototype.cs` is explicitly a local-file prototype for Remote Data Loader style manifest/data behavior.
- The prototype is constructed around one local MCAP path/source id, returns manifest/data DTOs, supports bearer-token denial, rejects unsupported multi-source requests, and does not start an HTTP server or expose a hosted endpoint.
- `Runtime/IO/Mcap/Remote/RemoteMcapModels.cs` and `RemoteMcapManifestMapper.cs` define DTOs and mapping only; they do not add Unity Inspector fields, cloud credentials, gateway configuration, or deployment plumbing.

Boundary documentation and validation:

- `Developer/MCAP Remote Data Source Boundary.md` states Phase 119 is a local prototype boundary, not a production hosted feature.
- The same note explicitly excludes production Foxglove Remote Data Loader deployment, Kubernetes/Helm/cache bucket/cloud object storage/range serving, OAuth/device-token/organization permission model/credential storage, Remote Access Gateway, and multi-file timeline merge.
- `Tests/Runtime/Phase119Validation.cs` validates the boundary note, DTO surface, local manifest mapping, authorization denial without data URL leakage, unsupported multi-source handling, and exact-byte local reader round trip.

Result: PASS for F remote-boundary non-overclaim.

The absence of a visible Inspector difference is expected: Phase 119 intentionally does not add production remote UI or runtime deployment behavior.

## Verification Commands

```powershell
dotnet run --project .\build\manual-mcap-validate\ManualMcapValidate.csproj -- "D:\BaiduSyncdisk\Obsidian Vault\Websocket\00 Inbox\Unity2Foxglove\Recordings"
```

Key output:

```text
FILE|phase116_120_manual_20260523_155220.mcap
  length=504457 lastWrite=2026-05-23T15:53:15 exclusiveOpen=True
  schemas=4 channels=14 chunks=5 metadataIndexes=35 attachmentIndexes=0
  statisticsMessages=2873 chunkMessages=2873 readMessages=2873 payloadBytes=4299544
  compressions=zstd chunkCrcOk=True
  schemaDirExists=True

FILE|phase116_120_manual_20260523_155842.mcap
  length=7286090 lastWrite=2026-05-23T16:00:09 exclusiveOpen=True
  schemas=4 channels=14 chunks=7 metadataIndexes=41 attachmentIndexes=0
  statisticsMessages=4884 chunkMessages=4884 readMessages=4884 payloadBytes=6926263
  compressions=none chunkCrcOk=True
  schemaDirExists=True
```

Schema evidence spot checks:

```powershell
Get-Content .\Unity2Foxglove\Recordings\phase116_120_manual_20260523_155220.schema\schema-evidence.json
Get-Content .\Unity2Foxglove\Recordings\phase116_120_manual_20260523_155842.schema\schema-evidence.json
```

Both files report:

```text
identityMode: Strict
complete: true
warnings: []
```

Unity log scan:

```powershell
$log = Join-Path $env:LOCALAPPDATA "Unity\Editor\Editor.log"
Select-String -Path $log -Pattern "phase116_120_manual|McapRecorder|MCAP Recording|MCAP|Mcap|CRC|summary|Exception|InvalidDataException|NullReferenceException|IOException|UnauthorizedAccessException"
```

No MCAP recording, CRC, summary, or close/write failure was found for the validated recordings. Unity cloud DNS/curl messages in the same Editor session are unrelated to MCAP recording.

## One-Time Manual Acceptance Flow

### A. Recording

1. Open `D:/BaiduSyncdisk/Obsidian Vault/Websocket/00 Inbox/Unity2Foxglove`.
2. Open `Unity2Foxglove/Assets/Scenes/SampleScene.unity`, or the full demo sample scene.
3. Clear Unity Console and confirm no compile errors.
4. Select the `FoxgloveManager` GameObject.
5. Enable `MCAP Recording`.
6. Set recording prefix to `phase116_120_manual`.
7. Set recording directory to `Unity2Foxglove/Recordings`.
8. Record once with `Recording Compression: Zstd`.
9. Record once with `Recording Compression: None`.
10. Stop Play Mode after visible scene/topic activity.

Pass criteria:

- At least one usable Zstd `.mcap`.
- At least one usable None `.mcap`.
- Recording stop closes the file.
- Matching `.schema/` sidecar exists when schema evidence is enabled.
- Unity Console has no recording crash, CRC, or summary-write errors.

### B. Replay Preflight

1. Stop Play Mode.
2. Disable `MCAP Recording`.
3. Enable `MCAP Replay`.
4. Set `Replay File Path` to one of the validated recordings.
5. Set `Replay Auto Play` on.
6. Set `Disable Live Publishers` on.
7. Set `Schema Evidence Identity Mode` to `Strict`.
8. Use the Inspector preflight controls:
   - `Use Latest Recording`
   - `Compare With Current`
   - `Copy Identity Summary`
   - `Open Recording Evidence`

Pass criteria:

- Recorded FoxRun hash and current FoxRun hash are visible.
- Expected status is `Match`.
- `Open Recording Evidence` opens the paired `.schema/` directory.
- `Copy Identity Summary` produces text suitable for the acceptance log.
- Protobuf/ROS2 aggregate hash is not treated as a replay blocker.

### C. Unity Replay

1. Keep replay enabled and recording disabled.
2. Enter Play Mode.
3. Observe the recorded scene state and topic-driven objects.
4. If replay controls are available, test pause, seek to middle, resume, seek near end, stop, and play again.

Pass criteria:

- Unity opens the recording through replay.
- Scene state reproduces recorded movement/state.
- Paused seek updates to latest-at state quickly.
- Live publishers and replay do not mix.
- Console has no file-open, schema mismatch block, chunk CRC, or summary parse error.

### D. Compatibility Fixtures In Unity

Generate fixtures first:

```powershell
dotnet run --project .\Packages\dev.unity2foxglove.sdk\Tests\Runtime\FoxgloveSdk.Tests.csproj -- --phase120-official
```

Then replay these files from Unity:

- `build/mcap-compat/unity_chunked_all_indexes.mcap`
- `build/mcap-compat/unity_summaryless_or_direct_fixture.mcap`
- `build/mcap-compat/official_python_chunked_zstd.mcap`
- `build/mcap-compat/official_python_unchunked_no_summary.mcap`

Pass criteria:

- All four files can be selected as `Replay File Path` and enter Play Mode.
- Indexed fixture has no summary/index/attachment/metadata read error.
- Summary-less/direct fixture has no hard failure for missing summary/index.
- Official Python fixtures are accepted by the local reader/replay path.

### E. Foxglove Desktop Open

1. Open Foxglove Desktop.
2. Use `Open local file`.
3. Open a validated Unity recording.
4. Confirm topic list, schema list, timeline, raw messages, and at least one scene/TF/camera visualization.
5. Open `build/mcap-compat/unity_chunked_all_indexes.mcap`.
6. Repeat topic/schema/timeline/raw-message checks.

Pass criteria:

- Foxglove Desktop opens the Unity-authored `.mcap`.
- Foxglove Desktop opens the indexed compatibility fixture.
- Topics, schemas, timeline, and raw messages are visible.
- Metadata/attachment indexes do not prevent opening.

If this step passes, Phase 120 can be manually upgraded from `PASS WITH NOTED LIMITATIONS` to public `PASS` evidence for selected local MCAP compatibility workflows.

### F. Phase 119 Remote Boundary Non-Overclaim

1. Confirm Unity Inspector does not expose production Remote Data Loader server controls.
2. Confirm there is no cloud cache, Kubernetes/Helm/deployment, organization auth/device token, or Remote Access Gateway UI.
3. Review:
   - `Developer/MCAP Remote Data Source Boundary.md`
   - `Developer/MCAP Official Compatibility Gate.md`

Pass criteria:

- Unity side does not expose production remote MCAP hosting.
- Docs separate static/direct MCAP URL, Remote Data Loader style backend, and Remote Access Gateway.
- Phase 119 remains a local prototype/mock boundary and does not overclaim production remote support.

## Final Verdict Template

```text
116-120 Manual Acceptance Result: PASS / PASS WITH LIMITATIONS / BLOCKED

Unity project:
Scene:
Branch:
Commit:
Unity version:
Foxglove Desktop version:
Recorded MCAP:
Schema sidecar:

A Recording:
B Replay Preflight:
C Unity Replay:
D Compatibility Fixtures in Unity:
E Foxglove Desktop Open:
F Remote Boundary Non-Overclaim:

Final verdict:
- If E passed: PASS for selected local MCAP compatibility workflows.
- If E skipped: PASS WITH NOTED LIMITATIONS.
- If any core Unity replay/open step failed: BLOCKED.

Notes:
```
