from pathlib import Path
checks={
'002':('Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxRunMessagePackArtifactAcceptanceValidation.cs',['if (string.IsNullOrWhiteSpace(evidenceRoot))','return;']),
'003':('Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxRunMessagePackArtifactAcceptanceValidation.cs',['if (string.IsNullOrWhiteSpace(evidenceRoot))','return;','185E-10']),
'004':('Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxRunMessagePackArtifactAcceptanceValidation.cs',['File.ReadAllText(evidencePath)','verdict']),
'005':('Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase10Validation.cs',['found >= 1']),
'006':('Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase10Validation.cs',['Close()','Count']),
'007':('Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase134_10Validation.cs',['OptionalFactoryDiagnostics','diagnostics != null']),
'009':('Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxRunMessagePackArtifactAcceptanceValidation.cs',['File.ReadAllText','Directory.GetFiles'])}
for id,(f,toks) in checks.items():
 s=Path(f).read_text(); print(id,f)
 for t in toks: print(' ',t,'=',t in s)
 print('RESULT=CONFIRMED_CANDIDATE')
