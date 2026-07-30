# FoxRun Custom ROS2 Interface

This source-only sample demonstrates Phase181 custom FoxRun DTO transport for
the selected ROS2 For Unity runtime and its matching static typesupport add-on.
This is the optional ROS2 Native Provider path. Foxglove WebSocket needs only
`dev.unity2foxglove.sdk`; the localhost sidecar Bridge instead uses the
independent `dev.unity2foxglove.ros2bridge` package and is not exercised here.

It contains three independent contracts:

- **Native Publish** selects
  `PublishTransportIds = new[] { FoxRunRos2TransportProvider.IdValue }`.
- **Native Subscribe** selects
  `SubscribeTransportId = FoxRunRos2TransportProvider.IdValue` and applies a
  managed DTO on Unity's main thread.
- **Native PublishAndSubscribe** selects the R2FU Provider for input and
  `FoxgloveWebSocketTransport.Id` for JSON output.

The full-duplex declaration is a diagnostic integration example. Prefer
separate one-way declarations when production ownership must be unambiguous.

## Static interface lock and identity

The DTO namespace, type names, and public member shapes in
`Phase181FoxRunCustomRos2Interface.cs` intentionally match the locked v1
interface in `dev.unity2foxglove.foxrun.ros2.interfaces`. The source generator
uses that identity to select
`unity2foxglove_foxrun_interfaces_v1/msg/Phase181State48D288ED82F1Envelope`.

Do not rename or reshape those DTOs locally. Make an explicit interface
revision and rebuild the matching Humble, Jazzy, or Lyrical typesupport add-on
instead. A matching runtime package plus add-on must be active before native
custom DTO contracts can register.

## Import and use

1. Install the matching static interface package and exactly one matching
   distro-specific typesupport add-on.
2. Select the same ROS2 For Unity runtime/RMW in the Manager's **Data
   Transport > ROS 2 Native Runtime (R2FU) — Shared** section.
3. Import this sample and add `Phase181FoxRunCustomRos2Interface` to a scene
   with a `FoxgloveManager`.
4. In the Manager, select `unity2foxglove.r2fu` for the required publish and/or
   subscribe direction. The Inspector creates the hidden same-GameObject
   Provider companion and enables subscriptions when requested.

For Windows-local bring-up, use the matching distro's Phase181 peer helper
after the Unity scene has reported its custom-interface READY marker. Linux and
Player evidence remain separate release-gate rows rather than being inferred
from this local Editor sample.

The component intentionally calls no ROS2 API. Generated bindings own node,
subscription, deep-copy, and teardown behavior; Inspector-facing sample state
is ordinary managed DTO data only.
