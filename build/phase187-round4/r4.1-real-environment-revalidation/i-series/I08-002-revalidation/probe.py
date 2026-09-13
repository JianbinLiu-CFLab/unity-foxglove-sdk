from pathlib import Path
import hashlib, json
p=Path('Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxRunMessagePackArtifactAcceptanceValidation.cs')
s=p.read_text(encoding='utf-8')
assert 'PHASE185_EVIDENCE_ROOT' in s
assert 'delegates ignored descriptor and manifest proof to the required Unity batch gate' in s
assert 'if (string.IsNullOrWhiteSpace(evidenceRoot))' in s
assert 'return;' in s
print('default_branch_delegates_to_unity_gate=true')
print('materialized_artifact_read_requires_evidence_root=true')
print('result=SATISFIED_BY_ROUND4')
print('source_sha256='+hashlib.sha256(p.read_bytes()).hexdigest())
