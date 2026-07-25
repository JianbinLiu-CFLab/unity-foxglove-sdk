# FoxRun Publish, Subscribe, and Full Duplex

## 1. Purpose

FoxRun exposes small Unity fields and properties as generated transport
contracts. It supports outbound telemetry, inbound control values, and an
explicit full-duplex mode without handwritten publisher or subscriber
components.

Use it for debug state, numeric plots, commands, small DTOs, and explicit event
snapshots. Use dedicated publisher components for large images, point clouds,
meshes, or other high-throughput binary data.

## 2. Smallest Example

```csharp
using UnityEngine;
using Unity.FoxgloveSDK.Components;

public partial class RobotTelemetry : MonoBehaviour
{
    [FoxRun("/topic")]
    private Vector3 _position;

    private void Update()
    {
        _position = transform.position;
    }
}
```

`[FoxRun("/topic")]` means `Publish`, `FixedRate`, 10 Hz. The containing
class must be `partial`, the topic must start with `/`, and the value must have
a supported generated wire shape.

## 3. Declaration Grammar

Import the short flow and policy vocabularies in files that use explicit
options:

```csharp
using Unity.FoxgloveSDK.Components;
using static Unity.FoxgloveSDK.Components.FoxRunFlow;
using static Unity.FoxgloveSDK.Components.FoxRunPolicy;
```

Then declarations stay compact:

```csharp
[FoxRun("/robot/pose")]
private PoseState _pose;

[FoxRun("/robot/command", Mode = Subscribe, Policy = Change, Hz = 30)]
private RobotCommand _command;

[FoxRun("/debug/state", Mode = PublishAndSubscribe,
    Policy = FixedRate, Hz = 10,
    Encoding = FoxRunEncoding.Protobuf)]
private DebugState _debugState;
```

### Flows

| `Mode` | Meaning |
|---|---|
| `Publish` | Unity is the source and sends the current value. This is the default. |
| `Subscribe` | One selected external endpoint is the source; Unity applies accepted values on the main thread. |
| `PublishAndSubscribe` | Both directions are generated. This is intended for debugging and integration, not as the normal production default. |

One subscription declaration resolves to exactly one `Source`. Publishing may
fan out to one or more `Targets`.

### Policies

| `Policy` | Publish behavior | Subscribe behavior |
|---|---|---|
| `FixedRate` | Sends the current value on each eligible cadence. | Applies when a newer staged value exists; it never reapplies stale state just because a timer fired. |
| `Change` without `Hz` | Sends the first value and later semantic changes. | Applies changed input at the next main-thread opportunity, bounded by the maximum subscribe rate. |
| `Change` with `Hz` | Sends changes plus a heartbeat at `Hz`. | Applies changes immediately and may refresh a newly received equal duplicate at `Hz`; it never invents a duplicate from stale input. |
| `Trigger` | Sends only when the generated publish trigger is called. | Keeps the newest staged value until the generated apply trigger is called. |

`Trigger` cannot be combined with an explicit positive `Hz`; the source
generator reports `FOXRUN609` instead of silently ignoring either setting.

`Tolerance` controls the change threshold for supported floating-point and
vector values. `Change + Hz` supplies the heartbeat without a second policy.
`OnlyIf` names one bool field, property, or zero-argument method and expresses
one positive gate. Members on the same topic must agree on `Policy`, `Hz`,
`Tolerance`, and `OnlyIf`; otherwise the generator reports `FOXRUN005`.

## 4. Rate and Admission Controls

`Hz` is a boundary cadence, not a network or ROS2 discovery setting:

- Publish: maximum generated publication cadence.
- Subscribe: maximum main-thread application cadence after transport admission.
- PublishAndSubscribe: the same explicit value governs each direction
  independently.

When `Hz` is omitted, fixed-rate publish resolves to 10 Hz and fixed-rate subscribe inherits the
Manager's frozen **Default Subscribe Rate Hz** (10 Hz by default).

Under **Foxglove Manager > Data Transport > Subscribe Data > Subscription
Delivery**, two adjacent controls have different jobs:

- **Default Subscribe Rate Hz** is 10 Hz by default and is inherited only by
  fixed-rate declarations without a positive `Hz`.
- **Maximum Subscribe Rate Hz (per Topic)** is the hard source-neutral
  admission ceiling for Foxglove WebSocket and ROS 2 Native input. Excess
  messages are dropped before avoidable DTO decode or native deep-copy work.

A declaration override cannot exceed the admission ceiling. Accepted input is
bounded latest-wins: if Unity cannot apply every value, the newest owned value
replaces the older pending value.

## 5. Subscribe Data

Subscribe Data is an external control surface and is disabled by default.
Enable **FoxRun Subscriptions** in the Manager before entering Play Mode.

Prefer an input-buffer member and validate it in normal Unity code:

```csharp
using UnityEngine;
using Unity.FoxgloveSDK.Components;
using static Unity.FoxgloveSDK.Components.FoxRunFlow;
using static Unity.FoxgloveSDK.Components.FoxRunPolicy;

public partial class SpeedController : MonoBehaviour
{
    [FoxRun("/control/target-speed", Mode = Subscribe,
        Source = FoxRunEndpoint.Foxglove,
        Policy = Change, Hz = 30,
        Encoding = FoxRunEncoding.JSON)]
    private float _requestedTargetSpeed;

    private void Update()
    {
        var safeTarget = Mathf.Clamp(_requestedTargetSpeed, 0f, 10f);
        ApplyValidatedTarget(safeTarget);
    }
}
```

Inbound targets must be writable. Generated allowlists, payload bounds,
encoding checks, source checks, transport admission, owned latest-wins
staging, and main-thread application all remain in force. A non-loopback
listener remains fail-closed unless the Manager's explicit remote-input and
authentication policy allows it.

## 6. Directional Endpoints and Encoding

Omit `Source`, `Targets`, or `Encoding` to inherit the relevant frozen Manager
profile. Do not write a numeric zero sentinel in user code. A full-duplex
declaration may inherit different Foxglove encodings for publish and subscribe;
an explicit `Encoding` applies to every Foxglove direction selected by that
declaration.

```csharp
[FoxRun("/control/command", Mode = PublishAndSubscribe,
    Encoding = FoxRunEncoding.Protobuf)]
private DriveCommand _command;
```

`Source` chooses the one input source. The normal core SDK path is
`FoxRunEndpoint.Foxglove`. `FoxRunEndpoint.Ros2Native` requires the optional
`dev.unity2foxglove.ros2forunity` facade, one selected distro runtime package,
and a supported native message or matching custom typesupport add-on.
`dev.unity2foxglove.sdk` alone is the normal installation for Foxglove and
ROS2 Bridge. `FoxRunEndpoint.Ros2Bridge` is publish-only and requires the
manually operated localhost sidecar; it is neither a subscribe source nor a
remote gateway.

`Targets` accepts one or more endpoint flags and replaces, rather than extends,
the Publish Profile default:

```csharp
[FoxRun("/robot/state",
    Targets = FoxRunEndpoint.Foxglove | FoxRunEndpoint.Ros2Native)]
private RobotState _state;
```

JSON and Protobuf are Foxglove wire encodings. Native and Bridge targets use
their generated ROS 2 message contracts; CDR is not a public `Encoding` option.
Source, targets, encoding, QoS, copy budget, maximum subscribe rate, and
directional default rates are frozen for the corresponding enabled session.

## 7. Official ROS 2 QoS

QoS is portable ROS 2 vocabulary, not a distro or RMW switch. It is legal only
when the declaration resolves at least one ROS 2 Native or Bridge direction.
Foxglove-only declarations do not consume ROS 2 QoS.

```csharp
[FoxRun("/robot/state",
    Targets = FoxRunEndpoint.Ros2Native | FoxRunEndpoint.Ros2Bridge,
    QoS = FoxRunQosProfile.Default,
    Reliability = FoxRunQosReliability.BestEffort,
    Durability = FoxRunQosDurability.TransientLocal,
    History = FoxRunQosHistory.KeepLast,
    Depth = 7)]
private RobotState _state;
```

The base profiles are `Default`, `SensorData`, and `SystemDefault`. Optional
overrides are `Reliable` or `BestEffort`, `Volatile` or `TransientLocal`,
`KeepLast` or `KeepAll`, and a positive Keep Last `Depth`. `SystemDefault` and
`KeepAll` remain real transport values; they are not silently rewritten.
`KeepAll` cannot be combined with `Depth`. Native and Bridge receive the same
resolved portable contract and let the selected ROS 2 transport perform its
official mapping.

## 8. Explicit Triggers

Publish triggers set the value first and then call the generated
`FoxRun_Publish_<member>()` method:

```csharp
using UnityEngine;
using Unity.FoxgloveSDK.Components;
using static Unity.FoxgloveSDK.Components.FoxRunPolicy;

public partial class StateReporter : MonoBehaviour
{
    [FoxRun("/events/state", Policy = Trigger)]
    private string _state;

    private void OnEnable()
    {
        _state = "enabled";
        FoxRun_Publish_state();
    }
}
```

Subscribe triggers stage the newest input without changing the Unity member
until user code calls `FoxRun_Apply_<member>()`. Generated trigger methods are
main-thread-oriented; worker callbacks should marshal to the Unity main thread
before invoking them.

## 9. Full Duplex

`PublishAndSubscribe` generates independent publish and apply schedules from
one declaration. Applying an inbound value marks that exact version so it is
not immediately echoed as a fresh outbound change; a later local mutation can
publish normally. Use this mode for debug loops and integration probes where
both sides understand the ownership rule. Prefer separate `Publish` and
`Subscribe` declarations for production authority boundaries.

## 10. Bounded Input Streams

Ordinary subscribed fields are bounded latest-wins state. Use the explicitly
opted-in `FoxRunStream<T>` shape when user code needs an ordered, finite batch
of high-rate input:

```csharp
using Unity.FoxgloveSDK.Components;
using static Unity.FoxgloveSDK.Components.FoxRunEndpoint;
using static Unity.FoxgloveSDK.Components.FoxRunFlow;

public partial class ControlSamples : MonoBehaviour
{
    [FoxRun("/control/samples", Mode = Subscribe, Source = Foxglove)]
    private FoxRunStream<ControlSample> _samples =
        new FoxRunStream<ControlSample>(
            new FoxRunStreamOptions(
                capacity: 32,
                maxInputHz: 1000,
                maxBatch: 16,
                overflow: FoxRunStreamOverflowPolicy.DropOldest));

    private void Update()
    {
        _samples.Drain(sample => Process(sample));
    }
}
```

A stream declaration is one initialized, non-static field with exactly one
`Subscribe` attribute. `Source`, Foxglove `Encoding`, and ROS 2 QoS are legal.
`Targets`, `Policy`, `Hz`, `Tolerance`, and `OnlyIf` are not: stream admission
and user-driven consumption replace ordinary field scheduling.

The parameterless stream uses capacity 1024, a finite 1000 Hz admission
ceiling, maximum batch 128, and `DropOldest`. `Drain(Action<T>)` retains stream
ownership, invokes at most `MaxBatch` callbacks, and disposes each value after
its callback; the callback must not retain the value. `TryTake` and
`TryTakeLatest` instead transfer one `FoxRunStreamSample<T>` lease to the
caller, which must dispose it. `Stats` exposes saturating received, admitted,
drained, taken, overflow, rate-drop, high-water, clear, and disposal
diagnostics without per-message logging. Streams remain Subscribe-only.

## 11. Aggregate Messages

`[FoxRunMessage]` remains an aggregate publish form. It uses the same `Policy`
vocabulary but does not expose a partial inbound mode.

```csharp
using Unity.FoxgloveSDK.Components;
using static Unity.FoxgloveSDK.Components.FoxRunPolicy;

[FoxRunMessage("/robot/summary", Policy = Change)]
public partial class RobotSummary
{
    [FoxRunField("battery")]
    private float _battery;
}
```

## 12. Foxglove Workflow

1. Add the component and a `FoxgloveManager` to the scene.
2. Configure Publish Data and, when needed, Subscribe Data before Play Mode.
3. Enter Play Mode.
4. Connect Foxglove to `ws://127.0.0.1:8765`.
5. Use Topics, Raw Messages, or Plot for output.
6. Use the optional **FoxRun Publish** extension for generated writable JSON
   and Protobuf subscription contracts.

The panel discovers contracts through `/foxrun/subscription-contracts`; it
does not guess topics or encodings. Protobuf input uses binary MessageData and
does not fall back to JSON.

## 13. Generated Evidence and Player Builds

The Roslyn generator is the authoring authority. Editor Play Mode refreshes the
canonical descriptor, manifest, hashes, and runtime schema info. Player builds
also generate physical fallback `.g.cs` files before compilation. These
artifacts describe the resolved contract and support replay governance; they do
not create a second runtime declaration model.

The canonical manifest is deterministic governance evidence. Report-only
timestamps and warnings are excluded from its contract fingerprints, while the
generated manifest, descriptor, hashes, and fallback sources remain ignored
machine-local build state rather than versioned authoring input.

Editor Play Mode registers runtime schema info carrying the manifest hash used
as MCAP metadata and later replay drift evidence.

MCAP stores this evidence as `unity2foxglove.foxrun.schema`, including
`globalManifestHash`. A replay schema mismatch is handled by the Manager's
configured schema-identity guard instead of silently applying incompatible
FoxRun data.

The broader SDK schema manifest also catalogs Protobuf and packaged ROS2
coverage. That aggregate inventory is separate from replay governance, which
uses the FoxRun contract identity recorded with the MCAP.

The debug overlay is non-contract diagnostics. It is not included in canonical
hashes and is not a replay guard key.

During IL2CPP preprocessing, expect logs such as:

```text
[FoxrunBuildPreprocess] Generating FoxRun source files...
[FoxrunCodeGenerator] Generated RobotTelemetry_FoxRun.g.cs
```

MCAP records the external boundary representation. Replay compares the
recorded FoxRun schema identity with the current generated identity and
suppresses live WebSocket and native fanout while replay is authoritative.

## 14. Troubleshooting

| Symptom | Check |
|---|---|
| No topic appears | The class is `partial`, the topic starts with `/`, the component is enabled, and Play Mode is running. |
| Subscribe receives nothing | Enable subscriptions, verify the selected source and encoding, and inspect transport-admission diagnostics. |
| Input arrives but applies slowly | Check declaration `Hz` or the Manager's **Default Subscribe Rate Hz**. |
| Messages are dropped | Check **Maximum Subscribe Rate Hz (per Topic)**, payload bounds, encoding, and native copy budget. |
| Stream drops or retains fewer samples than offered | Check its finite `MaxInputHz`, capacity, overflow policy, `MaxBatch`, and `Stats`; every stream is intentionally bounded. |
| Trigger value does not move | Call the correct generated publish or apply trigger from the Unity main thread. |
| Full-duplex value does not echo immediately | One-shot suppression of the just-applied inbound version is intentional. |
| Editor works but Player does not | Inspect the build-preprocess logs and generated fallback source. |

See [09_IL2CPP_Build_Guide](09_IL2CPP_Build_Guide.md) for Player verification
and [10_Architecture](10_Architecture.md) for generator and runtime ownership.
