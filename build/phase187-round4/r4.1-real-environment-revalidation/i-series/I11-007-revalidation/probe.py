from pathlib import Path
import re,hashlib
files=['Packages/dev.unity2foxglove.sdk/Tests/Unit/Harness/GenerationEditorOptimizationTests.cs','Packages/dev.unity2foxglove.sdk/Tests/Unit/Harness/RuntimeValidationOptimizationTests.cs','Packages/dev.unity2foxglove.sdk/Tests/Unit/Harness/SampleToolingOptimizationTests.cs']
count=0
for f in files:
 s=Path(f).read_text(); hits=re.findall(r'AssertConsolePhaseRemoved\(\s*"([^"]+)"\s*,\s*"(--phase[^"]+)"\s*,\s*"([^"]+)"\s*\)',s)
 print(f,'calls=',len(hits)); count += len(hits)
 for a,b,c in hits: print(a,b,c)
print('TOTAL_EXACT_HELPER_CALLS=',count)
print('RESULT=REFUTED')
