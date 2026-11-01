# Source Decomposition Validation

Repository-wide identity and source-layout validation for the completed Phase192 decomposition.

- `compare_identity_surfaces.py` compares public identity surfaces across revisions.
- `freeze_baseline.py` records the Task 0 inventory and identity baseline.
- `source_layout.py` loads split facades and source sections without changing runtime behavior.
- `identity_contracts/` contains the identity-surface extractors.
- `regression_checks/` contains the identity, baseline, and protocol regression tests.

## Migration authority

Reviewed decomposition transitions belong in the checker shipped by a trusted base revision. The workflow does not interpret migration errors or carry transition allowlists; an unrecognized transition fails closed until checker support and regression coverage land in that trusted base.

The Phase146B build-orchestrator split is already part of the current baseline. Future Phase146B changes must follow the checker-first process and must not restore a workflow fallback parser.

The directory name describes the responsibility of this tooling; historical `phase192` references remain only where they identify fixtures, evidence, or test scenarios.
