#!/usr/bin/env python3
from pathlib import Path
p=Path('Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxRunMessagePackArtifactAcceptanceValidation.cs')
s=p.read_text()
malformed='{"descriptorVersion":6,"generatorVersion":"6.0.0","types":[] garbage "encoding":"msgpack"}'
print('RAW_CONTAINS_GATE='+str('descriptor.Contains("\\"descriptorVersion\\":6"' in s))
print('MALFORMED_TOKEN_RETENTION='+str(all(t in malformed for t in ['"descriptorVersion":6','"generatorVersion":"6.0.0"','"encoding":"msgpack"'])))
print('STRICT_JSON_ORACLE=REJECT')
print('RESULT=RED')
