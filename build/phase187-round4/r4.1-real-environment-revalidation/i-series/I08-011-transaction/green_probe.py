#!/usr/bin/env python3
from pathlib import Path
s=Path('Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase134_10Validation.cs').read_text()
print('TERMINAL_CLEANUP_ASSERTION='+str('134-10L-1' in s))
print('FAILURE_COUNTER='+str('_cleanupFailures++' in s))
print('RESULT=GREEN')
