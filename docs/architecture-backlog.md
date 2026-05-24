# Architecture Backlog

This backlog records architecture candidates found by the Phase 126 coupling and file-size review. Items here are not implementation promises; they are places to revisit when a future phase touches the same subsystem.

## Phase 126 Baseline

Generate the current report with:

```bash
python Scripts/architecture/analyze_coupling.py --format text --output build/architecture/phase126-coupling-report.txt
```

Current high-value decisions:

- `FoxgloveManagerEditor.cs`: split `FoxglovePublisherBaseEditor` into `FoxglovePublisherBaseEditor.cs`; further Inspector drawer splits should wait until that file is touched again.
- `Program.cs`: default validation now runs from `PhaseValidationRegistry`; remaining legacy manual command handlers can be split later if they keep growing.
- `McapReader.cs`: defer chunk scanning and record decode helper extraction until the next MCAP reader behavior change.
- `MediaFoundationH264EncoderSidecar.cs`: defer native declarations and GUID/constants extraction until the Windows-native encoder path changes again.
- `FoxgloveRos2MsgSchemaCatalog.cs`: generated/catalog-like size is expected; do not split purely on line count.

Refactor rule: split only when the new file has a stable responsibility and reduces coupling or review cost. Do not move code only to lower a number in the report.
