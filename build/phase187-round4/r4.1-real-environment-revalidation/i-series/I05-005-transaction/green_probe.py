from pathlib import Path
p=Path('.github/workflows/dotnet-tests.yml'); lines=p.read_text().splitlines(); names=['Run xUnit unit tests','Run FoxRun publish panel behavior tests','Validate source generator DLL freshness script','Validate generated ROS2 schema output freshness','Run validation suite','Run Phase179 ROS2 acceptance helper regressions','Run Phase181 custom ROS2 acceptance helper regressions','Run Phase186 Bridge tooling and package-composition gate','Run official MCAP differential conformance','Validate local entrypoints']
missing=[]
for n in names:
 i=next(i for i,l in enumerate(lines) if l.strip()==f'- name: {n}')
 if not any(l.strip()=='if: always()' for l in lines[i+1:i+4]): missing.append(n)
print('GREEN_MISSING_ALWAYS='+str(len(missing))); print('GREEN_RESULT='+str(not missing))
