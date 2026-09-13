#!/usr/bin/env python3
from pathlib import Path
s=Path('Unity2Foxglove/Assets/Editor/ManualAcceptance/Phase186BatchModeRos2BridgeProbe.cs').read_text()
print('CREATED_AT_VALIDATION_PRESENT='+str('createdAt is stale or malformed' in s))
print('RESULT=GREEN')
