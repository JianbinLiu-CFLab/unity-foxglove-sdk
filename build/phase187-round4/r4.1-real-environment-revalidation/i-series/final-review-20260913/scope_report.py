from pathlib import Path
import hashlib
p=Path('build/phase187-round4/r4.1-real-environment-revalidation/i-series/final-review-20260913/scope-check.log'); print(p.read_text(encoding='utf-8')); print('scope_review_exit='+str(0 if 'adjudication=PASS' in p.read_text(encoding='utf-8') else 1)); print('scope_log_sha256='+hashlib.sha256(p.read_bytes()).hexdigest())
