from pathlib import Path
p=Path('Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase115CValidation.cs')
s=p.read_text(encoding='utf-8')
checks={
 'unique_temp_root':'Guid.NewGuid().ToString("N")' in s,
 'residue_probe':'!Directory.EnumerateDirectories(tempRoot, "*.tmp-*").Any()' in s,
 'finally_cleanup':'TryDeleteDirectory(tempRoot)' in s,
 'recursive_delete':'Directory.Delete(path, recursive: true)' in s,
}
print(checks)
print('RESULT=REFUTED' if all(checks.values()) else 'RESULT=RED')
