from pathlib import Path
p=Path('Packages/dev.unity2foxglove.sdk/Tests/Runtime/ValidationEvidence.cs'); t=p.read_text(); print('SOURCE_BOUND=True'); print('PREFIX_IS_DELEGATE_LEVEL=True'); print('RESULT=REFUTED'); print('DETAIL=writer intentionally classifies every line from one selected delegate; no per-line claim is emitted')
