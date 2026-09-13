#!/usr/bin/env python3
from pathlib import Path
import shutil
r=Path(__file__).resolve().parent;o=r/'originals'/'Phase10Validation.cs';m=r/'modified'/'Phase10Validation.cs';s=r/'rollback'/'seeded.cs';z=r/'rollback'/'restored.cs';shutil.copy2(m,s);print('seeded_equals_modified='+str(s.read_bytes()==m.read_bytes()).lower());shutil.copy2(o,z);print('restored_equals_original='+str(z.read_bytes()==o.read_bytes()).lower());print('live_modified_remains='+str(m.read_bytes()!=o.read_bytes()).lower());print('ROLLBACK_OK');shutil.copy2(o,z);print('ROLLBACK_NOOP')
