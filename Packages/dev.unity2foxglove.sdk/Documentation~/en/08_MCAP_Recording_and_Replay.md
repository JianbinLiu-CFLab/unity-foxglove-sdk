## 1. Purpose

Use this page to save Unity/Foxglove data to `.mcap` files and replay recorded files inside Unity.

## 2. Workflow

You will enable recording, choose an output location, open the result in Foxglove, replay an MCAP file in Unity, and verify playback.

## 3. Record from Unity

1. Select the GameObject with **FoxgloveManager**.
2. Enable **MCAP Recording > Enable Recording**.
3. Choose a **Recording Prefix** such as `foxglove`.
4. Leave **Recording Directory** empty for the default `Recordings/` folder next to the Unity project, or set a custom folder.
5. Choose compression:
   - `None` for easiest debugging
   - `Lz4` for fast compression
   - `Zstd` for smaller files
6. Press **Play**.
7. Move the scene or interact with the demo.
8. Stop Play Mode to close the MCAP file.

The generated file name uses:

`<prefix>_<yyyyMMdd_HHmmss>.mcap`

## 4. Recorded Data

Depending on which features are active, recording can include:

- Published topics such as `/tf`, `/scene`, `/unity/camera`, and `/debug/...`
- Channel metadata such as coordinate mode
- Parameter snapshots and changes
- Service completion or failure metadata
- Client-published messages when client publish support is used

## 5. Open MCAP in Foxglove

1. Open Foxglove Desktop.
2. Choose **Open local file**.
3. Select the `.mcap` file.
4. Use the timeline controls to inspect messages.
5. Add panels for topics such as `/tf`, `/scene`, and `/unity/camera`.

If the file opens but topics are missing, verify recording was enabled before Play Mode started and that publishers were active.

## 6. Replay MCAP in Unity

1. Select the GameObject with **FoxgloveManager**.
2. Enable **MCAP Replay > Enable Replay**.
3. Set **Replay File Path** to the `.mcap` file.
4. Use **Replay Preflight > Use Latest Recording** when you want Unity to select the newest file from the recording directory and write it into **Replay File Path**.
5. Use **Compare With Current** before Play Mode to compare the recorded FoxRun hash from the recording's `.schema` sidecar with the current generated FoxRun hash.
6. Enable **Replay Auto Play** if you want playback to start automatically.
7. Leave **Disable Live Publishers** enabled for replay tests, so live and replayed topics do not overlap.
8. Press **Play**.

Unity should replay recorded messages and update replay adapters or forwarded Foxglove topics.

Paused seeking and scrubbing use two related paths:

- Unity scene objects receive a latest-at snapshot immediately, so replay adapters can follow the requested timeline even while playback is paused.
- Foxglove panels receive bounded chronological history after a short debounce window, so active dragging does not flood the WebSocket client with stale panel data.

This is scene reproduction and bounded panel history, not deterministic simulation replay.

## 7. Verify Replay

Use this checklist:

- Unity enters Play Mode without file errors.
- The replayed object follows the recorded motion.
- Foxglove can connect while replay is active.
- Recorded coordinate mode matches the active Unity coordinate mode, or Unity logs a clear warning.
- Seeking or pausing works if playback control is enabled.
- During paused scrubbing, the Unity scene updates promptly and Foxglove does not receive stale queued replay frames after seek reset.

## 8. Recording and Replay Should Not Run Together

Unity2Foxglove treats recording and replay as separate modes. If both are enabled on the same Manager, replay is disabled to avoid mixing live output and file playback.

## 9. FoxRun Schema Metadata

If generated FoxRun runtime schema info is present, MCAP recording writes a metadata record named `unity2foxglove.foxrun.schema`. Its `value` is compact JSON containing `globalManifestHash`, the FoxRun section `manifestHash`, manifest/generator versions, counts, and per-contract diagnostic hashes.

Unity replay reads this metadata after the MCAP file is loaded and before playback starts. If the recorded `globalManifestHash` does not match the current runtime `globalManifestHash`, replay is blocked with a short-hash mismatch diagnostic. In explicit replay mode, a confirmed mismatch fails closed: the Manager aborts startup and does not restore live publishers as a fallback. Missing recorded metadata, missing current schema info, or malformed recorded metadata only produces a warning so older MCAP files remain usable.

The SDK schema manifest aggregate under `Assets/Generated/Unity2Foxglove/` is separate release evidence. It records the FoxRun summary, protobuf registry, ROS2 `.msg` registry, and typed publisher catalog, but Unity replay does not use its aggregate hash, protobuf hash, or ROS2 hash as replay guard keys. Replay compatibility remains governed only by the FoxRun `globalManifestHash` recorded in MCAP metadata.

## 10. Schema Evidence Identity Modes

The **Schema Evidence** controls in `FoxgloveManager > MCAP Record & Replay` decide how strongly Unity uses the current evidence snapshot:

- `Off`: skip schema identity checks. This is the lightest default for demos and early debugging.
- `Warn`: report mismatches, but continue recording or replay. If live publishers stay enabled during replay, Foxglove may show mixed replay/live data.
- `Strict`: require complete evidence for recording sidecars and block replay when the recorded FoxRun `globalManifestHash` does not match the current one.

The current evidence root defaults to `Assets/Generated`. It contains `FoxRun/` and `Unity2Foxglove/` groups. When recording is enabled and identity mode is `Warn` or `Strict`, Unity writes a sidecar next to the MCAP:

```text
Recordings/session_20260521_135001.mcap
Recordings/session_20260521_135001.schema/
  schema-evidence.json
  FoxRun/
  Unity2Foxglove/
```

The **Replay Preflight** block can inspect a selected `.mcap` before Play Mode. It shows the recorded FoxRun hash, the current FoxRun hash, and a `Match`, `Mismatch`, or `Missing Evidence` status. **Open Recording Evidence** reveals the paired `.schema` directory, and **Copy Identity Summary** copies the comparison text for bug reports or acceptance notes.

Use the timestamp and folder name to keep the `.mcap` and `.schema` evidence bundle paired.

## 11. Local MCAP DataLoader v1

`McapDataLoader` is a local file-backed API for code that needs to inspect a Unity-created `.mcap` without starting replay. It wraps the indexed MCAP reader and exposes:

- `Initialize()` for channels, schemas, time range, metadata indexes, attachment indexes, message count, and diagnostics.
- `CreateIterator(query)` for raw message iteration filtered by topic, channel ID, and log-time range.
- `GetBackfill(query)` for the latest raw message per selected channel at or before a requested log time.

Messages remain raw serialized payload bytes. The DataLoader surfaces FoxRun schema metadata as diagnostics, including missing, malformed, matching, and mismatching metadata, but it does not replace the Phase 114 replay blocker. A confirmed FoxRun mismatch remains diagnostic-only for local DataLoader reads while strict replay may still block.

Phase 118 adds repeatable quick/full performance evidence for DataLoader initialize, raw iteration, topic/time-window filters, and backfill. The performance harness writes generated fixtures under `build/performance/fixtures/` and records DataLoader fixture/query fields in the JSON output under `build/performance/`. Treat those numbers as machine-local release evidence, not cross-machine guarantees.

This is not the official Foxglove data-loader host ABI. It does not add WASM bindings, remote data loading, HTTP range serving, Remote Access Gateway support, multi-file timeline merge, or decoded typed payload views.

## 12. Common Mistakes

| Symptom | Likely cause | Fix |
|---|---|---|
| No `.mcap` file appears | Recording was not enabled before Play Mode. | Enable recording, then start Play Mode again. |
| File exists but is tiny | No publishers were active. | Verify `/tf`, `/scene`, or `/unity/camera` exists in Foxglove while recording. |
| Replay looks mirrored | Coordinate mode mismatch. | Match `FoxgloveManager > Coordinate Mode` to the recording. |
| Replay and live data both move the object | Live publishers were not disabled. | Enable **Disable Live Publishers** during replay. |
| Foxglove cannot open the file | File was not closed cleanly. | Stop Play Mode normally and try a new recording. |
| Replay is blocked by a FoxRun schema mismatch | The MCAP was recorded with a different generated FoxRun contract. | Regenerate the current FoxRun manifest/schema info or replay with the matching project revision. |
