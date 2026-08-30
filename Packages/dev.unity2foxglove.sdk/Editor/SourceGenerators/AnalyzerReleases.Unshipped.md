; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
FOXRUN200 | FoxRun | Error | FoxRun inbound arrays and aggregate members are not supported.
FOXRUN202 | FoxRun | Warning | Subscribe member names should communicate input-port authority.
FOXRUN203 | FoxRun | Error | FoxRun inbound targets must be writable.
FOXRUN215 | FoxRun | Error | FoxRunStream<T> must be one initialized Subscribe field with stream-safe arguments.
FOXRUN216 | FoxRun | Error | FoxRunStream<T> fields must have a non-null initializer.
FOXRUN600 | FoxRun | Error | FoxRun mode must be Publish, Subscribe, or PublishAndSubscribe.
FOXRUN602 | FoxRun | Error | FoxRun Encoding must be inherit, json, or protobuf.
FOXRUN603 | FoxRun | Error | FoxRun ProtobufFieldNumber must be a legal non-reserved tag or zero for automatic assignment.
FOXRUN604 | FoxRun | Error | Same-topic FoxRun members cannot mix Encoding declarations.
FOXRUN605 | FoxRun | Error | FoxRun ProtobufFieldNumber values must be unique per topic.
FOXRUN609 | FoxRun | Error | FoxRun Trigger cannot be combined with an explicit Hz.
FOXRUN610 | FoxRun | Error | Generated FoxRun method conflicts with an existing member.
FOXRUN616 | FoxRun | Error | Typed MessagePack requires a supported bounded recursive value shape and signed Int32 enum values.
FOXRUN617 | FoxRun | Error | Explicit MessagePack cannot use Protobuf-only field-number metadata.
FOXRUN618 | FoxRun | Error | Explicit MessagePack subscribe topics cannot mix ordinary and stream members or contain multiple streams.
FOXRUN619 | FoxRun | Error | Explicit multi-member MessagePack directions require one normalized schedule tuple.
FOXRUN620 | FoxRun | Error | FoxRun transport Provider selection invalid.
FOXRUN621 | FoxRun | Error | FoxRun directional transport selection invalid.
FOXRUN622 | FoxRun | Error | FoxRun delivery policy contains an invalid axis or depth.
FOXRUN623 | FoxRun | Error | FoxRun declaring host identity cannot be represented by the generated partial-class contract.
FOXSERVICE010 | FoxService | Error | FoxService declaring host identity cannot be represented by the generated partial-class contract.

### Removed Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
FOXRUN023 | FoxRun | Error | Retired; renumbered as FOXRUN600 and permanently reserved.
FOXRUN024 | FoxRun | Error | Retired; renumbered as FOXRUN200 and permanently reserved.
FOXRUN025 | FoxRun | Warning | Retired; renumbered as FOXRUN201 and permanently reserved.
FOXRUN026 | FoxRun | Warning | Retired; renumbered as FOXRUN400 and permanently reserved.
FOXRUN027 | FoxRun | Warning | Retired; renumbered as FOXRUN202 and permanently reserved.
FOXRUN028 | FoxRun | Error | Retired; renumbered as FOXRUN203 and permanently reserved.
FOXRUN029 | FoxRun | Error | Retired; renumbered as FOXRUN601 and permanently reserved.
FOXRUN030 | FoxRun | Error | Retired; renumbered as FOXRUN602 and permanently reserved.
FOXRUN031 | FoxRun | Error | Retired; renumbered as FOXRUN603 and permanently reserved.
FOXRUN032 | FoxRun | Error | Retired; renumbered as FOXRUN604 and permanently reserved.
FOXRUN033 | FoxRun | Error | Retired; renumbered as FOXRUN605 and permanently reserved.
FOXRUN034 | FoxRun | Error | Retired; renumbered as FOXRUN401 and permanently reserved.
FOXRUN035 | FoxRun | Error | Retired; renumbered as FOXRUN204 and permanently reserved.
FOXRUN036 | FoxRun | Error | Retired; renumbered as FOXRUN205 and permanently reserved.
FOXRUN037 | FoxRun | Error | Retired; renumbered as FOXRUN206 and permanently reserved.
FOXRUN038 | FoxRun | Error | Retired; renumbered as FOXRUN207 and permanently reserved.
FOXRUN039 | FoxRun | Error | Retired; renumbered as FOXRUN208 and permanently reserved.
FOXRUN040 | FoxRun | Error | Retired; renumbered as FOXRUN209 and permanently reserved.
FOXRUN041 | FoxRun | Error | Retired; renumbered as FOXRUN210 and permanently reserved.
FOXRUN042 | FoxRun | Error | Retired; renumbered as FOXRUN211 and permanently reserved.
FOXRUN043 | FoxRun | Error | Retired; renumbered as FOXRUN212 and permanently reserved.
FOXRUN044 | FoxRun | Warning | Retired; renumbered as FOXRUN213 and permanently reserved.

; Reserved before release. These are comments instead of Removed Rules rows
; because Roslyn release tracking permits removal rows only for shipped rules.
; FOXRUN201 | FoxRun | Warning | Retired before release; subscription policy now applies symmetrically and this ID is permanently reserved.
; FOXRUN205 | FoxRun | Error | Retired before release; Ros2Native is now a legal Subscribe or PublishAndSubscribe Source and this ID remains permanently reserved.
; FOXRUN206 | FoxRun | Error | Retired before release; directional Encoding legality now uses FOXRUN612 and this ID remains permanently reserved.
; FOXRUN214 | FoxRun | Error | Retired before release; directional Source legality now uses FOXRUN612 and this ID remains permanently reserved.
; FOXRUN213 | FoxRun | Warning | Retired before release; explicit QoS without a ROS 2 direction now fails with FOXRUN614 and this ID remains permanently reserved.
; FOXRUN400 | FoxRun | Warning | Retired before release; valid PublishAndSubscribe is an explicit flow and ownership guidance belongs in documentation, so this ID remains permanently reserved.
; FOXRUN401 | FoxRun | Error | Retired before release; directional profiles resolve full-duplex encodings independently and this ID remains permanently reserved.
; FOXRUN601 | FoxRun | Error | Retired before release with the removed Unless declaration and remains permanently reserved.
; FOXRUN204, FOXRUN207-FOXRUN212, FOXRUN402, FOXRUN606-FOXRUN608, and FOXRUN611-FOXRUN615 are retired before release with the removed closed transport model and remain permanently reserved.
