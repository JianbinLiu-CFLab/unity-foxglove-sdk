# Phase186 breaking upgrade

Phase186 intentionally removes ROS-specific API and serialized state from
`dev.unity2foxglove.sdk`. There is no compatibility facade or reader for the
ROS-specific Manager and Publisher fields listed as removed below. Back up
external scenes and prefabs, update scripts, install the intended optional
Provider packages, then configure each Manager again.

## Package replacement

- Keep `dev.unity2foxglove.sdk` for Foxglove WebSocket, MCAP, replay, and
  transport-neutral FoxRun.
- Add `dev.unity2foxglove.ros2bridge` for the loopback U2R2 sidecar.
- Add `dev.unity2foxglove.ros2forunity` plus exactly one matching runtime for
  direct native ROS2. Bridge and R2FU do not depend on each other.

Moved Bridge runtime, CDR, schema, diagnostics, Editor, analyzer, sample, and
test types now use `Unity2Foxglove.Ros2Bridge` or
`Unity2Foxglove.Ros2Bridge.Editor`. R2FU-owned native types use the R2FU
package namespaces.

The R2FU project setting `_initialRosPackageName` is relocated rather than
discarded. Its owning type keeps the exact
`ProjectSettings/FoxRunRos2InterfacePackageSettings.asset` `FilePath` and the
same serialized field name inside `dev.unity2foxglove.ros2forunity`; it is read
only when that optional package is installed.

## Declaration API removed from the core SDK

The following public declaration surface was removed:

- `FoxRunEndpoint`, including `None`, `Everything`, `Ros2Native`, and
  `Ros2Bridge`;
- `FoxRunAttribute.Source`;
- `FoxRunAttribute.Targets` and `FoxRunMessageAttribute.Targets`;
- `FoxRunQosProfile`, `FoxRunQosReliability`, `FoxRunQosDurability`,
  `FoxRunQosHistory`, `FoxRunResolvedQos`, and the ROS-specific QoS resolvers;
- SDK-owned ROS interface generation, typesupport preflight, CDR, U2R2,
  Bridge health/runtime, and native demand/status APIs.

Use the direction-specific Provider contract and portable delivery policy:

```csharp
[FoxRun(
    "/robot/state",
    Mode = FoxRunFlow.PublishAndSubscribe,
    SubscribeTransportId = "unity2foxglove.ros2bridge",
    PublishTransportIds = new[]
    {
        "foxglove.websocket",
        "unity2foxglove.ros2bridge"
    },
    Reliability = FoxRunDeliveryReliability.BestEffort,
    Durability = FoxRunDeliveryDurability.Volatile,
    History = FoxRunDeliveryHistory.KeepLast,
    Depth = 5)]
private RobotState _state;
```

An omitted publish list inherits the Manager's frozen zero-or-more Publish
destinations. An omitted subscribe ID inherits its one enabled Subscribe
source. Explicit IDs are never replaced by a fallback.

## Provider extension and Inspector changes

- Third-party Editor integrations must now implement both
  `IFoxRunTransportProviderDrawer.Order` and
  `IFoxRunManagerSetupDrawer.Order`. These required interface members are a
  source-breaking change; use them to give drawers a deterministic order.
- The drawer ID `foxglove.websocket` is reserved for the built-in transport.
  A third-party drawer must not register that ID.
- Registrations with duplicate drawer IDs remain conflicted until only one
  owner remains. The later registration no longer replaces the earlier one,
  and a conflicted Provider has no Inspector subsection.
- An empty Manager Subscribe Transport ID disables selection of a Subscribe
  source and does not fall back to foxglove.websocket. The corresponding
  `ConfiguredFoxRunSubscribeTransportId.Value` is `null` until a source is
  selected.
- Publish and Subscribe fields are closed selectors over installed Provider
  choices. Select from the installed Provider choices; the Inspector no
  longer accepts an arbitrary not-yet-installed Provider ID as free text.

## Exact old core public type inventory

The following top-level public type names no longer resolve from the core SDK
assembly. The first group is replaced by neutral Provider/delivery contracts;
the second group moved to, or was replaced inside, the optional R2FU package;
the third group moved to, or was replaced inside, the optional Bridge package.
Moving a type to another package or assembly is a breaking API change even
when its simple name remains the same.

### Replaced by neutral core contracts

```text
FoxRunEndpoint
FoxRunEndpointDiagnosticCode
FoxRunEndpointResolution
FoxRunEndpointResolver
FoxRunPublishDispatchResult
FoxRunPublishTargetPolicy
FoxRunPublishTargetStatus
FoxRunQosDiagnosticCode
FoxRunQosDurability
FoxRunQosHistory
FoxRunQosProfile
FoxRunQosReliability
FoxRunQosResolution
FoxRunResolvedPublishContract
FoxRunResolvedQos
FoxRunResolvedTopology
FoxTopicSinkPublishResult
IFoxglovePublishTargetSource
IFoxTopicResolvedContractSink
IFoxTopicTargetSink
```

### Relocated to or replaced by R2FU

```text
FoxRunCustomNativeContractDemandPolicy
FoxRunManifestCustomNativeContract
FoxRunNativeDemandPolicy
FoxRunReflectionRos2CustomDtoShapeBuilder
FoxRunReflectionRos2MessageShapeBuilder
FoxRunRos2ContractCapability
FoxRunRos2ContractKind
FoxRunRos2CustomDtoDiagnostic
FoxRunRos2CustomDtoMemberKind
FoxRunRos2CustomDtoMemberShape
FoxRunRos2CustomDtoSequenceRepresentation
FoxRunRos2CustomDtoShape
FoxRunRos2CustomIdentity
FoxRunRos2CustomNamingPolicy
FoxRunRos2CustomOutboundBudgetPolicy
FoxRunRos2InterfaceContractLock
FoxRunRos2InterfaceDigest
FoxRunRos2InterfaceDigestInput
FoxRunRos2InterfaceIdentity
FoxRunRos2InterfaceInvalidLockException
FoxRunRos2InterfaceJsonWriter
FoxRunRos2InterfaceLock
FoxRunRos2InterfacePackageCommand
FoxRunRos2InterfacePackagePreflight
FoxRunRos2InterfacePackageRenderer
FoxRunRos2InterfacePackageWriter
FoxRunRos2InterfacePackageWriteResult
FoxRunRos2InterfaceRenderedFile
FoxRunRos2InterfaceRenderedPackage
FoxRunRos2InterfaceRenderException
FoxRunRos2InterfaceRevisionRequiredException
FoxRunRos2InterfaceSourcePreflightContract
FoxRunRos2InterfaceSourcePreflightDiagnosticCode
FoxRunRos2InterfaceSourcePreflightResult
FoxRunRos2InterfaceSourcePreflightState
FoxRunRos2MessageMemberKind
FoxRunRos2MessageMemberShape
FoxRunRos2MessageShape
FoxRunRos2NativeCopyBudgetPolicy
FoxRunRos2QosProfileResolver
FoxRunRos2SequenceRepresentation
FoxRunRos2ShapeDiagnostic
FoxRunSchemaCustomNativeContractInfo
PointCloud2NativeFrame
Ros2NativeOutputPolicy
```

### Relocated to or replaced by the Bridge package

```text
FoxgloveRos2MsgSchemaCatalog
FoxgloveRos2MsgSchemaCatalogEntry
IRos2BridgeCommandRunner
IRos2BridgeHealthProbe
IRos2BridgePublisherPreparationTransport
IRos2BridgeSink
McapRos2CdrDiagnosticPayload
McapRos2CdrTypedDecoderFactory
ProcessRos2BridgeCommandRunner
Ros2BridgeCommandResult
Ros2BridgeEffectiveOutput
Ros2BridgeFrame
Ros2BridgeFrameWriter
Ros2BridgeHealthCheckResult
Ros2BridgeHealthEnvironmentSnapshot
Ros2BridgeHealthOptions
Ros2BridgeHealthPong
Ros2BridgeHealthProgress
Ros2BridgeHealthReport
Ros2BridgeHealthRunner
Ros2BridgeHealthStatus
Ros2BridgeHealthSummary
Ros2BridgeOutputOverride
Ros2BridgeOutputPolicy
Ros2BridgeOutputResolution
Ros2BridgeProbeResult
Ros2BridgePublisher
Ros2BridgeRos2PathSource
Ros2BridgeRuntime
Ros2BridgeStatsSnapshot
Ros2BridgeTcpClient
Ros2BridgeTopicProfile
Ros2BridgeU2R2HealthCodec
Ros2BridgeU2R2HealthProbe
Ros2CdrCameraCalibrationBuilder
Ros2CdrCompressedImageBuilder
Ros2CdrCompressedPointCloudBuilder
Ros2CdrDeserializerEntry
Ros2CdrDeserializerRegistry
Ros2CdrFrameTransformBuilder
Ros2CdrGeneratedDeserializers
Ros2CdrGeneratedSerializers
Ros2CdrGeometryWriter
Ros2CdrLaserScanBuilder
Ros2CdrPayloadValidator
Ros2CdrPointCloudBuilder
Ros2CdrReader
Ros2CdrSceneUpdateBuilder
Ros2CdrSensorCameraInfoBuilder
Ros2CdrSensorCompressedImageBuilder
Ros2CdrSensorPointCloud2Builder
Ros2CdrSerializerEntry
Ros2CdrSerializerRegistry
Ros2CdrWriter
Ros2CdrWriterBudgetExceededException
Ros2MsgSchemasSetup
Ros2PublisherSchemaNames
Unity2FoxgloveRos2MsgRegistryEntry
Unity2FoxgloveRos2MsgRegistrySection
```

## Removed members on surviving core types

- `FoxRunAttribute.Source`, `FoxRunAttribute.Targets`,
  `FoxRunAttribute.QoS`, `FoxRunAttribute.Reliability`,
  `FoxRunAttribute.Durability`, and `FoxRunAttribute.History`;
- `FoxRunMessageAttribute.Targets`, `FoxRunMessageAttribute.QoS`,
  `FoxRunMessageAttribute.Reliability`, `FoxRunMessageAttribute.Durability`,
  and `FoxRunMessageAttribute.History`;
- the endpoint/QoS members on `FoxgloveInputTopicInfo`,
  `FoxRunPublishSessionPolicy`, `FoxRunSubscriptionSessionPolicy`, manifest,
  descriptor, schema-info, and source-generator model types;
- `FoxgloveManager.Ros2NativeEnabled`, `Ros2BridgeEnabled`,
  `DefaultRos2BridgeOutputEnabled`, `AllowPublisherRos2BridgeOverride`,
  `Ros2BridgeNamespace`, `ResolveRos2BridgeQos`,
  `ResolveRos2BridgeTopic`, `TryResolveRos2BridgeTopic`, old active/default
  endpoint and ROS QoS properties, native copy-budget properties,
  `TryPrepareRos2Publish`, `PublishRos2`, `PublishRos2Cdr`, and
  `GetOrRegisterRos2MsgSchemaChannel`;
- `FoxgloveRuntime.RegisterRos2MsgSchemaChannel` and
  `FoxgloveRuntime.PublishRos2Cdr`, plus
  `FoxgloveSession.EnableCdr`, `IsCdrEnabled`,
  `RegisterRos2MsgSchemaChannel`, and `PublishRos2Cdr`;
- `FoxglovePublisherBase.Ros2BridgeOutput`, `BridgeOutputResolution`,
  `Ros2BridgeTopicOverride`, `EffectiveRos2BridgeTopic`,
  `EffectiveRos2BridgeQos`, `SupportsRos2BridgeOutput`, and
  `SupportsRos2Encoding`;
- the old camera/point-cloud Bridge payload flags and PointCloud2-native
  request, frame, event, TF, schema, topic, diagnostics, and prepared-demand
  members. Use Provider contributions and the packed point-cloud surface.

## Removed Manager serialized fields

These pre-Phase186 fields are not read after upgrade:

```text
_ros2NativeEnabled
_ros2BridgeEnabled
_ros2BridgeHost
_ros2BridgePort
_ros2BridgeAutoConnect
_defaultRos2BridgeOutputEnabled
_allowPublisherRos2BridgeOverride
_ros2BridgeNamespace
_ros2BridgeQosPreset
_ros2BridgeCustomReliability
_ros2BridgeCustomDurability
_ros2BridgeCustomDepth
_legacyRos2BridgeQosPreset
_legacyRos2BridgeCustomReliability
_legacyRos2BridgeCustomDurability
_legacyRos2BridgeCustomDepth
_ros2BridgeQosSerializationVersion
_ros2BridgeQos
_ros2BridgeQueueCapacity
_ros2BridgeReconnectIntervalMs
_ros2BridgeSendTimeoutMs
_defaultFoxRunNativePublishRos2Qos
_legacyDefaultFoxRunNativePublishRos2Qos
_defaultFoxRunNativePublishQos
_defaultFoxRunRos2Qos
_legacyDefaultFoxRunRos2Qos
_defaultFoxRunNativeSubscribeQos
_defaultFoxRunEndpoint
_defaultFoxRunSubscriptionProvider
_defaultFoxRunSubscriptionSource
_foxRunRos2NativeCopyBudgetBytes
```

Replacement:

1. In `FoxgloveManager > Data Transport`, select zero or more Publish
   destinations from the installed Provider choices.
2. Enable subscriptions only when needed and select exactly one Source.
3. Selecting Bridge or R2FU creates its hidden, normally serialized companion.
4. Configure host/port/reconnect or native runtime/QoS only in that Provider's
   subsection.
5. Disable/re-enable or restart Play Mode so the next immutable session
   captures the new configuration.

## Removed ordinary-publisher serialized fields

These component fields are not migrated:

```text
_ros2BridgeOutput
_ros2BridgeTopicOverride
_publishStandardRos2CompressedImage
_publishStandardRos2RawImage
```

Ordinary publishers now inherit the Manager's Publish destinations and route
through neutral Provider contributions. Provider-wide topic policy belongs to
the Provider companion. Advanced FoxRun declarations use
`PublishTransportIds`; ordinary publisher Inspectors do not expose a second
routing authority.

Seven point-cloud names changed but retain explicit Unity serialization
aliases, so they are not data-loss removals:

| Former field | Current field |
| --- | --- |
| `_publishPointCloud2NativeTfAnchor` | `_publishPackedPointCloudTfAnchor` |
| `_pointCloud2NativeTfParentFrame` | `_packedPointCloudTfParentFrame` |
| `_pointCloud2NativeTfChildFrame` | `_packedPointCloudTfChildFrame` |
| `_pointCloud2NativeTfTranslation` | `_packedPointCloudTfTranslation` |
| `_pointCloud2NativeTfRotationEuler` | `_packedPointCloudTfRotationEuler` |
| `_deskewedPointCloud2NativeTopic` | `_deskewedPackedPointCloudTopic` |
| `_deskewedPointCloud2NativeMaxPublishRateHz` | `_deskewedPackedPointCloudMaxPublishRateHz` |

## Upgrade sequence

1. Commit or back up every external scene and prefab.
2. Update `[FoxRun]` and `[FoxRunMessage]` declarations.
3. Install only the SDK and optional Provider packages the project needs.
4. Open each Manager and recreate its Publish/Subscribe selection explicitly.
5. Re-enter Provider-specific host, port, QoS/policy, and native runtime values.
6. Review Camera and ordinary publisher routing; do not expect removed per-
   component Bridge toggles to survive.
7. Let Unity import every installed Provider analyzer, then regenerate FoxRun
   sources and resolve every diagnostic.
8. Verify the generated core partial contains no ROS/CDR/U2R2 symbol and each
   Provider contributes its own partial.
9. Run the package matrix and the applicable live Provider acceptance before
   saving upgraded production scenes.

The repository intentionally does not infer old endpoints or silently select a
Provider. A configured missing or conflicted ID is an explicit failure.
