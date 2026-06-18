# Test Migration Tracker (Phase 140B)

Maps console validation phases (`Tests/Runtime/Phase*Validation.cs`) to their
xUnit equivalents under `Tests/Unit/`. Behavioral checks move to xUnit; real
socket/integration and source-text/structural checks stay in the console runner
for now (the latter migrate to Roslyn syntax-tree tests in Phase 3).

**Console checks for rows marked `full` are deleted after Phase 4
reconciliation**, once equivalent xUnit (or Roslyn-backed xUnit) coverage exists.
Rows marked `partial` keep their remaining console coverage.

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
| Phase148 | Routing | `Routing/SinkChannelFilterTests` | partial | Pure per-sink channel filtering behavior is covered in xUnit with semantic test names. Filters are start-time/static routing policy: configure before `FoxgloveRuntime.Start`, stop before changing, and do not hot-swap during an active session. Console phase remains as a compatibility runner and registry check during Phase148. |
| Phase36 | Transport | `Transport/TransportStatsSnapshotTests` | partial | Pure-logic 36A / 36B-1..6 migrated. Real-socket `TestDisconnectedClientDropsRetained` + `TestRuntimeAccessorLifecycle` stay in console (integration, handled later). |
| Phase7 | Protocol | `Protocol/ServiceAndCapabilityTests` | partial | Pure-logic migrated (capabilities, logger, service registry/call, param subscription, time-frame). Real-server `TestStopStartPreservesParameters` + `TestHandlerDelegateSuccessAndFailure` (bind fixed ports 18795/18796) stay in console. |
| Phase6 | Protocol | `Protocol/ParameterAndServiceTests` | full | All checks are fake-transport pure logic (capabilities, parameter store/subscriptions, service advertise, binary codec, call timeout/sweep). |
| Phase140_21 | Harness | `Harness/LegacyPhaseOptimizationTests` | full | Source-shape and pure validator checks migrated to xUnit; console checks removed in Phase 4. |
| Phase140_22 | Harness | `Harness/LegacyPhaseOptimizationTests` | full | Source-shape checks migrated to xUnit; console checks removed in Phase 4. |
| Phase140_23 | Harness | `Harness/LegacyPhaseOptimizationTests` | full | Source-shape checks migrated to xUnit; console checks removed in Phase 4. |
| Phase140_24 | Harness | `Harness/LegacyPhaseOptimizationTests` | full | Source-shape and manifest-format checks migrated to xUnit; console checks removed in Phase 4. |
| Phase140_27 | Harness | `Harness/LegacyPhaseOptimizationTests` | full | Source-shape checks migrated to xUnit; console checks removed in Phase 4. |
| Phase140_64 | Harness | `Harness/SensorRos2OptimizationTests` | full | Source-shape and pure behavior checks migrated to xUnit; console checks removed in Phase 4. |
| Phase140_65 | Harness | `Harness/SensorRos2OptimizationTests` | full | Source-shape and pure behavior checks migrated to xUnit; console checks removed in Phase 4. |
| Phase140_66 | Harness | `Harness/SensorRos2OptimizationTests` | full | Source-shape and pure behavior checks migrated to xUnit; console checks removed in Phase 4. |
| Phase140_67 | Harness | `Harness/SensorRos2OptimizationTests` | full | Source-shape and pure behavior checks migrated to xUnit; console checks removed in Phase 4. |
| Phase140_68 | Harness | `Harness/GenerationEditorOptimizationTests` | full | Source-shape and pure API checks migrated to xUnit; console checks removed in Phase 4. |
| Phase140_69 | Harness | `Harness/GenerationEditorOptimizationTests` | full | Source-shape checks migrated to xUnit; console checks removed in Phase 4. |
| Phase140_70 | Harness | `Harness/GenerationEditorOptimizationTests` | full | Source-shape checks migrated to xUnit; console checks removed in Phase 4. |
| Phase140_71 | Harness | `Harness/GenerationEditorOptimizationTests` | full | Source-shape checks migrated to xUnit; console checks removed in Phase 4. |
| Phase140_72 | Harness | `Harness/GenerationEditorOptimizationTests` | full | Source-shape checks migrated to xUnit; console checks removed in Phase 4. |
| Phase140_73 | Harness | `Harness/GenerationEditorOptimizationTests` | full | Source-shape checks migrated to xUnit; console checks removed in Phase 4. |
| Phase140_74 | Harness | `Harness/SampleToolingOptimizationTests` | full | Source-shape checks migrated to xUnit; console checks removed in Phase 4. |
| Phase140_75 | Harness | `Harness/SampleToolingOptimizationTests` | full | Source-shape checks migrated to xUnit; console checks removed in Phase 4. |
| Phase140_76 | Harness | `Harness/SampleToolingOptimizationTests` | full | Source-shape checks migrated to xUnit; console checks removed in Phase 4. |
| Phase140_78 | Harness | `Harness/SampleToolingOptimizationTests` | full | Source-shape checks migrated to xUnit; console checks removed in Phase 4. |
| Phase140_79 | Harness | `Harness/SampleToolingOptimizationTests` | full | Source-shape checks migrated to xUnit; console checks removed in Phase 4. |
| Phase140_80 | Harness | `Harness/SampleToolingOptimizationTests` | full | Source-shape checks migrated to xUnit; console checks removed in Phase 4. |
| Phase140_81 | Harness | `Harness/SampleToolingOptimizationTests` | full | Source-shape checks migrated to xUnit; console checks removed in Phase 4. |
| Phase140_82 | Harness | `Harness/SampleToolingOptimizationTests` | full | Source-shape checks migrated to xUnit; console checks removed in Phase 4. |
| Phase140_83 | Harness | `Harness/R2fuGuardHelperOptimizationTests` | full | Source-shape checks migrated to Roslyn/xUnit; console checks removed in Phase 4. |
| Phase140_89 | Harness | `Harness/RuntimeValidationOptimizationTests` | full | Source-shape checks migrated to xUnit; console checks removed in Phase 4. |
| Phase140_90 | Harness | `Harness/RuntimeValidationOptimizationTests` | full | Source-shape checks migrated to xUnit; console checks removed in Phase 4. |
| Phase140_91 | Harness | `Harness/RuntimeValidationOptimizationTests` | full | Source-shape checks migrated to xUnit; console checks removed in Phase 4. |
| Phase140_92 | Harness | `Harness/RuntimeValidationOptimizationTests` | full | Source-shape checks migrated to xUnit; console checks removed in Phase 4. |
| Phase140_94 | Harness | `Harness/RuntimeValidationOptimizationTests` | full | Source-shape checks migrated to xUnit; console checks removed in Phase 4. |
| Phase140_95 | Harness | `Harness/RemoteTimelineOptimizationTests` | full | Source-shape checks migrated to xUnit; console checks removed in Phase 4. |
| Phase140_96 | Harness | `Harness/ConformancePerformanceOptimizationTests` | full | Source-shape and hygiene-scope checks migrated to Roslyn/xUnit; console checks removed in Phase 4. |

## Deferred (not yet migrated)

- **Real-socket / integration** (stay in console for now): Phase1, Phase3, Phase8,
  Phase28, Phase33, and the two Phase36 socket checks above.
- **Source-text / structural** (→ Roslyn, Phase 3): the `source` bucket from the
  Phase 0 triage manifest.
- **Repo hygiene / file existence** (stay in console permanently): the `hygiene`
  bucket.

## Phase 5 Audit

- CI runs both the runtime validation runner and the xUnit unit test project.
- Fully migrated Phase140B rows above now have xUnit coverage plus removal guards
  that assert their old console entries are absent.
- Partial rows keep their remaining console coverage until their unmigrated
  socket, source-shape, or hygiene checks have an equivalent target.
- Runtime runner ownership remains limited to repository hygiene, true
  integration/socket checks, Unity/ROS2/Foxglove Desktop acceptance boundaries,
  and not-yet-migrated partial coverage.
