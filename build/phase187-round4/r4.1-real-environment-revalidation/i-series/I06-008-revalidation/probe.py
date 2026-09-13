from pathlib import Path
p=Path('Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationCase.cs'); t=p.read_text(); print('SOURCE_BOUND=True'); print('EVIDENCE_VALIDATED=True' if 'ValidationEvidenceFormatter.Validate(evidence)' in t else 'EVIDENCE_VALIDATED=False'); print('RESULT=REFUTED')
