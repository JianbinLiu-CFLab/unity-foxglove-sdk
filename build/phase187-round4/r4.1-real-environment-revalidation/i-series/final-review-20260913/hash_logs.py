from pathlib import Path
import hashlib
for n in ['correctness-phase184.log','correctness-focused.log','scope-check.log','mutation-check.log']:
 p=Path('build/phase187-round4/r4.1-real-environment-revalidation/i-series/final-review-20260913')/n
 print(n,'exit_recorded',p.exists(),'sha256',hashlib.sha256(p.read_bytes()).hexdigest())
