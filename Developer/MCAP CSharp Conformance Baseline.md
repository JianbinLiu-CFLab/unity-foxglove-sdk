# MCAP CSharp Conformance Baseline

## Final Verdict

PASS WITH MEASURED BASELINE.

Phase 121 adds a C# runner bridge for the official `foxglove/mcap` conformance harness shape. It does not claim full official MCAP conformance, byte-identical writer parity, or replacement of the upstream SDKs.

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

## Runner Scope

### C# streamed reader

The streamed runner invokes:

```text
dotnet Unity2Foxglove.McapConformance.dll read-streamed <mcap>
```

It serializes MCAP records into the official `{ "records": [...] }` shape, expands chunk contents, skips `MessageIndex`, and preserves summary/footer records.

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

### C# writer

The writer runner is present but returns unsupported for all official variants in Phase 121. This is intentional. The current low-level `McapWriter` can emit MCAP records, but productized official writer option parity is Phase 122.

Unsupported writer variants are skips, not supported failures.

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

- productize writer option parity;
- map official writer flags to supported C# writer controls;
- decide whether byte-identical writer output is a public requirement.

Phase 123:

- true non-seeking streaming reader;
- query/order parity beyond the selected local reader workflows;
- explicit support decision for `pad` extra data variants.

## Non-Claims

This evidence does not claim:

- full official MCAP conformance;
- complete official MCAP SDK replacement;
- production Foxglove Remote Data Loader support;
- Remote Access Gateway support;
- support for skipped variants.
