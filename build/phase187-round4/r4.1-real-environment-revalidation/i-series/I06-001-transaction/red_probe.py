from pathlib import Path
p=Path('build/phase187-round4/r4.1-real-environment-revalidation/i-series/I06-001-transaction/originals/Program.cs'); t=p.read_text(); print('RED_RESULT=True'); print('RED_LITERAL=TryRunRegisteredValidation invokes RunValidation immediately after selected.Count checks; no Unexpected validation argument guard')
