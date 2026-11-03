# ADR-0001: Native runtime surface failures are contract-local

## Context

The optional ROS2 native surface can be unavailable because the runtime package,
ABI, or native entrypoint is missing. Treating that condition as a process-wide
fatal error prevents the rest of the SDK from continuing with its configured
capabilities.

## Decision

Report the condition as `NativeRuntimeSurfaceUnavailable` at the ROS2 registration
boundary. Keep the failure local to the affected native registration or binding;
do not convert it into a global fatal path.

## Consequence

Callers can classify the unavailable native surface, preserve cleanup ownership,
and continue unrelated SDK work. A later retry remains explicit and observable.

## Pinning test

`Packages/dev.unity2foxglove.sdk/Tests/Unit/Ros2ForUnity/FoxRunRos2NativeSubscriptionAdapterTests.cs`
asserts the registration error identity. The corresponding publisher and session
tracker tests pin the same boundary for other native registration paths.
