from pathlib import Path
import re,hashlib
p=Path('Packages/dev.unity2foxglove.sdk/Tests/Runtime/Program.cs'); b=p.read_bytes(); t=b.decode('utf-8')
body=t[t.index('internal static List<string> CollectValidationFailures'):t.index('    /// <summary>', t.index('internal static List<string> CollectValidationFailures')+1)]
checks={'foreach':bool(re.search(r'foreach\s*\(var validation in validations\)',body)),'no_early_return': 'return failures;' in body and 'return new' not in body,'failure_accumulation':'failures.Add(validation.Name)' in body}
print('SOURCE_SHA256='+hashlib.sha256(b).hexdigest()); print('CHECKS='+repr(checks)); print('RESULT=SATISFIED_BY_ROUND4' if all(checks.values()) else 'RESULT=CONFIRMED')
