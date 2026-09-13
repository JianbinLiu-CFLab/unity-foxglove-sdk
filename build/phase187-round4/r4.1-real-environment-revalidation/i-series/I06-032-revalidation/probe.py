from pathlib import Path
import hashlib,re,json
p=Path('Packages/dev.unity2foxglove.sdk/Tests/Runtime/ValidationEvidence.cs'); b=p.read_bytes(); t=b.decode('utf-8')
exact_class=bool(re.search(r'\bclass\s+ValidationEvidence\b',t)); enum=bool(re.search(r'\benum\s+ValidationEvidence\b',t)); formatter=bool(re.search(r'\bclass\s+ValidationEvidenceFormatter\b',t)); writer=bool(re.search(r'\bclass\s+ValidationEvidenceTextWriter\b',t));
out={'source_sha256':hashlib.sha256(b).hexdigest(),'exact_class':exact_class,'enum':enum,'formatter':formatter,'writer':writer,'result':'REFUTED' if not exact_class and enum and formatter and writer else 'CONFIRMED'}
print(json.dumps(out,sort_keys=True)); Path('build/phase187-round4/r4.1-real-environment-revalidation/i-series/I06-032-revalidation/SUMMARY.json').write_text(json.dumps(out,indent=2)+'\n')
