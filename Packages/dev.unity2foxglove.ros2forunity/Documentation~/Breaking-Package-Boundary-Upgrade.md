# Breaking Package-Boundary Upgrade

This package boundary removes ROS-specific endpoint, QoS, schema, CDR, and runtime concepts
from `dev.unity2foxglove.sdk`. FoxRun declarations now select transports by
stable Provider ID and express only portable delivery intent.

There is no compatibility facade. Existing source must be migrated before it
will compile.

## Package selection

- Keep `dev.unity2foxglove.sdk` for Foxglove WebSocket, MCAP, replay, and
  transport-neutral FoxRun.
- Install `dev.unity2foxglove.ros2forunity` plus exactly one compatible runtime
  package for direct typed R2FU publish/subscribe.
- Install `dev.unity2foxglove.ros2bridge` for the U2R2 sidecar Bridge path.
  The Bridge package depends only on the SDK and does not require R2FU.

## FoxRun declaration migration

Replace the removed `Source`, `Targets`, and `FoxRunEndpoint` API with stable
Provider IDs:

| Former route | New Provider ID |
| --- | --- |
| Foxglove WebSocket | `foxglove.websocket` |
| ROS2 For Unity native | `unity2foxglove.r2fu` |
| ROS2 Bridge sidecar | `unity2foxglove.ros2bridge` |

Before:

```csharp
[FoxRun(
    "/robot/state",
    Mode = FoxRunFlow.PublishAndSubscribe,
    Source = FoxRunEndpoint.Ros2Native,
    Targets = FoxRunEndpoint.Foxglove | FoxRunEndpoint.Ros2Native,
    QoS = FoxRunQosProfile.SensorData)]
private RobotState _state;
```

After:

```csharp
[FoxRun(
    "/robot/state",
    Mode = FoxRunFlow.PublishAndSubscribe,
    SubscribeTransportId = "unity2foxglove.r2fu",
    PublishTransportIds = new[]
    {
        "foxglove.websocket",
        "unity2foxglove.r2fu"
    },
    Reliability = FoxRunDeliveryReliability.BestEffort,
    Durability = FoxRunDeliveryDurability.Volatile,
    History = FoxRunDeliveryHistory.KeepLast,
    Depth = 5)]
private RobotState _state;
```

Omit `SubscribeTransportId` or `PublishTransportIds` only when the Manager's
frozen directional Provider selection is intended. An explicit publish array
must be non-empty and contain unique canonical IDs.

## QoS and runtime ownership

The removed SDK types `FoxRunQosProfile`, `FoxRunQosReliability`,
`FoxRunQosDurability`, `FoxRunQosHistory`, and `FoxRunResolvedQos` have no core
replacement. Use the portable `FoxRunDelivery*` fields above. R2FU and Bridge
map that intent into their own local QoS models and reject unsupported
combinations.

R2FU-specific copy budgets, generated ROS interface identity, typesupport
preflight, subscription/publisher hubs, and route diagnostics now live in
`dev.unity2foxglove.ros2forunity`. U2R2 frames, CDR codecs, ROS message
builders, and Bridge diagnostics now live in
`dev.unity2foxglove.ros2bridge`.

## Scene and script checklist

1. Update every `[FoxRun]` and `[FoxRunMessage]` declaration to Provider IDs.
2. Ensure each `FoxgloveManager` has the intended installed Provider component
   and select the publish/subscribe defaults in its Inspector.
3. Replace SDK namespace imports for moved R2FU or Bridge types.
4. Regenerate FoxRun sources after Unity finishes importing all installed
   Provider analyzers.
5. Confirm the generated core partial contains no ROS symbols and that each
   installed Provider contributes its own separately named partial.
