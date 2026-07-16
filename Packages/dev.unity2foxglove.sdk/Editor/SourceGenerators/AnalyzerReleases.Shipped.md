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
FOXRUN006 | FoxRun | Error | Unsupported or non-canonical FoxRun member type.
FOXRUN007 | FoxRun | Warning | Generic FoxRun declaring type or member type may be unsafe for IL2CPP contract governance.
FOXRUN008 | FoxRun | Error | FoxRun topic must be absolute and start with '/'.
FOXRUN009 | FoxRun | Warning | RateHz <= 0 disables scheduled publishing unless trigger-only.
FOXRUN010 | FoxRun | Warning | Binary/blob values are unsupported in the FoxRun contract path.
FOXRUN011 | FoxRun | Error | FoxRun declaring class name is required.
FOXRUN012 | FoxRun | Error | FoxRun member name is required.
FOXRUN013 | FoxRun | Error | FoxRun publish mode must be between 0 and 3.
FOXRUN014 | FoxRun | Error | FoxRun member kind must be field or property.
FOXRUN015 | FoxRun | Error | FoxRun conditional gate member is missing or invalid.
FOXRUN016 | FoxRun | Error | FoxRun conditional gate member must be bool.
FOXRUN017 | FoxRun | Error | Same-topic FoxRun members mix When or Unless conditional gates.
FOXRUN018 | FoxRun | Error | [FoxRunField] requires an enclosing [FoxRunMessage] type.
FOXRUN019 | FoxRun | Error | Aggregate and field-level FoxRun members cannot share one topic.
FOXRUN020 | FoxRun | Error | Aggregate array fields are not supported yet.
FOXRUN021 | FoxRun | Error | [FoxRunField] cannot be applied to static members.
FOXRUN022 | FoxRun | Error | Aggregate JSON field names must be unique per topic.
FOXRUN200 | FoxRun | Error | FoxRun inbound arrays and aggregate members are not supported.
FOXRUN201 | FoxRun | Warning | SubscribeOnly ignores publish timing options.
FOXRUN202 | FoxRun | Warning | SubscribeOnly member names should communicate input-port authority.
FOXRUN203 | FoxRun | Error | FoxRun inbound targets must be writable.
FOXRUN204 | FoxRun | Error | FoxRun SubscriptionProvider must be a known provider value.
FOXRUN205 | FoxRun | Error | Ros2Native is supported only for SubscribeOnly members.
FOXRUN206 | FoxRun | Error | Ros2Native cannot declare JSON or Protobuf Encoding.
FOXRUN207 | FoxRun | Error | Native member type must implement ROS2.Message from ros2cs_common.
FOXRUN208 | FoxRun | Error | Native message type requires a public parameterless constructor.
FOXRUN209 | FoxRun | Error | Native message type must be declared directly in a package msg namespace.
FOXRUN210 | FoxRun | Error | Explicit SchemaName must match the validated canonical ROS type.
FOXRUN211 | FoxRun | Error | Native message graph cannot be deep-copied safely.
FOXRUN212 | FoxRun | Error | Native generation requires the optional Native assembly reference.
FOXRUN213 | FoxRun | Warning | Ros2Qos is ignored for explicit WebSocket-only subscriptions.
FOXRUN400 | FoxRun | Warning | PublishAndSubscribe requires explicit authority ownership.
FOXRUN401 | FoxRun | Error | PublishAndSubscribe requires an explicit Protobuf or Json Encoding.
FOXRUN600 | FoxRun | Error | FoxRun mode must be PublishOnly, SubscribeOnly, or PublishAndSubscribe.
FOXRUN601 | FoxRun | Error | FoxRun Unless conditional gate member is missing or invalid.
FOXRUN602 | FoxRun | Error | FoxRun Encoding must be inherit, json, or protobuf.
FOXRUN603 | FoxRun | Error | FoxRun ProtobufFieldNumber must be a legal non-reserved tag or zero for automatic assignment.
FOXRUN604 | FoxRun | Error | Same-topic FoxRun members cannot mix Encoding declarations.
FOXRUN605 | FoxRun | Error | FoxRun ProtobufFieldNumber values must be unique per topic.
FOXSERVICE001 | FoxService | Error | FoxService name must be non-empty and absolute.
FOXSERVICE002 | FoxService | Error | FoxService method signature is unsupported.
FOXSERVICE003 | FoxService | Error | FoxService request type is unsupported.
FOXSERVICE004 | FoxService | Error | FoxService response type is unsupported.
FOXSERVICE005 | FoxService | Error | FoxService name is duplicated.
FOXSERVICE006 | FoxService | Warning | FoxService schema metadata is omitted and generated defaults are used.
FOXSERVICE007 | FoxService | Warning | FoxService DTO member is ignored or only partially serializable.
FOXSERVICE008 | FoxService | Error | FoxService DTO graph is recursive.
FOXSERVICE009 | FoxService | Warning | FoxService DTO graph exceeds the conservative validation depth limit.
