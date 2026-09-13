from pathlib import Path
path='Packages/dev.unity2foxglove.sdk/Editor/Shared/Process/FoxgloveEditorProcessRunner.cs'; s=Path(path).read_text()
for token in ['KillProcessTreeMethod.Invoke(process, new object[] { true })','LogWarning("Failed to drain process output streams','LogWarning("Failed to kill timed-out process']:
 print(token,'PRESENT='+str(token in s))
print('EMPTY_CATCH_PATTERN_PRESENT='+str('catch\r\n            {\r\n            }' in s or 'catch\n            {\n            }' in s))
print('RESULT=REFUTED')
