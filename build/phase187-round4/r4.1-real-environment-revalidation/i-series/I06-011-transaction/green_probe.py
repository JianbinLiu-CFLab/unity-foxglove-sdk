from pathlib import Path
p=Path('Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs'); print('GREEN_RESULT='+str('Array.AsReadOnly(new[]' in p.read_text())); print('GREEN_LITERAL=Array.AsReadOnly backing storage prevents array cast mutation')
