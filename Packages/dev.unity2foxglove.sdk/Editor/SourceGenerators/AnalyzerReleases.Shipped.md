; Shipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

## Release 1.4.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
FOXRUN001 | FoxRun | Error | Class must be declared partial to use [FoxRun].
FOXRUN002 | FoxRun | Warning | Same FoxRun topic has conflicting SchemaName values.
FOXRUN003 | FoxRun | Warning | FoxRun field names collide after stripping leading underscores.
FOXRUN004 | FoxRun | Error | [FoxRun] on a multi-variable field declaration is unsupported.
FOXRUN005 | FoxRun | Warning | Same-topic FoxRun members mix PublishMode, ChangeEpsilon, or ForceIntervalSeconds.
FOXRUN015 | FoxRun | Error | FoxRun conditional gate member is missing or invalid.
FOXRUN016 | FoxRun | Error | FoxRun conditional gate member must be bool.
FOXRUN017 | FoxRun | Error | Same-topic FoxRun members mix When or Unless conditional gates.
FOXSERVICE001 | FoxService | Error | FoxService name must be non-empty and absolute.
FOXSERVICE002 | FoxService | Error | FoxService method signature is unsupported.
FOXSERVICE003 | FoxService | Error | FoxService request type is unsupported.
FOXSERVICE004 | FoxService | Error | FoxService response type is unsupported.
FOXSERVICE005 | FoxService | Error | FoxService name is duplicated.
FOXSERVICE006 | FoxService | Warning | FoxService schema metadata is omitted and generated defaults are used.
