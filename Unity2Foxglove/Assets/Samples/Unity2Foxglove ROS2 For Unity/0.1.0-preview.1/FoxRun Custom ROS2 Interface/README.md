# FoxRun Custom ROS2 Interface

This source-only sample demonstrates Phase181 custom FoxRun DTO transport for
the selected ROS2 For Unity runtime and its matching static typesupport add-on.
This is the optional ROS2 Native path. Normal Foxglove WebSocket and
localhost-sidecar Bridge projects use `dev.unity2foxglove.sdk` alone; Bridge is
publish-only and is not exercised by this sample.

It contains three independent contracts:

- **Native Publish** explicitly selects
  `Targets = FoxRunEndpoint.Ros2Native` with official
  `QoS = FoxRunQosProfile.Default`.
- **Native Subscribe** explicitly selects
  `Source = FoxRunEndpoint.Ros2Native` and applies a managed DTO on Unity's
  main thread.
- **Native PublishAndSubscribe** explicitly selects native ROS2 as its source
  and Foxglove JSON as its output target.

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
4. Enable native ROS2 output and/or FoxRun subscriptions according to the
   direction being exercised.

For Windows-local bring-up, use the matching distro's Phase181 peer helper
after the Unity scene has reported its custom-interface READY marker. Linux and
Player evidence remain separate release-gate rows rather than being inferred
from this local Editor sample.

The component intentionally calls no ROS2 API. Generated bindings own node,
subscription, deep-copy, and teardown behavior; Inspector-facing sample state
is ordinary managed DTO data only.
