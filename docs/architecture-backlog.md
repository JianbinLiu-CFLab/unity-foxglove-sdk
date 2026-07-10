# Architecture Backlog

This is an internal engineering decision record for architecture candidates found by the Phase 126 coupling and file-size review. It records both future work and deliberate no-op decisions so a completed or intentionally cohesive module is not repeatedly rediscovered as technical debt.

Items here are not implementation promises. A candidate is reopened only when its stated trigger occurs or fresh evidence shows a real ownership, coupling, or review-cost problem.

## Reconciliation Status (2026-07-10)

The original five high-value candidates are mostly resolved: three have landed as responsibility-based extractions, one is a deliberate no-op, and one remains a low-priority observation. In other words, four of five entries are closed; `Program.cs` is the only active backlog item.

| Candidate | Status | Current disposition and evidence | Revisit trigger |
| --- | --- | --- | --- |
| `Editor/Manager/FoxgloveManagerEditor.cs` | Completed | `FoxglovePublisherBaseEditor` is separate, and the manager Inspector is partitioned into `FoxgloveManagerEditor.*.cs` partials for MCAP, publish data, ROS2 bridge, FoxServices, diagnostics, security, and shared helpers. Phase 137E established the split; Phase 174-004 completed the later section-level partition. | Do not reopen for line count alone. Revisit only when a new Inspector section has a distinct state/lifecycle owner. |
| `Tests/Runtime/Program.cs` | Deferred, low priority | Default and targeted validation selection are owned by `PhaseValidationRegistry`. `Program.cs` still owns a bounded set of legacy manual smoke/generation modes and their command-line parsing. That coupling is understood and currently acceptable. | Split a manual-tool dispatcher only if new manual modes add shared option parsing or materially obscure the validation-runner path. |
| `Runtime/IO/Mcap/Reader/McapReader.cs` | Completed | Record decoding, chunk reading, and summary construction now have dedicated reader helpers: `McapRecordDecoder`, `McapChunkReader`, and `McapSummaryBuilder`. `McapReader` remains the compatibility and stream-facing facade. | Revisit only for a new reader behavior that creates another independent state owner; preserve the facade API. |
| `Runtime/Schemas/Proto/Video/MediaFoundationH264EncoderSidecar.cs` | Completed | Phase 174-011 moved Windows Media Foundation COM interop declarations into `MediaFoundationH264EncoderSidecar.ComInterop.cs`, leaving encoder lifecycle and packet flow in the sidecar facade. | Treat encoder lifecycle, COM apartment rules, and packet ordering as one protected boundary; do not introduce a sidecar base class merely to share code. |
| `Runtime/Schemas/Ros2Msg/FoxgloveRos2MsgSchemaCatalog.cs` | Closed, intentional no-op | This is a coherent generated catalog, with one generator as its source of truth. Its size is expected and splitting it would make generation and review less direct without reducing a real runtime boundary. | Revisit only if the generator gains independently consumed catalog partitions; otherwise regenerate the single catalog. |

## Phase 126 Baseline

Generate the current report with:

```bash
python Scripts/architecture/analyze_coupling.py --format text --output build/architecture/phase126-coupling-report.txt
```

## Operating Rule

Split only when the new file has a stable responsibility and reduces coupling or review cost. Do not move code only to lower a number in the report. When a candidate is closed as cohesive or generated, record that decision here instead of reopening it on the next size scan.
