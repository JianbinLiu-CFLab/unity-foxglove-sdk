# Test Migration Tracker (Phase 140B)

Maps console validation phases (`Tests/Runtime/Phase*Validation.cs`) to their
xUnit equivalents under `Tests/Unit/`. Behavioral checks move to xUnit; real
socket/integration and source-text/structural checks stay in the console runner
for now (the latter migrate to Roslyn syntax-tree tests in Phase 3).

**Console checks are intentionally retained (overlapping coverage)** until the
Phase 4 reconciliation, which verifies each migrated check has an xUnit (or
Roslyn) equivalent before deleting the console copy.

Status: `full` = whole file's behavioral checks migrated; `partial` = only a
subset migrated (remainder noted).

| Source (console) | Domain | xUnit class | Status | Notes |
|---|---|---|---|---|
| Phase140_17 | Lidar | `Sensors/LidarProfileAndPatternTests` | partial | Behavioral 140-17D/E only. Source-text checks (Slice/Contains on `.cs`) stay → Phase 3 Roslyn. |
| Phase134_8 | Mcap | `Mcap/McapLengthPrefixBoundsTests` | full | |
| Phase134_9 | Mcap | `Mcap/McapReaderIndexingTests` | full | |
| Phase10 | Mcap | `Mcap/McapRecordRoundtripTests` | full | Links shared `McapRecordReader` helper. |
| Phase24D | Mcap | `Mcap/McapMixedSchemaGuardTests` | full | |
| Phase34 | Mcap | `Mcap/McapAttachmentAndSummaryCrcTests` | full | |
| Phase37 | Mcap | `Mcap/McapDirectMessageRecordsTests` | full | |
| Phase36 | Transport | `Transport/TransportStatsSnapshotTests` | partial | Pure-logic 36A / 36B-1..6 migrated. Real-socket `TestDisconnectedClientDropsRetained` + `TestRuntimeAccessorLifecycle` stay in console (integration, handled later). |
| Phase7 | Protocol | `Protocol/ServiceAndCapabilityTests` | partial | Pure-logic migrated (capabilities, logger, service registry/call, param subscription, time-frame). Real-server `TestStopStartPreservesParameters` + `TestHandlerDelegateSuccessAndFailure` (bind fixed ports 18795/18796) stay in console. |
| Phase6 | Protocol | `Protocol/ParameterAndServiceTests` | full | All checks are fake-transport pure logic (capabilities, parameter store/subscriptions, service advertise, binary codec, call timeout/sweep). |
| Phase140_83 | Harness | `Harness/R2fuGuardHelperOptimizationTests` | full | Source-shape checks migrated to Roslyn/xUnit; console checks retained until Phase 4. |
| Phase140_95 | Harness | `Harness/RemoteTimelineOptimizationTests` | full | Source-shape checks migrated to xUnit; console checks retained until Phase 4. |
| Phase140_96 | Harness | `Harness/ConformancePerformanceOptimizationTests` | full | Source-shape and hygiene-scope checks migrated to Roslyn/xUnit; console checks retained until Phase 4. |

## Deferred (not yet migrated)

- **Real-socket / integration** (stay in console for now): Phase1, Phase3, Phase8,
  Phase28, Phase33, and the two Phase36 socket checks above.
- **Source-text / structural** (→ Roslyn, Phase 3): the `source` bucket from the
  Phase 0 triage manifest.
- **Repo hygiene / file existence** (stay in console permanently): the `hygiene`
  bucket.
