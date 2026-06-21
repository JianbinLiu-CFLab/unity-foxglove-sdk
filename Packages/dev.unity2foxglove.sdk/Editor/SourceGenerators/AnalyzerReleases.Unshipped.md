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
FOXRUN011 | FoxRun | Error | FoxRun declaring class name is required.
FOXRUN012 | FoxRun | Error | FoxRun member name is required.
FOXRUN013 | FoxRun | Error | FoxRun publish mode must be between 0 and 3.
FOXRUN014 | FoxRun | Error | FoxRun member kind must be field or property.
FOXRUN018 | FoxRun | Error | [FoxRunField] requires an enclosing [FoxRunMessage] type.
FOXRUN019 | FoxRun | Error | Aggregate and field-level FoxRun members cannot share one topic.
FOXRUN020 | FoxRun | Error | Aggregate array fields are not supported yet.
FOXRUN021 | FoxRun | Error | [FoxRunField] cannot be applied to static members.
FOXRUN022 | FoxRun | Error | Aggregate JSON field names must be unique per topic.
FOXRUN023 | FoxRun | Error | FoxRun mode must be PublishOnly, SubscribeOnly, or PublishAndSubscribe.
FOXRUN024 | FoxRun | Error | FoxRun inbound arrays and aggregate members are not supported.
FOXRUN025 | FoxRun | Warning | SubscribeOnly ignores publish timing options.
FOXRUN026 | FoxRun | Warning | PublishAndSubscribe requires explicit authority ownership.
FOXRUN027 | FoxRun | Warning | SubscribeOnly member names should communicate input-port authority.
FOXRUN028 | FoxRun | Error | FoxRun inbound targets must be writable.
