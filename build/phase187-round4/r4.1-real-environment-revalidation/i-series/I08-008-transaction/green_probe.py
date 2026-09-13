#!/usr/bin/env python3
import json
from pathlib import Path
p=Path('Unity2Foxglove/Assets/Generated/FoxRun/foxrun.generation-descriptor.json')
m=Path('Unity2Foxglove/Assets/Generated/FoxRun/foxrun.manifest.json')
json.loads(p.read_text()); json.loads(m.read_text())
print('DESCRIPTOR_STRICT_JSON=PASS')
print('MANIFEST_STRICT_JSON=PASS')
print('RESULT=GREEN')
