# MCAP Official Compatibility Gate

## Final Verdict

`PASS WITH NOTED LIMITATIONS`

This verdict is intentionally not `PASS` because Foxglove Desktop manual visual opening is recorded as deferred in this automated phase. Core local compatibility and official Python interop are validated by the Phase 120 commands.

## Phase 120B hardening

Phase 120B hardening closes known Phase 116-120 DataLoader review findings without expanding the Phase 120 compatibility verdict. CRC mismatch remains a hard failure, unindexed sequential fallback receives explicit limits, and remote prototype data access keeps production Remote Data Loader infrastructure out of scope.

## Environment

- Branch: `feature/phase120-mcap-compatibility-gate`
- Commit: recorded dynamically in `build/mcap-compat/phase120-report.json`
- Python: 3.13.9 on the local validation machine
- Official `mcap` package: 1.3.1 on the local validation machine

## Phase Evidence

- Phase 116: local `McapDataLoader` facade.
- Phase 117: MCAP spec parity matrix and summary-less/direct-message fallback.
- Phase 118: DataLoader hardening and performance baselines.
- Phase 119: local prototype remote MCAP manifest/data boundary.

## Official Interop

- Unity-authored MCAP fixtures are opened by local readers in `--phase120`.
- `--phase120-official` uses the official Python `mcap` reader to open Unity-authored fixtures.
- `--phase120-official` uses the official Python `mcap` writer to create supported fixture profiles, then opens those fixtures with Unity2Foxglove readers.

## Foxglove Desktop Manual Open

Manual visual confirmation is deferred for this automated implementation pass.

Open `build/mcap-compat/unity_chunked_all_indexes.mcap` in Foxglove Desktop before promoting a public `PASS` verdict. Confirm topics, schemas, timeline, Raw Messages, and relevant visualization panels.

## Performance

Phase 118 quick performance mode records DataLoader initialize, iteration, filtered query, and backfill scenarios. Full-mode numbers are optional release-candidate evidence and are not required for this automated gate.

## Skipped Checks

- Foxglove Desktop manual open: skipped/deferred, so verdict remains `PASS WITH NOTED LIMITATIONS`.
- Production Remote Data Loader deployment: intentionally out of scope.
- Cloud cache, Kubernetes/Helm, organization auth, Remote Access Gateway, and HTTP range serving: intentionally out of scope.

## Claim Ledger

May claim:

- Unity2Foxglove writes MCAP files validated against the selected official MCAP specification workflows.
- Unity2Foxglove-authored MCAP files are readable by the official Python MCAP reader for the supported fixture set.
- Unity2Foxglove can locally read, inspect, query, and backfill selected official Python-authored MCAP fixtures.
- Unity2Foxglove provides a local DataLoader-shaped facade over MCAP files.
- Unity2Foxglove has a documented boundary for future remote MCAP data-source work.

Must not claim:

- Unity2Foxglove is a complete official MCAP library replacement.
- Unity2Foxglove implements every official writer option as a public API.
- Unity2Foxglove implements production Foxglove Remote Data Loader infrastructure.
- Unity2Foxglove ships cloud cache, Kubernetes deployment, organization auth, or Remote Access Gateway support.
- Unity2Foxglove decodes every payload encoding into typed objects.

## References

- MCAP format specification: https://mcap.dev/spec
- MCAP Python library: https://mcap.dev/docs/python/
- MCAP Python writer API: https://mcap.dev/docs/python/mcap-apidoc/mcap.writer
- Foxglove Remote Data Loader: https://docs.foxglove.dev/docs/visualization/connecting/cloud-data/remote-data-loader
- Foxglove local data: https://docs.foxglove.dev/docs/visualization/connecting/local-data
