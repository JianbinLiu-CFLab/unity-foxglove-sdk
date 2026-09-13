import csv,collections,hashlib,json
from pathlib import Path
p=Path('build/phase187-round4/r4.1-real-environment-revalidation/i-series/R4.1-I_REFUTED_EVIDENCE_CORRECTION_20260912.tsv'); rows=list(csv.DictReader(p.open(encoding='utf-8'),delimiter='\t')); c=collections.Counter(r['disposition'] for r in rows); assert len(rows)==264 and not (c['PENDING_REVALIDATION'] or c['CONFIRMED']); print('overlay_rows=264'); print('terminal_dispositions='+json.dumps(c,sort_keys=True))
for d in ['I01-009-transaction','I01-010-transaction','I01-012-transaction','I01-013-transaction','I01-014-transaction','I01-017-transaction','I09-004-transaction','I08-003-transaction','I08-004-transaction','I08-007-transaction','I08-009-transaction']:
 q=p.parent/d; assert (q/'VERIFICATION.txt').exists() and (q/'ROLLBACK.sh').exists(); rb=(q/'rollback.out').read_text(encoding='utf-8',errors='ignore') if (q/'rollback.out').exists() else ''; assert 'ROLLBACK_OK' in rb and 'ROLLBACK_NOOP' in rb and 'live_modified_remains=true' in rb; print(d+': quartet_and_rollback=true')
print('overlay_sha256='+hashlib.sha256(p.read_bytes()).hexdigest()); print('adjudication=PASS')
