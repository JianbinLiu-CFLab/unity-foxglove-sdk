# MCAP DataLoader Hardening Review Closure

Date: 2026-05-23

Branch: feature/phase120b-mcap-dataloader-hardening

Commit: branch tip

## Scope

Phase 120B closes the Phase 116-120 MCAP DataLoader review findings without expanding the Phase 120 compatibility verdict.

## Findings

| ID | Status | Closure |
|---|---|---|
| M1 | closed | DataLoader backfill now uses `McapIndexedReader.ReadLatestBefore` instead of materializing the full 0..T query range. |
| M2 | closed | Unindexed sequential fallback now uses explicit MaxMessages and MaxPayloadBytes limits and does not retain message payloads during inventory-only summary scans. |
| M3 | closed | CRC hard failure behavior is preserved and validated for indexed and sequential chunk paths. |
| L1 | closed | Remote prototype now caps legacy in-memory data responses and exposes a stream response for larger local files. |
| L2 | closed | Chunk schema/channel scanning decodes directly from the chunk buffer instead of allocating record-sized temporary arrays. |

## Follow-up Review Closure

| ID | Status | Closure |
|---|---|---|
| R1 | closed | Indexed latest-at backfill now derives its early-stop target from eligible chunk index channel offsets when available, instead of declared channel inventory count. If chunk index channel offsets are absent, it conservatively falls back to the declared/filter channel count. |
| R2 | accepted policy | The two-pass unindexed scan is intentional: summary construction performs an inventory-only scan that avoids retaining payloads, while the first message query performs a bounded payload-retaining sequential scan under MaxMessages and MaxPayloadBytes. |
| R3 | closed | `RemoteMcapDataStreamResponse` implements `IDisposable` and closes the owned stream when disposed. |
| R4 | closed | `DecodeSchema` and `DecodeChannel` offset overloads now enforce `contentLen` segment bounds for strings, prefixed bytes, maps, and exact record consumption. |

## Policies

CRC hard failure remains the policy. A corrupt chunk must not be silently skipped because that would produce incomplete replay/query output without a reliable data-quality signal.

Default sequential fallback limits:

- MaxMessages: 100000
- MaxPayloadBytes: 268435456

## Implementation Results

- `McapSequentialReadLimits.Default.MaxMessages`: 100000
- `McapSequentialReadLimits.Default.MaxPayloadBytes`: 268435456
- `RemoteMcapDataSourcePrototype.DefaultMaxInMemoryDataBytes`: 16777216
- `--phase120b`: 37 checks passed
- `--phase116`: 62 checks passed
- `--phase117`: 43 checks passed
- `--phase118`: 33 checks passed
- `--phase119`: 42 checks passed
- `--phase120`: 48 checks passed
- `--phase120-official`: 61 checks passed
- Performance quick: passed, wrote `build/performance/phase35_performance_quick_20260523-130655.json`

## Validation

```powershell
dotnet run --project .\Packages\dev.unity2foxglove.sdk\Tests\Runtime\FoxgloveSdk.Tests.csproj -- --phase120b
```
