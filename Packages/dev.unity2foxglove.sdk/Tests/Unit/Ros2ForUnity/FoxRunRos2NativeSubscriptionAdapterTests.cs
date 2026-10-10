// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Ros2ForUnity
// Purpose: Verify typed native subscription binding lifecycle and ownership.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using Unity2Foxglove.Ros2ForUnity.Native;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Ros2ForUnity
{
    [Trait("Phase", "179-C")]
    [Trait("Domain", "Ros2NativeSubscription")]
    public sealed partial class FoxRunRos2NativeSubscriptionAdapterTests
    {
        [Fact]
        public void BindingUsesExplicitTypedDelegatesAndMovesThroughReceiving()
        {
            var generation = 7L;
            var backend = new FakeBackend();
            FakeMessage applied = null;
            var binding = CreateBinding(
                backend,
                generation,
                () => generation,
                value => applied = value,
                value =>
                {
                    if (!ReferenceEquals(applied, value))
                        return false;
                    applied = null;
                    return true;
                });

            Assert.Equal(FoxRunRos2SubscriptionBindingState.Configured, binding.State);
            binding.WaitForRuntime();
            Assert.Equal(FoxRunRos2SubscriptionBindingState.WaitingForRuntime, binding.State);

            var registration = binding.TryRegister();
            Assert.True(registration.Succeeded);
            Assert.Equal(FoxRunRos2RegistrationError.None, registration.Error);
            Assert.Equal(FoxRunRos2SubscriptionBindingState.Ready, binding.State);

            using var borrowed = Message("alpha");
            backend.Invoke(borrowed);
            Assert.Equal(FoxRunRos2SubscriptionBindingState.Receiving, binding.State);
            Assert.True(binding.TryApplyLatest(generation));
            Assert.NotSame(borrowed, applied);
            Assert.Equal("alpha", applied.Data);

            binding.Stop();
            Assert.Null(applied);
            Assert.Equal(FoxRunRos2SubscriptionBindingState.Stopped, binding.State);
        }

        [Fact]
        public void InboundInspectionFailureRollsBackTheCreatedSubscription()
        {
            var driver = new InspectionFailureNodeDriver();
            var owner = new Ros2ForUnityFoxRunNodeOwner(driver);
            var backend = owner.AcquireBackend();

            var result = backend.Register<FakeMessage>(
                Contract(),
                new ManagedQosProfile(),
                _ => { });

            Assert.False(result.Succeeded);
            Assert.Equal(FoxRunRos2RegistrationError.BackendFailure, result.Error);
            Assert.Equal(1, driver.CreateSubscriptionCount);
            Assert.Equal(1, driver.RemoveSubscriptionCount);

            backend.ReleaseNodeOwnership();
            owner.ReleaseHostOwnership();
            Assert.Equal(1, driver.ReleaseNodeCount);
        }

        [Fact]
        public void NativeBinaryLoadFailureUsesDedicatedNonRetryableError()
        {
            var backend = new FakeBackend
            {
                RegistrationException = new TargetInvocationException(
                    new DllNotFoundException("native surface unavailable"))
            };
            var binding = CreateBinding(backend, 306, () => 306, _ => { }, _ => false);

            var result = binding.TryRegister();

            Assert.False(result.Succeeded);
            Assert.Equal(
                FoxRunRos2RegistrationError.NativeRuntimeSurfaceUnavailable,
                result.Error);
            Assert.Equal("DllNotFoundException", result.FailureKind);
            Assert.False(binding.CanRetryRegistration);
            Assert.Equal(0, backend.RegisterCount);
            binding.Stop();
        }

        [Fact]
        public void InboundInspectionFailureRetainsTokenWhenRollbackFails()
        {
            var driver = new InspectionFailureNodeDriver
            {
                RemoveSubscriptionFailure = new InvalidOperationException("rollback pending")
            };
            var owner = new Ros2ForUnityFoxRunNodeOwner(driver);
            var backend = owner.AcquireBackend();

            var result = backend.Register<FakeMessage>(
                Contract(),
                new ManagedQosProfile(),
                _ => { });

            Assert.False(result.Succeeded);
            Assert.Equal(FoxRunRos2RegistrationError.BackendFailure, result.Error);
            Assert.NotNull(result.Token);
            Assert.Equal(1, driver.CreateSubscriptionCount);
            Assert.Equal(1, driver.RemoveSubscriptionCount);

            driver.RemoveSubscriptionFailure = null;
            backend.RemoveSubscription(result.Token);
            Assert.Equal(2, driver.RemoveSubscriptionCount);

            backend.ReleaseNodeOwnership();
            owner.ReleaseHostOwnership();
            Assert.Equal(1, driver.ReleaseNodeCount);
        }
        [Fact]
        public void FatalInboundInspectionFailureRollsBackAndEscapesInsteadOfBecomingBackendFailure()
        {
            var driver = new InspectionFailureNodeDriver(new OutOfMemoryException("inspection fatal"));
            var owner = new Ros2ForUnityFoxRunNodeOwner(driver);
            var backend = owner.AcquireBackend();

            var thrown = Record.Exception(() => backend.Register<FakeMessage>(
                Contract(),
                new ManagedQosProfile(),
                _ => { }));

            Assert.IsType<OutOfMemoryException>(thrown);
            Assert.Contains(
                nameof(InspectionFailureNodeDriver.IsSubscriptionUsable),
                thrown.StackTrace,
                StringComparison.Ordinal);
            Assert.Equal(1, driver.CreateSubscriptionCount);
            Assert.Equal(1, driver.RemoveSubscriptionCount);

            backend.ReleaseNodeOwnership();
            owner.ReleaseHostOwnership();
            Assert.Equal(1, driver.ReleaseNodeCount);
        }

        [Fact]
        public void DiagnosticSnapshotReportsLivePendingAndExactOwnershipCounters()
        {
            var backend = new FakeBackend();
            FakeMessage applied = null;
            var binding = CreateBinding(
                backend,
                71,
                () => 71,
                value => applied = value,
                value =>
                {
                    if (!ReferenceEquals(applied, value))
                        return false;
                    applied = null;
                    return true;
                });
            Assert.True(binding.TryRegister().Succeeded);

            using var first = Message("first");
            using var latest = Message("latest");
            backend.Invoke(first);
            backend.Invoke(latest);

            Assert.True(binding.TryGetSnapshot(71, out var pending));
            Assert.Equal(2, pending.Received);
            Assert.Equal(1, pending.Replaced);
            Assert.Equal(0, pending.Applied);
            Assert.Equal(1, pending.Pending);
            Assert.Equal("/native/string", pending.Topic);
            Assert.Equal("Demo.Receiver", pending.DeclaringType);
            Assert.Equal("_incoming", pending.MemberName);
            Assert.Equal("std_msgs/msg/String", pending.CanonicalRosType);
            Assert.Equal(
                FoxRunResolvedQos.Default,
                pending.Qos);
            Assert.Equal(
                FoxRunQosProfile.Default,
                pending.Qos.Profile);
            Assert.Equal(
                FoxRunQosReliability.Reliable,
                pending.Qos.Reliability);
            Assert.Equal(
                FoxRunQosDurability.Volatile,
                pending.Qos.Durability);
            Assert.Equal(
                FoxRunQosHistory.KeepLast,
                pending.Qos.History);
            Assert.Equal(10, pending.Qos.Depth);
            Assert.True(pending.LastReceiveStopwatchTimestamp > 0);
            Assert.Equal(0, pending.LastApplyStopwatchTimestamp);

            Assert.True(binding.TryApplyLatest(71));
            Assert.Equal("latest", applied.Data);
            Assert.True(binding.TryGetSnapshot(71, out var drained));
            Assert.Equal(2, drained.Received);
            Assert.Equal(1, drained.Replaced);
            Assert.Equal(1, drained.Applied);
            Assert.Equal(0, drained.Pending);
            Assert.True(drained.LastReceiveStopwatchTimestamp > 0);
            Assert.True(drained.LastApplyStopwatchTimestamp > 0);
            binding.Stop();
        }

        [Fact]
        [Trait("Phase", "187-R2-H03-003")]
        public void RecoverableBackendFailureRetriesOnTheNextRegistrationAttempt()
        {
            var backend = new FakeBackend();
            backend.EnqueueRegistration(FoxRunRos2NativeBackendRegistration.Failure(
                FoxRunRos2RegistrationError.BackendFailure,
                "temporary backend failure"));
            backend.EnqueueRegistration(FoxRunRos2NativeBackendRegistration.Success(new FakeToken()));
            var binding = CreateBinding(backend, 303, () => 303, _ => { }, _ => false);

            var first = binding.TryRegister();
            Assert.False(first.Succeeded);
            Assert.Equal(FoxRunRos2RegistrationError.BackendFailure, first.Error);
            Assert.Equal(FoxRunRos2SubscriptionBindingState.Failed, binding.State);

            var second = binding.TryRegister();
            Assert.True(second.Succeeded);
            Assert.Equal(FoxRunRos2SubscriptionBindingState.Ready, binding.State);
            Assert.Equal(2, backend.RegisterCount);
            binding.Stop();
        }

        [Fact]
        public void FailedRegistrationRetainsRollbackTokenBeforeRetrying()
        {
            var backend = new FakeBackend
            {
                Next = FoxRunRos2NativeBackendRegistration.Success(new FakeToken(false)),
                RemoveException = new InvalidOperationException("rollback pending")
            };
            var binding = CreateBinding(backend, 305, () => 305, _ => { }, _ => false);

            var first = binding.TryRegister();

            Assert.False(first.Succeeded);
            Assert.Equal(1, backend.RemoveCount);
            Assert.True(binding.CanRetryRegistration);

            backend.RemoveException = null;
            backend.Next = FoxRunRos2NativeBackendRegistration.Success(new FakeToken());

            var second = binding.TryRegister();

            Assert.True(second.Succeeded);
            Assert.Equal(2, backend.RegisterCount);
            Assert.Equal(2, backend.RemoveCount);
            binding.Stop();
        }

        [Fact]
        [Trait("Phase", "187-R2-H03-003")]
        public void RecoverableRegistrationRetryStopsAtTheFiniteAttemptBound()
        {
            var backend = new FakeBackend();
            for (var i = 0; i < 5; i++)
                backend.EnqueueRegistration(FoxRunRos2NativeBackendRegistration.Failure(
                    FoxRunRos2RegistrationError.BackendFailure,
                    "persistent backend failure"));
            var binding = CreateBinding(backend, 304, () => 304, _ => { }, _ => false);

            for (var i = 0; i < 4; i++)
            {
                var result = binding.TryRegister();
                Assert.False(result.Succeeded);
                Assert.Equal(FoxRunRos2RegistrationError.BackendFailure, result.Error);
            }
            var terminal = binding.TryRegister();
            Assert.False(terminal.Succeeded);
            Assert.Equal(4, backend.RegisterCount);
            Assert.False(binding.CanRetryRegistration);
            binding.Stop();
        }

        [Fact]
        public void AcceptanceArmRejectsOldPendingOwnership()
        {
            var backend = new FakeBackend();
            var binding = CreateBinding(backend, 72, () => 72, _ => { }, _ => false);
            Assert.True(binding.TryRegister().Succeeded);
            using var old = Message("old-pending");
            backend.Invoke(old);

            Assert.Equal(
                FoxRunRos2AcceptanceArmStatus.PendingNotIdle,
                binding.ArmAcceptanceAttempt(out _));
            Assert.True(binding.TryApplyLatest(72));
            binding.Stop();
        }

        [Fact]
        public void PreArmInFlightCallbackIsExcludedAndMakesArmFail()
        {
            using var copyEntered = new ManualResetEventSlim();
            using var releaseCopy = new ManualResetEventSlim();
            var backend = new FakeBackend();
            var binding = CreateBinding(
                backend,
                73,
                () => 73,
                _ => { },
                _ => false,
                copy: value =>
                {
                    copyEntered.Set();
                    Assert.True(releaseCopy.Wait(TimeSpan.FromSeconds(10)));
                    return Message(value.Data);
                });
            Assert.True(binding.TryRegister().Succeeded);
            using var borrowed = Message("pre-arm");
            var callback = new Thread(() => backend.Invoke(borrowed)) { IsBackground = true };
            callback.Start();
            Assert.True(copyEntered.Wait(TimeSpan.FromSeconds(5)));

            Assert.Equal(
                FoxRunRos2AcceptanceArmStatus.CallbackInFlight,
                binding.ArmAcceptanceAttempt(out _));
            releaseCopy.Set();
            Assert.True(callback.Join(TimeSpan.FromSeconds(5)));
            Assert.True(binding.TryApplyLatest(73));
            Assert.Equal(
                FoxRunRos2AcceptanceArmStatus.Armed,
                binding.ArmAcceptanceAttempt(out var armed));
            Assert.Equal(0, armed.Received);
            Assert.Equal(0, armed.Replaced);
            Assert.Equal(0, armed.Applied);
            Assert.True(binding.EndAcceptanceAttempt(armed.Epoch));
            binding.Stop();
        }

        [Fact]
        public void HistoricalReplacementCannotSatisfyANewAcceptanceAttempt()
        {
            var backend = new FakeBackend();
            var binding = CreateBinding(backend, 74, () => 74, _ => { }, _ => false);
            Assert.True(binding.TryRegister().Succeeded);
            using var historicalFirst = Message("historical-1");
            using var historicalLatest = Message("historical-2");
            backend.Invoke(historicalFirst);
            backend.Invoke(historicalLatest);
            Assert.True(binding.TryApplyLatest(74));

            Assert.Equal(
                FoxRunRos2AcceptanceArmStatus.Armed,
                binding.ArmAcceptanceAttempt(out var armed));
            using var onlyFinal = Message("current-final-only");
            backend.Invoke(onlyFinal);
            Assert.True(binding.TryApplyLatest(74));
            Assert.True(binding.TryGetAcceptanceAttempt(out var attempt));
            Assert.Equal(1, attempt.Received);
            Assert.Equal(0, attempt.Replaced);
            Assert.Equal(1, attempt.Applied);
            Assert.False(attempt.IsSingleApplyLatestWinsComplete);
            Assert.True(binding.TryCompleteAcceptanceAttempt(armed.Epoch, out var completed));
            Assert.False(completed.IsSingleApplyLatestWinsComplete);
            Assert.True(binding.EndAcceptanceAttempt(armed.Epoch));
            using var afterFailedAttempt = Message("after-failed-attempt");
            backend.Invoke(afterFailedAttempt);
            Assert.True(binding.TryApplyLatest(74));
            binding.Stop();
        }

        [Fact]
        public void TwoMainThreadAppliesFailTheSingleApplyAttemptGate()
        {
            var backend = new FakeBackend();
            var binding = CreateBinding(backend, 75, () => 75, _ => { }, _ => false);
            Assert.True(binding.TryRegister().Succeeded);
            Assert.Equal(
                FoxRunRos2AcceptanceArmStatus.Armed,
                binding.ArmAcceptanceAttempt(out var armed));
            using var first = Message("first");
            using var replaced = Message("replaced");
            backend.Invoke(first);
            backend.Invoke(replaced);
            Assert.True(binding.TryApplyLatest(75));
            using var secondApply = Message("second-apply");
            backend.Invoke(secondApply);
            Assert.True(binding.TryApplyLatest(75));

            Assert.True(binding.TryGetAcceptanceAttempt(out var attempt));
            Assert.Equal(3, attempt.Received);
            Assert.Equal(1, attempt.Replaced);
            Assert.Equal(2, attempt.Applied);
            Assert.False(attempt.IsSingleApplyLatestWinsComplete);
            Assert.True(binding.EndAcceptanceAttempt(armed.Epoch));
            binding.Stop();
        }

    }
}
#endif
