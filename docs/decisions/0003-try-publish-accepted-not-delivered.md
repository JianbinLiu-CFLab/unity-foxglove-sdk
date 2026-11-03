# ADR-0003: `TryPublish == true` means middleware acceptance

## Context

A ROS2 publisher can successfully hand a message to the configured middleware
while no peer is subscribed or while peer observation is not available to the
SDK. Treating the return value as end-to-end delivery would overstate the
runtime guarantee.

## Decision

`IUnity2FoxgloveRos2Publisher<T>.TryPublish` returns `true` only when the local
middleware accepts the publish call. It does not report subscriber count,
network delivery, or peer observation. `false` reports local rejection through
`error`.

## Consequence

Applications and acceptance tests must use a separate peer-observation signal
when they need delivery evidence. The API remains truthful for zero-subscriber
and disconnected-peer cases.

## Pinning test and contract

The public contract is documented in
`Packages/dev.unity2foxglove.ros2forunity/README.md` under “Publish result
contract”, and the interface is
`Packages/dev.unity2foxglove.ros2forunity/Runtime/IUnity2FoxgloveRos2Publisher.cs`.
Peer-observation acceptance remains a separate lane.
