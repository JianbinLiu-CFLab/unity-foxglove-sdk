from pathlib import Path
p=Path('Scripts/release/run_ci.py')
text=p.read_text()
print('GREEN_RESULT=' + str('--ignore-failed-sources' not in text))
print('GREEN_COMMAND=dotnet restore sample.csproj' if '--ignore-failed-sources' not in text else 'GREEN_COMMAND=dotnet restore sample.csproj --ignore-failed-sources')
