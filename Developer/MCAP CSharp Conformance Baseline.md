# MCAP CSharp Conformance Baseline

## Final Verdict

PASS WITH MEASURED BASELINE.

Phase 121 adds a C# runner bridge for the official `foxglove/mcap` conformance harness shape. Phase 122 adds measured writer option parity for direct/no-chunk, no-padding writer variants. Phase 123 routes reader evidence through explicit streaming and strict indexed paths so product fallback behavior is measured separately from official-style indexed requirements. This evidence does not claim full official MCAP conformance or replacement of the upstream SDKs.

## v1.9.1 Baseline

The starting point is the v1.9.1 local MCAP compatibility baseline:

- local `McapDataLoader` initialization, iterator, and backfill facade exist;
- `McapIndexedReader.OpenRead(string)` remains part of the public surface;
- direct/no-summary MCAP files are accepted through bounded sequential fallback;
- latest-at backfill uses `ReadLatestBefore` instead of unbounded `0..T` materialization;
- indexed and sequential CRC mismatch behavior hard-fails consistently;
- Unity/Foxglove manual acceptance confirmed selected Unity-authored and official compatibility fixture workflows.

Phase 121 must not downgrade those claims unless the generated conformance report identifies a regression in those already accepted workflows.

## Official Harness

Observed local upstream clone:

```text
third-party/mcap
c3cab6bd3ce79199e362766daec3a4689f3a0335
```

The wrapper treats `third-party/mcap` as read-only. C# runner overlays are copied into `build/mcap-conformance/mcap-overlay`, and the report is written to:

```text
build/mcap-conformance/phase121-conformance-report.json
```

The report records:

- `externalToolingStatus`;
- official MCAP path and commit;
- Node and Yarn versions when available;
- generated variant count;
- streamed reader pass/fail/skip counts;
- indexed reader pass/fail/skip counts;
- writer pass/fail/skip counts;
- failure and skip reasons;
- final `verdict`.

## 2026-05-23 Manual Acceptance A

Result: PASS for measured baseline evidence, with explicit non-claim of full official MCAP conformance.

User-confirmed Unity checks:

- Unity project opened in Unity 6.3 LTS (`6000.3.14f1`) on `SampleScene`.
- Unity Console showed `0` errors after refresh/reopen.
- No conformance-output assembly import errors remained after keeping MCAP conformance build outputs outside `Packages/`.

Repository report check:

```text
D:/BaiduSyncdisk/Obsidian Vault/Websocket/00 Inbox/build/mcap-conformance/phase121-conformance-report.json
```

The report exists, is readable JSON, and records:

| Field | Value |
|---|---|
| `externalToolingStatus` | `available` |
| `generatedVariantCount` | `416` |
| `verdict` | `MEASURED BASELINE WITH FAILURES` |
| `officialMcapCommit` | `c3cab6bd3ce79199e362766daec3a4689f3a0335` |

Acceptance interpretation:

- The external official MCAP tooling was available locally.
- The generated conformance variant set was measured.
- The result is intentionally recorded as `MEASURED BASELINE WITH FAILURES`.
- This is not a full official conformance PASS and must not be described as complete official MCAP conformance.
- Current acceptable claim: C# MCAP conformance bridge and report generation are operational, and the report establishes a measured baseline for remaining reader/writer parity work.

## 2026-05-23 Manual Acceptance B

Result: PASS for the Unity recording main path used by writer-option parity.

Important setup note: schema evidence sidecars for this gate require `Schema Evidence Identity Mode: Strict`. Earlier non-Strict recordings can still be readable `.mcap` files, but they are not accepted for this B gate because they do not produce the required paired `.schema/` directory.

User-confirmed Unity setup:

- `FoxgloveManager` selected in `SampleScene`.
- `Enable Recording`: on.
- `Enable Replay`: off.
- Cube/scene/tf/camera data active during Play Mode.
- Unity Console showed `0` errors after the Strict recording pass.

Validated Strict recordings under:

```text
D:/BaiduSyncdisk/Obsidian Vault/Websocket/00 Inbox/Unity2Foxglove/Recordings
```

| File | Compression | Size | Schemas | Channels | Chunks | Messages | Payload Bytes | Chunk CRC | Closed | Sidecar |
|---|---|---:|---:|---:|---:|---:|---:|---|---|---|
| `phase121_125_chunked_20260523_212338.mcap` | `none` | 4664957 | 4 | 14 | 47 | 2854 | 4382108 | PASS | PASS | PASS |
| `phase121_125_zstd_20260523_212201.mcap` | `zstd` | 963294 | 4 | 14 | 42 | 2589 | 3935124 | PASS | PASS | PASS |

Reader validation details:

- Both files were opened by `McapIndexedReader.OpenRead`.
- `summary.Statistics.MessageCount`, decoded chunk message count, and `ReadMessages(MaxMessages=0)` count matched for both files.
- Both files were opened with `FileShare.None` in read/write mode after Play stopped, proving the recorder closed its file handle.
- Both files contained expected scene/debug topics including `/scene`, `/tf`, `/unity/camera`, `/debug/position`, `/debug/position2`, `/debug/health`, and the 115F debug topics.

Sidecar validation:

| Sidecar | `mcapFile` | `identityMode` | `complete` | `warnings` | FoxRun Hash |
|---|---|---|---|---|---|
| `phase121_125_chunked_20260523_212338.schema` | `phase121_125_chunked_20260523_212338.mcap` | `Strict` | `true` | `[]` | `02f67b8128249d53032fc5cf3ed713bcb23f82049e99374d9ad1f5045f1f8c1c` |
| `phase121_125_zstd_20260523_212201.schema` | `phase121_125_zstd_20260523_212201.mcap` | `Strict` | `true` | `[]` | `02f67b8128249d53032fc5cf3ed713bcb23f82049e99374d9ad1f5045f1f8c1c` |

Acceptance interpretation:

- The normal Unity recording path produced readable chunked/indexed MCAP files for both `None` and `Zstd` compression.
- Recording stop closed the file handle cleanly.
- Strict schema evidence directories paired with the `.mcap` filenames by prefix/timestamp.
- No file-level CRC, summary, or reader failures were observed.
- This validates the Unity-facing writer-option default/main path; it does not convert the official conformance report into a full official writer conformance PASS.

## 2026-05-23 Manual Acceptance C

Result: PASS for direct-message and summaryless/local-fallback reading through Unity Inspector and runtime reader verification.

Validated compatibility fixtures:

```text
D:/BaiduSyncdisk/Obsidian Vault/Websocket/00 Inbox/build/mcap-compat/unity_chunked_all_indexes.mcap
D:/BaiduSyncdisk/Obsidian Vault/Websocket/00 Inbox/build/mcap-compat/unity_summaryless_or_direct_fixture.mcap
```

Unity Inspector observations:

| File | Unity Summary | Topic Preview | Console Result |
|---|---|---|---|
| `unity_chunked_all_indexes.mcap` | `Size: 1,481 bytes`; `Channels: 2`; `Chunks: 2`; `Messages: 3`; `Raw Time Range: 10 - 40 ns`; `Metadata Indexes: 1`; `Attachment Indexes: 1` | `/phase120/a`, `/phase120/b` | Play Mode entered; server started; no console errors. |
| `unity_summaryless_or_direct_fixture.mcap` | `Size: 265 bytes`; `Channels: 1`; `Chunks: 0`; `Messages: 2`; `Raw Time Range: 10 - 20 ns`; `Metadata Indexes: 0`; `Attachment Indexes: 0` | `/phase120/direct` | Play Mode entered; server started; no Unity console errors. |

Both fixtures show `Status: Missing Evidence` in replay identity preflight because the generated compatibility fixtures do not ship paired FoxRun `.schema/` sidecars. This is expected for this C gate and is not a reader/replay failure. The accepted warning text is:

```text
Recorded MCAP does not contain FoxRun schema metadata; replay will continue without schema hash enforcement.
```

Local reader verification:

| File | Schemas | Channels | Chunks | Metadata Indexes | Attachment Indexes | Messages Read | JSON Schema Root | Compression | CRC |
|---|---:|---:|---:|---:|---:|---:|---|---|---|
| `unity_chunked_all_indexes.mcap` | 2 | 2 | 2 | 1 | 1 | 3 | `{"type":"object"}` | `zstd` | PASS |
| `unity_summaryless_or_direct_fixture.mcap` | 1 | 1 | 0 | 0 | 0 | 2 | `{"type":"object"}` | direct/no-chunk | PASS |

Post-manual correction:

- Foxglove Desktop manual open identified that the original `unity_summaryless_or_direct_fixture.mcap` used `jsonschema` content `{}`, which Desktop rejected with `Expected ".type": "object"` on `/phase120/direct`.
- The fixture generator now writes `{"type":"object"}` for the direct fixture, matching the chunked fixture and the official Python direct fixture.
- Phase 120 validation now includes `120-F7`, which asserts every `message_encoding=json` + `schema_encoding=jsonschema` compatibility fixture has a Foxglove Desktop-compatible root object schema.
- The regenerated `unity_summaryless_or_direct_fixture.mcap` is `265` bytes and was verified by local reader and official Python reader to contain two `/phase120/direct` messages at log times `10` and `20`.

Foxglove Desktop follow-up:

- The regenerated direct fixture opens and renders `/phase120/direct`.
- The remaining Desktop Problems entry is the expected warning `This file is unindexed. Unindexed files may have degraded performance.`
- That warning is accepted for this fixture because the fixture intentionally has no summary/index section (`Chunks: 0`, `Metadata Indexes: 0`, `Attachment Indexes: 0`) to exercise the direct-message/summaryless fallback path.
- Any Desktop error such as `Expected ".type": "object"` remains a failure; the unindexed warning alone is not a failure for this gate.

Automated validation:

```powershell
dotnet run --no-restore --project .\Packages\dev.unity2foxglove.sdk\Tests\Runtime\FoxgloveSdk.Tests.csproj -- --phase123
dotnet run --no-restore --project .\Packages\dev.unity2foxglove.sdk\Tests\Runtime\FoxgloveSdk.Tests.csproj -- --phase122
```

Results:

- `--phase123`: 28 checks passed, including non-seeking direct-message stream reads, summaryless metadata/attachment fallback, product linear fallback caching, strict indexed mode, and DataEnd CRC checks.
- `--phase122`: 30 checks passed, including default chunked writer layout, direct-message writer options, data CRC independence, and measured conformance writer flag mapping.
- `--phase120`: 51 checks passed after adding the JSON schema root guard.
- `--phase120-official`: 66 checks passed after adding the JSON schema root guard.

Acceptance interpretation:

- Unity Inspector can analyze and preview indexed chunked fixtures with `/phase120/a` and `/phase120/b`.
- Unity Inspector can analyze and preview zero-chunk direct/summaryless fixtures with `/phase120/direct`.
- Unity replay enters Play Mode with live publishers disabled and without hard reader, summary, chunk-index, or CRC errors.
- The missing schema evidence warning is expected for generated compatibility fixtures and does not block direct/summaryless reader acceptance.
- Foxglove Desktop visual open is accepted for the regenerated direct fixture with the expected unindexed-file warning.
- This validates the 122/123 local direct-message and summaryless reading path; it does not claim full official conformance for all generated variants.

## 2026-05-23 Manual Acceptance D

Result: PASS for replay query, latest-at, and seek behavior on a real Unity-authored recording.

Replay target:

```text
D:/BaiduSyncdisk/Obsidian Vault/Websocket/00 Inbox/Unity2Foxglove/Recordings/phase121_125_chunked_20260523_212338.mcap
```

Unity replay/preflight observations:

- `MCAP Replay` enabled.
- `Replay Auto Play` enabled.
- `Disable Live Publishers` enabled.
- Replay identity preflight used `phase121_125_chunked_20260523_212338.mcap`.
- Recording evidence used `phase121_125_chunked_20260523_212338.schema`.
- Recorded FoxRun hash matched current FoxRun hash:

```text
02f67b8128249d53032fc5cf3ed713bcb23f82049e99374d9ad1f5045f1f8c1c
```

- Preflight status: `Match`.
- Inspector MCAP summary reported `Size: 4,664,957 bytes`, `Channels: 14`, `Chunks: 47`, `Messages: 2,854`, `Metadata Indexes: 35`, `Attachment Indexes: 0`.
- Topic preview included `/unity/client_log`, `/debug/overlay_smoke`, `/scene`, `/tf`, and debug 115F topics.

Unity replay observations:

- Play Mode entered with live publishers disabled.
- Cube pose visibly changed under replay.
- Console showed no hard errors during replay/seek.
- Observed console messages included expected replay/runtime informational lines such as server start and pose ownership arbitration. The known `FOXRUN007` manual-probe warning is not a replay query failure.

Foxglove seek observations:

- Connected to `ws://127.0.0.1:8765 Unity Foxglove SDK`.
- Timeline seeked to the recording around `2026-05-23T21:24:05.834+02:00`.
- `Problems` panel reported `No problems found`.
- 3D panels displayed the Cube at the seeked pose.
- `/tf` raw panel showed `parent_frame_id: "unity_world"` and `child_frame_id: "Cube"` at the seeked timestamp.
- Plot panel populated transform series around the seek point.
- Raw debug position panel showed seek-aligned position data.

Local reader query verification:

```text
FILE|phase121_125_chunked_20260523_212338.mcap
  statisticsMessages=2854 chunkMessages=2854 readMessages=2854
  querySeekNs=1779564246921012200
  queryWindow=1779564244436576050..1779564249405448350
  windowMessages=335
  queryDescendingTop5=5 descendingSorted=True
  latestAtTfScene=2
  latestChannels=3@1779564246875731000,4@1779564246875913700
```

Acceptance interpretation:

- Time-window message queries return data around the seek point.
- Descending query order is honored.
- `ReadLatestBefore` returns latest-at pose candidates for both `/tf` and `/scene` channels near the seek point.
- Unity replay and Foxglove visualization show the same practical behavior: seeked timeline state updates visible scene/TF/raw data without hard reader errors.
- This validates the Phase 123 replay query/seek surface for the selected Unity-authored recording.

## 2026-05-23 Manual Acceptance E

Result: PASS for decoded DataLoader iteration and structured decode-failure behavior on a real Unity-authored recording.

Replay/data source:

```text
D:/BaiduSyncdisk/Obsidian Vault/Websocket/00 Inbox/Unity2Foxglove/Recordings/phase121_125_chunked_20260523_212338.mcap
```

Unity runner:

```text
D:/BaiduSyncdisk/Obsidian Vault/Websocket/00 Inbox/Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase124DecodedDataLoaderAcceptance.cs
```

The runner was attached to the `Foxglove` GameObject as `Phase 124 Decoded DataLoader Acceptance` and executed through the Inspector button `Run Decoded DataLoader Acceptance`.

Unity Console result:

```text
[Phase124DecodedDataLoader] RESULT pass=True rawCount=6 decodedCount=6 sequencesMatch=True sawJson=True sawProtobuf=True unsupportedKind=Unsupported malformedKind=Failed
```

Observed decoded checks:

| Check | Result |
|---|---|
| Raw iterator vs decoded iterator count | `rawCount=6`, `decodedCount=6` |
| Raw/decoded timing and byte preservation | `sequencesMatch=True` |
| JSON payload decoding | `sawJson=True` |
| Foxglove protobuf payload decoding | `sawProtobuf=True` |
| Unsupported encoding behavior | `unsupportedKind=Unsupported` with structured problem |
| Malformed JSON behavior | `malformedKind=Failed` with structured problem |

Unity Console showed informational acceptance logs only for this run, with no warnings or errors.

Acceptance interpretation:

- `CreateIterator(...)` and `CreateDecodedIterator(...)` returned aligned raw/decoded message sequences for selected real recording topics.
- `TryDecodeMessage(...)` decoded selected real messages while preserving the raw message data.
- JSON and Foxglove protobuf topics from the real recording were both exercised.
- Unsupported message encoding produced a structured `Unsupported` result instead of throwing or dropping raw bytes.
- Malformed JSON produced a structured `Failed` result instead of crashing or dropping raw bytes.
- This validates the Phase 124 decoded DataLoader Unity-facing acceptance gate for the selected Unity-authored recording.

## 2026-05-23 Manual Acceptance F

Result: USER-CONFIRMED PASS for ROS2 CDR typed decode through the Unity one-click acceptance runner.

Generated MCAP:

```text
D:/BaiduSyncdisk/Obsidian Vault/Websocket/00 Inbox/Unity2Foxglove/Recordings/phase125_ros2_cdr_typed_manual_20260523_223600.mcap
```

Unity runner:

```text
D:/BaiduSyncdisk/Obsidian Vault/Websocket/00 Inbox/Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase125Ros2CdrTypedDecodeAcceptance.cs
```

The runner is attached as `Phase 125 ROS2 CDR Typed Decode Acceptance` and executed through the Inspector button `Run ROS2 CDR Typed Decode Acceptance`. The user confirmed this Unity acceptance step passed after running the one-click component.

Runner coverage:

| Check | Expected acceptance result |
|---|---|
| `foxglove_msgs/msg/FrameTransform` | `Ros2CdrTyped`, decoded value is a `Foxglove.FrameTransform`, raw bytes preserved |
| `foxglove_msgs/msg/SceneUpdate` | `Ros2CdrTyped`, decoded value is a `Foxglove.SceneUpdate`, raw bytes preserved |
| `foxglove_msgs/msg/PointCloud` | `Ros2CdrTyped`, decoded value is a `Foxglove.PointCloud`, raw bytes preserved |
| Unknown ROS2 CDR schema | Diagnostic fallback result, not a hard crash |
| Malformed ROS2 CDR payload | Structured failed/diagnostic result, not silent success |
| Throw decode policy | Explicit exception path is exercised |
| Diagnostic fallback failure | Structured `Failed` result includes fallback failure problem |

Automated validation:

```text
dotnet run --project .\Packages\dev.unity2foxglove.sdk\Tests\Runtime\FoxgloveSdk.Tests.csproj -- --phase125
```

Result:

```text
Phase 125: 118 checks passed.
```

Acceptance interpretation:

- Supported ROS2 CDR schemas typed-decode through the DataLoader path into the corresponding Foxglove protobuf message objects.
- Unknown ROS2 CDR schemas fall back to diagnostic decoding instead of being treated as successful typed payloads.
- Malformed payloads surface structured problems, and the throw policy path raises an explicit exception.
- The Unity manual run generated a real MCAP file and the user reported no Unity OOM, hang, or CDR alignment/header/sequence error during the acceptance run.

## Runner Scope

### C# streamed reader

The streamed runner invokes:

```text
dotnet Unity2Foxglove.McapConformance.dll read-streamed <mcap>
```

It serializes MCAP records into the official `{ "records": [...] }` shape, expands chunk contents, skips `MessageIndex`, and preserves summary/footer records.

As of Phase 123, the streamed runner first validates the file through the runtime non-seeking `McapStreamingReader` using official-style query semantics. The normalized official JSON remains produced by the conformance scanner so the output shape can preserve summary/footer records exactly.

Variants using official `pad` extra data are skipped until the reader intentionally supports those trailing bytes.

### C# indexed reader

The indexed runner invokes:

```text
dotnet Unity2Foxglove.McapConformance.dll read-indexed <mcap>
```

It verifies the file through `McapIndexedReader` and emits the official indexed result shape:

```text
schemas
channels
messages
statistics
```

Message-less variants are skipped, matching the upstream indexed-reader runner pattern. Direct/no-summary message variants are in scope through the bounded sequential fallback.

As of Phase 123, strict indexed conformance disables linear fallback. Product behavior still permits bounded linear fallback for Unity replay/data loading when indexes are missing, but that behavior is not counted as strict indexed support.

### C# writer

The writer runner maps official feature flags to `McapWriterOptions` and supports the measured direct/no-chunk, no-padding subset. Local byte checks confirmed direct `OneMessage`, `OneMetadata-mdx-st-sum`, and `OneAttachment-ax-st-sum` outputs match the official fixture bytes.

Chunked writer byte parity and extra record padding remain skipped. Unsupported writer variants are skips, not supported failures.

## How To Run

Normal static validation:

```powershell
dotnet run --project .\Packages\dev.unity2foxglove.sdk\Tests\Runtime\FoxgloveSdk.Tests.csproj -- --phase121
```

External official harness run:

```powershell
.\Scripts\mcap\conformance\run_phase121_conformance.ps1
```

Validation wrapper:

```powershell
dotnet run --project .\Packages\dev.unity2foxglove.sdk\Tests\Runtime\FoxgloveSdk.Tests.csproj -- --phase121-conformance
```

If Node, Yarn, or the official clone is missing, normal validation records an external-tooling skipped report instead of failing the local SDK.

## Deferred Work

Phase 122:

- completed writer option parity for the Unity recorder API surface;
- mapped official writer flags to supported C# writer controls;
- proved direct/no-chunk byte-identical output for selected official fixtures;
- left chunked byte-identical writer output and `pad` extra data as deferred work.

Phase 123:

- added a true non-seeking runtime streaming reader path;
- added reader query order, official exclusive end-time, fallback, and CRC validation options;
- separated strict indexed conformance from product linear fallback behavior;
- left explicit support for official `pad` extra data variants as deferred work.

## Non-Claims

This evidence does not claim:

- full official MCAP conformance;
- complete official MCAP SDK replacement;
- production Foxglove Remote Data Loader support;
- Remote Access Gateway support;
- support for skipped variants.
