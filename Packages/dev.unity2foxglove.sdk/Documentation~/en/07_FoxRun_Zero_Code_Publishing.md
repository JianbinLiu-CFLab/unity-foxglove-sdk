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
    [FoxRun("/robot/pose")]
    private Vector3 _position;

    private void Update()
    {
        _position = transform.position;
    }
}
```

`[FoxRun("/robot/pose")]` means `Publish`, `FixedRate`, 10 Hz. The containing
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

[FoxRun("/robot/command", Mode = Subscribe, Policy = Change, RateHz = 30)]
private RobotCommand _command;

[FoxRun("/debug/state", Mode = PublishAndSubscribe,
    Policy = FixedRate, RateHz = 10,
    Encoding = FoxRunWireEncoding.Protobuf)]
private DebugState _debugState;
```

### Flows

| `Mode` | Meaning |
|---|---|
| `Publish` | Unity is the source and sends the current value. This is the default. |
| `Subscribe` | One selected external provider is the source; Unity applies accepted values on the main thread. |
| `PublishAndSubscribe` | Both directions are generated. This is intended for debugging and integration, not as the normal production default. |

One subscription declaration resolves to exactly one input provider. Publishing
may fan out to multiple enabled destinations.

### Policies

| `Policy` | Publish behavior | Subscribe behavior |
|---|---|---|
| `FixedRate` | Sends the current value on each eligible cadence. | Applies when a newer staged value exists; it never reapplies stale state just because a timer fired. |
| `Change` | Sends the first value and later semantic changes. | Applies only when the staged value differs from the last applied value. |
| `ChangeOrInterval` | Sends changes plus the configured heartbeat interval. | Applies a change or a newly received duplicate after the interval; it never invents a duplicate. |
| `Trigger` | Sends only when the generated publish trigger is called. | Keeps the newest staged value until the generated apply trigger is called. |

`Trigger` cannot be combined with an explicit positive `RateHz`; the source
generator reports `FOXRUN609` instead of silently ignoring either setting.

`ChangeEpsilon` controls the change threshold for floating-point and vector
values. `ForceIntervalSeconds` controls the `ChangeOrInterval` heartbeat.
Members on the same topic must agree on `Policy`, `ChangeEpsilon`, and
`ForceIntervalSeconds`; otherwise the generator reports `FOXRUN005`.

## 4. Rate and Admission Controls

`RateHz` is a boundary cadence, not a network or ROS2 discovery setting:

- Publish: maximum generated publication cadence.
- Subscribe: maximum main-thread application cadence after transport admission.
- PublishAndSubscribe: the same explicit value governs each direction
  independently.

When `RateHz` is omitted, publish resolves to 10 Hz and subscribe inherits the
Manager's frozen **Default Subscribe Rate Hz** (10 Hz by default).

Under **Foxglove Manager > Data Transport > Subscribe Data > Subscription
Delivery**, two adjacent controls have different jobs:

- **Default Subscribe Rate Hz** is 10 Hz by default and is inherited only by
  declarations without a positive `RateHz`.
- **Maximum Subscribe Rate Hz (per Topic)** is the hard provider-neutral
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
        Policy = Change, RateHz = 30,
        Encoding = FoxRunWireEncoding.Json)]
    private float _requestedTargetSpeed;

    private void Update()
    {
        var safeTarget = Mathf.Clamp(_requestedTargetSpeed, 0f, 10f);
        ApplyValidatedTarget(safeTarget);
    }
}
```

Inbound targets must be writable. Generated allowlists, payload bounds,
encoding checks, provider checks, transport admission, owned latest-wins
staging, and main-thread application all remain in force. A non-loopback
listener remains fail-closed unless the Manager's explicit remote-input and
authentication policy allows it.

## 6. Wire Encoding and Input Provider

`Encoding = FoxRunWireEncoding.Inherit` resolves through the Manager's frozen
directional defaults. `PublishAndSubscribe` uses one wire contract in both
directions and must therefore choose JSON or Protobuf explicitly.

```csharp
[FoxRun("/control/command", Mode = PublishAndSubscribe,
    Encoding = FoxRunWireEncoding.Protobuf)]
private DriveCommand _command;
```

`SubscriptionProvider` chooses the one input source. The normal core SDK path
uses Foxglove WebSocket. `Ros2Native` requires the optional
`dev.unity2foxglove.ros2forunity` facade, one selected distro runtime package,
and a supported native message or matching custom typesupport add-on. Provider,
encoding, QoS, copy budget, maximum subscribe rate, and default subscribe rate
are frozen for one enabled subscription session.

## 7. Explicit Triggers

Publish triggers set the value first and then call the generated
`FoxRun_Trigger_<member>()` method:

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
        FoxRun_Trigger_state();
    }
}
```

Subscribe triggers stage the newest input without changing the Unity member
until user code calls `FoxRun_Apply_<member>()`. Generated trigger methods are
main-thread-oriented; worker callbacks should marshal to the Unity main thread
before invoking them.

## 8. Full Duplex

`PublishAndSubscribe` generates independent publish and apply schedules from
one declaration. Applying an inbound value marks that exact version so it is
not immediately echoed as a fresh outbound change; a later local mutation can
publish normally. Use this mode for debug loops and integration probes where
both sides understand the ownership rule. Prefer separate `Publish` and
`Subscribe` declarations for production authority boundaries.

## 9. Aggregate Messages

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

## 10. Foxglove Workflow

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

## 11. Generated Evidence and Player Builds

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

## 12. Troubleshooting

| Symptom | Check |
|---|---|
| No topic appears | The class is `partial`, the topic starts with `/`, the component is enabled, and Play Mode is running. |
| Subscribe receives nothing | Enable subscriptions, verify the selected provider and encoding, and inspect transport-admission diagnostics. |
| Input arrives but applies slowly | Check declaration `RateHz` or the Manager's **Default Subscribe Rate Hz**. |
| Messages are dropped | Check **Maximum Subscribe Rate Hz (per Topic)**, payload bounds, encoding, and native copy budget. |
| Trigger value does not move | Call the correct generated publish or apply trigger from the Unity main thread. |
| Full-duplex value does not echo immediately | One-shot suppression of the just-applied inbound version is intentional. |
| Editor works but Player does not | Inspect the build-preprocess logs and generated fallback source. |

See [09_IL2CPP_Build_Guide](09_IL2CPP_Build_Guide.md) for Player verification
and [10_Architecture](10_Architecture.md) for generator and runtime ownership.
