; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
FOXRUN006 | FoxRun | Error | Unsupported or non-canonical FoxRun member type.
FOXRUN007 | FoxRun | Warning | Generic FoxRun declaring type or member type may be unsafe for IL2CPP contract governance.
FOXRUN008 | FoxRun | Error | FoxRun topic must be absolute and start with '/'.
FOXRUN009 | FoxRun | Warning | RateHz <= 0 disables scheduled publishing unless trigger-only.
FOXRUN010 | FoxRun | Warning | Binary/blob values are unsupported in the FoxRun contract path.
