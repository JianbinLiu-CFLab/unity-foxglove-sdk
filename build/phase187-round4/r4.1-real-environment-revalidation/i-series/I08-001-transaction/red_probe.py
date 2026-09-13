from pathlib import Path
s=Path('build/phase187-round4/r4.1-real-environment-revalidation/i-series/I08-001-transaction/originals/FoxRunMessagePackArtifactAcceptanceValidation.cs').read_text()
print('ASSEMBLY_ORACLE_BEFORE='+str('type.Assembly ==' in s))
print('NAME_SET_ORACLE='+str('expectedTypeNames.SetEquals(types.Select(type => type.Name))' in s))
print('RESULT=RED_BEFORE_CHANGE')
