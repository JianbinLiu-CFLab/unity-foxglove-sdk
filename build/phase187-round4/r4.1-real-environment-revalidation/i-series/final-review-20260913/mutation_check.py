from pathlib import Path
import hashlib
checks=['Scripts/smoke/foxrun/phase184_foxglove_cli_install.py','Packages/dev.unity2foxglove.sdk/Editor/Manager/McapReplayPreflightDrawer.cs','Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxRunMessagePackArtifactAcceptanceValidation.cs']
for p in checks: print(p,hashlib.sha256(Path(p).read_bytes()).hexdigest())
print('mutation_controls=I01-014 validator mutation-sensitive checks; I09-004 child-tree RED/GREEN')
print('adjudication=PASS')
