# Workstream G Closure Ledger

Updated: 2026-10-07

This ledger records a reconstructed closure mapping for the Unity lifecycle and
manual-acceptance review that was originally tracked as Phase 140-27. The
`Phase140-27/P*` labels below are local finding labels, not historical IDs. The
original validation checks use the `140-27A-1` through `140-27F-4` namespace;
the final column keeps that mapping explicit for later audits. The reviewed
scope is:

- Unity2Foxglove/Assets/Scripts/**
- Unity2Foxglove/Assets/Editor/**
- Unity2Foxglove/Assets/Experimental/**

The reviewed main baseline for this closure pass is
4b940af4e1395ff4ae9af95949187f8f1614b6c8; the closure changes are carried by
the feature commit that follows that baseline.
The historical register is retained by finding ID and severity so later reviews
can distinguish closure evidence from a new regression.

## Reconstructed finding register

| Finding | Severity | Current state | Original checks | Remediation and regression evidence |
| --- | --- | --- | --- | --- |
| Phase140-27/P2-1 | P2 | Closed | 140-27A-1 | Demo wiring uses transactional ownership and clears its generation, registrations, event handlers, and initialized state when the runtime disappears. |
| Phase140-27/P2-2 | P2 | Closed | 140-27B-1 | Debug overlay uses the shared run-in-background lease; overlapping smoke components no longer restore another owner's value. |
| Phase140-27/P2-3 | P2 | Closed | 140-27B-2 | Phase106 uses the same shared lease and releases it during disable/destroy cleanup. |
| Phase140-27/P2-4 | P2 | Closed | 140-27C-1, 140-27C-2 | Phase110 and Phase127 warn once when the optional executor hook is unavailable instead of silently skipping the condition. |
| Phase140-27/P2-5 | P2 | Closed | 140-27C-3 | Phase127 reports `UNITY2FOXGLOVE_R2FU_EXECUTOR_STARTED=False` when reflection cannot find the hook. |
| Phase140-27/P2-6 | P2 | Closed | 140-27E-1, 140-27E-3 | Input System references are guarded, the legacy input path compiles without the optional package, and the fallback fixture is tested. |
| Phase140-27/P3-1 | P3 | Closed | 140-27D-2 | FoxRun trigger fixed-rate telemetry uses a 64-bit counter. |
| Phase140-27/P3-2 | P3 | Closed | 140-27A-2 | Full Demo resolves an explicitly assigned cube or a scene cube publisher, not a fragile Player-tagged object lookup. |
| Phase140-27/P3-3 | P3 | Closed | 140-27E-5 | The live and package sample scenes provide the cube binding where available; manager and cube consumers retain fallback discovery for unbound optional fields. |
| Phase140-27/P3-4 | P3 | Closed | 140-27F-1, 140-27F-2 | Phase109 separates the side-effect-free availability query from initialization. |
| Phase140-27/P3-5 | P3 | Closed | 140-27E-2 | Mouse drag caches the main camera and only retries lookup after the cached camera is unavailable. |
| Phase140-27/P3-6 | P3 | Closed | 140-27D-1 | The long-running FoxRun probe keeps its frame counter wide and bounds the float sample to its exact integer range. |

## Supporting historical checks

These checks remain part of the historical validation surface but are supporting
behavior or harness checks rather than independent reconstructed findings:

- `140-27A-3`: reset_pose reports a missing cube as an error.
- `140-27A-4`: client-message preview uses strict UTF-8 before the hex fallback.
- `140-27E-4`: FoxRun telemetry fields remain private.
- `140-27F-3`: the test project compiles the migrated Phase 140-27 validation.
- `140-27F-4`: the validation registry exposes the Phase 140-27 command.

## Automated closure checks

UnityDemoRuntimeScriptTests verifies the ledger IDs, the shared lease
integration, and the name-independent cube resolver. WorkstreamFClosureTests
also exercises overlapping lease owners and restoration of the original global
value. The source-level checks are intentionally paired with the repository's
real Unity batch gate because Unity lifecycle behavior cannot be executed by the
dotnet-only test assembly.

The required Unity gate must report exit code 0, no error CS lines, and no
warning CS lines for the exact commit under review. A new finding must receive
a new ID; historical rows are not reused for unrelated regressions.
