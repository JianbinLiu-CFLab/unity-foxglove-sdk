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
