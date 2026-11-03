# ADR-0002: Interactive selection timeout retains transaction ownership

## Context

An interactive ROS2 runtime selection can expire before the editor completes
registration. Releasing ownership at timeout would allow a late callback to
mutate a replacement selection and would remove the user's ability to retry or
cancel the pending transaction coherently.

## Decision

A timeout transitions the owned selection to `TimedOut` but retains transaction
ownership. The owner may explicitly retry, which creates a new generation, or
cancel, which performs rollback. Late callbacks cannot complete a replacement
generation.

## Consequence

Timeout handling is deterministic and recoverable. Retry/cancel remain visible
state transitions instead of implicit cleanup races.

## Pinning test

`Packages/dev.unity2foxglove.sdk/Tests/Unit/Harness/WorkstreamBClosureTests.cs`
pins `MarkTimedOut`, retained ownership, retry generation, and cancellation
behaviour through the injectable selection coordinator/lifecycle tests.
