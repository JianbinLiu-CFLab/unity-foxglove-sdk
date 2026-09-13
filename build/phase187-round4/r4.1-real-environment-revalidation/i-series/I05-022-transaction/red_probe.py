from pathlib import Path
p=Path('build/phase187-round4/r4.1-real-environment-revalidation/i-series/I05-022-transaction/original_run_ci.py')
text=p.read_text()
print('RED_RESULT=' + str('--ignore-failed-sources' in text))
print('RED_COMMAND=dotnet restore sample.csproj --ignore-failed-sources' if '--ignore-failed-sources' in text else 'RED_COMMAND=dotnet restore sample.csproj')
