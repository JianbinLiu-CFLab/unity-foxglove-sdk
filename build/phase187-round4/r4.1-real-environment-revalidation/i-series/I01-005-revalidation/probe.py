from pathlib import Path
import hashlib,json
paths=[Path('Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxRunTransportProviderDrawerRegistry.cs'),Path('Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunEditorRegistry/FoxRunEditorDefinitionRegistry.cs')]
text='\n'.join(p.read_text() for p in paths)
checks={'identity_freeze':'new Entry(id, order, definition)' in text,'conflict_quarantine':'Definitions.Count == 1' in text,'reserved_builtin':'built-in Foxglove WebSocket Provider ID is reserved.' in text}
out={'source_sha256':{str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in paths},'checks':checks,'result':'SATISFIED_BY_ROUND4' if all(checks.values()) else 'PENDING'}
print(json.dumps(out,sort_keys=True)); (Path('build/phase187-round4/r4.1-real-environment-revalidation/i-series/I01-005-revalidation/SUMMARY.json')).write_text(json.dumps(out,indent=2)+'\n')
