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
    public sealed partial class FoxRunRos2NativeSubscriptionAdapterTests
    {
        [Fact]
        public void OneApplyAfterRealReplacementBurstPassesAttemptAccounting()
        {
            var backend = new FakeBackend();
            var binding = CreateBinding(backend, 76, () => 76, _ => { }, _ => false);
            Assert.True(binding.TryRegister().Succeeded);
            Assert.Equal(
                FoxRunRos2AcceptanceArmStatus.Armed,
                binding.ArmAcceptanceAttempt(out var armed));
            using var first = Message("seq-0");
            using var final = Message("seq-1");
            backend.Invoke(first);
            backend.Invoke(final);
            Assert.True(binding.TryApplyLatest(76));

            Assert.True(binding.TryGetAcceptanceAttempt(out var attempt));
            Assert.Equal(2, attempt.Received);
            Assert.Equal(1, attempt.Replaced);
            Assert.Equal(1, attempt.Applied);
            Assert.Equal(0, attempt.Pending);
            Assert.Equal(0, attempt.CallbacksInFlight);
            Assert.True(attempt.IsSingleApplyLatestWinsComplete);
            Assert.True(binding.EndAcceptanceAttempt(armed.Epoch));
            binding.Stop();
        }

        [Fact]
        public void CompletionClosesAdmissionBeforeTakingAMutationStableSnapshot()
        {
            using var preCloseCopyEntered = new ManualResetEventSlim();
            using var releasePreCloseCopy = new ManualResetEventSlim();
            var postCloseCopyCalls = 0;
            var backend = new FakeBackend();
            var binding = CreateBinding(
                backend,
                77,
                () => 77,
                _ => { },
                _ => false,
                copy: value =>
                {
                    if (value.Data == "pre-close-block")
                    {
                        preCloseCopyEntered.Set();
                        Assert.True(releasePreCloseCopy.Wait(TimeSpan.FromSeconds(10)));
                        throw new InvalidOperationException("deliberate pre-close copy failure");
                    }
                    if (value.Data == "post-close-must-reject")
                        Interlocked.Increment(ref postCloseCopyCalls);
                    return Message(value.Data);
                });
            Assert.True(binding.TryRegister().Succeeded);
            Assert.Equal(
                FoxRunRos2AcceptanceArmStatus.Armed,
                binding.ArmAcceptanceAttempt(out var armed));
            using var first = Message("seq-0");
            using var final = Message("seq-1");
            backend.Invoke(first);
            backend.Invoke(final);
            Assert.True(binding.TryApplyLatest(77));

            using var preClose = Message("pre-close-block");
            var preCloseCallback = new Thread(() => backend.Invoke(preClose)) { IsBackground = true };
            preCloseCallback.Start();
            Assert.True(preCloseCopyEntered.Wait(TimeSpan.FromSeconds(5)));

            Assert.False(binding.TryCompleteAcceptanceAttempt(armed.Epoch, out _));
            using var postClose = Message("post-close-must-reject");
            var postCloseCallback = new Thread(() => backend.Invoke(postClose)) { IsBackground = true };
            postCloseCallback.Start();
            Assert.True(postCloseCallback.Join(TimeSpan.FromSeconds(5)));
            Assert.Equal(0, Volatile.Read(ref postCloseCopyCalls));

            releasePreCloseCopy.Set();
            Assert.True(preCloseCallback.Join(TimeSpan.FromSeconds(5)));
            Assert.True(binding.TryCompleteAcceptanceAttempt(armed.Epoch, out var completed));
            Assert.True(completed.IsSingleApplyLatestWinsComplete);
            Assert.Equal(2, completed.Received);
            Assert.Equal(1, completed.Replaced);
            Assert.Equal(1, completed.Applied);
            Assert.Equal(0, completed.Pending);
            Assert.Equal(0, completed.CallbacksInFlight);
            Assert.False(binding.TryApplyLatest(77));
            Assert.True(binding.EndAcceptanceAttempt(armed.Epoch));
            using var afterCompletedAttempt = Message("after-completed-attempt");
            backend.Invoke(afterCompletedAttempt);
            Assert.True(binding.TryApplyLatest(77));
            binding.Stop();
        }

        [Fact]
        public void RuntimeUnavailableStaysRetryableWhileUnsupportedAndFailuresAreTerminal()
        {
            var unavailable = new FakeBackend
            {
                Next = FoxRunRos2NativeBackendRegistration.Failure(
                    FoxRunRos2RegistrationError.RuntimeUnavailable,
                    "runtime is still warming")
            };
            var waiting = CreateBinding(unavailable, 1, () => 1, _ => { }, _ => false);
            var waitingResult = waiting.TryRegister();
            Assert.False(waitingResult.Succeeded);
            Assert.Equal(FoxRunRos2SubscriptionBindingState.WaitingForRuntime, waiting.State);

            var unsupported = new FakeBackend
            {
                Next = FoxRunRos2NativeBackendRegistration.Failure(
                    FoxRunRos2RegistrationError.UnsupportedMessageType,
                    "not packaged")
            };
            var unsupportedBinding = CreateBinding(unsupported, 1, () => 1, _ => { }, _ => false);
            Assert.False(unsupportedBinding.TryRegister().Succeeded);
            Assert.Equal(FoxRunRos2SubscriptionBindingState.Unsupported, unsupportedBinding.State);

            var failed = new FakeBackend
            {
                Next = FoxRunRos2NativeBackendRegistration.Failure(
                    FoxRunRos2RegistrationError.BackendFailure,
                    new string('x', 4096))
            };
            var failedBinding = CreateBinding(failed, 1, () => 1, _ => { }, _ => false);
            var failure = failedBinding.TryRegister();
            Assert.Equal(FoxRunRos2SubscriptionBindingState.Failed, failedBinding.State);
            Assert.InRange(failure.Diagnostic.Length, 1, FoxRunRos2RegistrationResult.MaximumDiagnosticLength);
        }

        [Fact]
        public void PublicDiagnosticsDoNotExposeBackendDetails()
        {
            const string sensitiveDetail = "zenoh-password=phase179-secret";
            const string expectedMessage =
                "The native ROS2 backend failed while operating the subscription.";
            var backend = new FakeBackend
            {
                Next = FoxRunRos2NativeBackendRegistration.Failure(
                    FoxRunRos2RegistrationError.BackendFailure,
                    sensitiveDetail)
            };
            var binding = CreateBinding(backend, 34, () => 34, _ => { }, _ => false);

            var registration = binding.TryRegister();

            Assert.False(registration.Succeeded);
            Assert.Equal(FoxRunRos2RegistrationError.BackendFailure, registration.Error);
            Assert.Equal(expectedMessage, registration.Diagnostic);
            Assert.DoesNotContain(sensitiveDetail, registration.Diagnostic, StringComparison.Ordinal);
            Assert.True(binding.TryGetSnapshot(34, out var bindingSnapshot));
            Assert.Equal(expectedMessage, bindingSnapshot.Diagnostic);
            Assert.DoesNotContain(sensitiveDetail, bindingSnapshot.Diagnostic, StringComparison.Ordinal);

            var boundarySnapshot = new FoxRunRos2SubscriptionBindingSnapshot(
                "public-boundary",
                34,
                FoxRunRos2SubscriptionBindingState.Failed,
                FoxRunRos2RegistrationError.BackendFailure,
                sensitiveDetail,
                0, 0, 0, 0, 0, 0, 0);
            Assert.Equal(expectedMessage, boundarySnapshot.Diagnostic);
            Assert.DoesNotContain(sensitiveDetail, boundarySnapshot.Diagnostic, StringComparison.Ordinal);

            var diagnostics = new FoxRunRos2SubscriptionDiagnostics();
            diagnostics.Update(
                "source:34|public-boundary",
                boundarySnapshot,
                FoxRunRos2RuntimeDiagnosticContext.Unknown);
            var published = Assert.Single(diagnostics.GetSnapshots());
            Assert.Equal("BackendFailure", published.LastErrorCode);
            Assert.Equal(expectedMessage, published.LastErrorMessage);
            Assert.DoesNotContain(sensitiveDetail, published.LastErrorMessage, StringComparison.Ordinal);
        }

        [Fact]
        public void InternalFailureKindRetainsOnlyTheBackendExceptionClass()
        {
            const string sensitiveDetail = "zenoh-password=phase181-secret";

            var failure = FoxRunRos2RegistrationResult.Failure(
                FoxRunRos2RegistrationError.PublisherBackendFailure,
                "ObjectDisposedException: " + sensitiveDetail);

            Assert.Equal(
                "The native ROS2 backend failed while operating the publisher.",
                failure.Diagnostic);
            Assert.Equal("ObjectDisposedException", failure.FailureKind);
            Assert.DoesNotContain(sensitiveDetail, failure.FailureKind, StringComparison.Ordinal);
        }

        [Fact]
        public void OnlyTheCurrentSuccessfulRegistrationAttemptCanPublish()
        {
            var backend = new FakeBackend();
            backend.EnqueueRegistration(FoxRunRos2NativeBackendRegistration.Failure(
                FoxRunRos2RegistrationError.RuntimeUnavailable,
                "warming"));
            backend.EnqueueRegistration(FoxRunRos2NativeBackendRegistration.Success(new FakeToken()));
            FakeMessage applied = null;
            var binding = CreateBinding(
                backend,
                2,
                () => 2,
                value => applied = value,
                value =>
                {
                    if (!ReferenceEquals(applied, value))
                        return false;
                    applied = null;
                    return true;
                });

            Assert.False(binding.TryRegister().Succeeded);
            using var failedAttemptMessage = Message("failed-attempt");
            backend.InvokeAttempt(0, failedAttemptMessage);
            Assert.Equal(0, binding.ReceivedCount);
            Assert.Equal(FoxRunRos2SubscriptionBindingState.WaitingForRuntime, binding.State);

            Assert.True(binding.TryRegister().Succeeded);
            Assert.Equal(FoxRunRos2SubscriptionBindingState.Ready, binding.State);
            using var oldAttemptMessage = Message("old-attempt");
            backend.InvokeAttempt(0, oldAttemptMessage);
            Assert.Equal(0, binding.ReceivedCount);
            Assert.Equal(FoxRunRos2SubscriptionBindingState.Ready, binding.State);
            Assert.False(binding.TryApplyLatest(2));

            using var currentAttemptMessage = Message("current-attempt");
            backend.InvokeAttempt(1, currentAttemptMessage);
            Assert.Equal(1, binding.ReceivedCount);
            Assert.Equal(FoxRunRos2SubscriptionBindingState.Receiving, binding.State);
            Assert.True(binding.TryApplyLatest(2));
            Assert.Equal("current-attempt", applied.Data);
            Assert.Equal(2, binding.StaleCallbackCount);
            binding.Stop();
        }

        [Fact]
        public void SynchronousCallbackBeforeTokenAcceptanceIsRejected()
        {
            using var synchronous = Message("synchronous");
            var backend = new FakeBackend { SynchronousMessage = synchronous };
            var binding = CreateBinding(backend, 2, () => 2, _ => { }, _ => false);

            Assert.True(binding.TryRegister().Succeeded);

            Assert.Equal(0, binding.ReceivedCount);
            Assert.Equal(1, binding.StaleCallbackCount);
            Assert.Equal(FoxRunRos2SubscriptionBindingState.Ready, binding.State);
            Assert.False(binding.TryApplyLatest(2));
            binding.Stop();
        }

        [Fact]
        public void BackendRegisterCanReenterStopWithoutDeadlockAndLateTokenRollsBack()
        {
            var backend = new FakeBackend();
            FoxRunRos2SubscriptionBinding<FakeMessage> binding = null;
            backend.DuringRegister = () => binding.Stop();
            binding = CreateBinding(backend, 2, () => 2, _ => { }, _ => false);

            FoxRunRos2RegistrationResult result = default;
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try { result = binding.TryRegister(); }
                catch (Exception exception) { failure = exception; }
            }) { IsBackground = true };
            thread.Start();

            Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
            Assert.Null(failure);
            Assert.False(result.Succeeded);
            Assert.Equal(FoxRunRos2RegistrationError.Stopped, result.Error);
            Assert.Equal(FoxRunRos2SubscriptionBindingState.Stopped, binding.State);
            Assert.Equal(1, backend.RemoveCount);
            Assert.Equal(1, backend.ReleaseCount);
        }

        [Fact]
        public void FatalLateTokenRollbackStillReleasesDeferredNodeOwnership()
        {
            var events = new List<string>();
            var backend = new FakeBackend(events)
            {
                RemoveException = new OutOfMemoryException("rollback-primary")
            };
            FoxRunRos2SubscriptionBinding<FakeMessage> binding = null;
            backend.DuringRegister = () => binding.Stop();
            binding = CreateBinding(backend, 2, () => 2, _ => { }, _ => false);

            var thrown = Assert.Throws<OutOfMemoryException>(() => binding.TryRegister());

            Assert.Equal("rollback-primary", thrown.Message);
            Assert.Equal(
                new[] { "remove-subscription" },
                events);
            Assert.Equal(1, backend.RemoveCount);
            Assert.Equal(0, backend.ReleaseCount);

            backend.RemoveException = null;
            binding.Stop();

            Assert.Equal(
                new[] { "remove-subscription", "remove-subscription", "release-node" },
                events);
            Assert.Equal(2, backend.RemoveCount);
            Assert.Equal(1, backend.ReleaseCount);
        }

        [Fact]
        public void RecoverableNodeReleaseFailureRetainsOwnershipForRetry()
        {
            var events = new List<string>();
            var backend = new FakeBackend(events)
            {
                ReleaseException = new InvalidOperationException("release is temporarily unavailable")
            };
            var binding = CreateBinding(backend, 2, () => 2, _ => { }, _ => false);

            Assert.True(binding.TryRegister().Succeeded);
            binding.Stop();

            Assert.Equal(1, backend.ReleaseCount);
            Assert.False(((IFoxRunRos2DeferredCleanupStatus)binding).CleanupComplete);

            backend.ReleaseException = null;
            binding.Stop();

            Assert.Equal(2, backend.ReleaseCount);
            Assert.True(((IFoxRunRos2DeferredCleanupStatus)binding).CleanupComplete);
        }

        [Fact]
        public void SubscriptionHubTeardownContinuesAfterFatalHostedBinding()
        {
            var stopOrder = new List<string>();
            var bindings = new IFoxRunRos2SubscriptionHostedCleanup[]
            {
                new FakeHostedCleanup(
                    "first",
                    stopOrder,
                    new OutOfMemoryException("first-primary")),
                new FakeHostedCleanup("second", stopOrder, null)
            };

            var thrown = Assert.Throws<OutOfMemoryException>(() =>
                FoxRunRos2SubscriptionHub.StopHostedBindings(
                    bindings,
                    _ => { }));

            Assert.Equal("first-primary", thrown.Message);
            Assert.Equal(new[] { "first", "second" }, stopOrder);
        }

        [Fact]
        public void SubscriptionHubDrainsDeferredCleanupBeforeReleasingHostedBindings()
        {
            var cleanupOrder = new List<string>();
            var queue = new FoxRunRos2HostCleanupQueue(
                Thread.CurrentThread.ManagedThreadId);
            var binding = new FakeDeferredHostedCleanup(
                queue,
                cleanupOrder,
                dispatchCleanup: true);

            FoxRunRos2SubscriptionHub.StopHostedBindingsAndDrainDeferredCleanup(
                new IFoxRunRos2SubscriptionHostedCleanup[] { binding },
                queue,
                TimeSpan.FromSeconds(1),
                _ => { },
                out var cleanupComplete);

            Assert.True(cleanupComplete);
            Assert.Equal(new[] { "stop", "cleanup" }, cleanupOrder);
        }

        [Fact]
        public void SubscriptionHubDeferredCleanupTimeoutRemainsExplicit()
        {
            var cleanupOrder = new List<string>();
            var queue = new FoxRunRos2HostCleanupQueue(
                Thread.CurrentThread.ManagedThreadId);
            var binding = new FakeDeferredHostedCleanup(
                queue,
                cleanupOrder,
                dispatchCleanup: false);

            FoxRunRos2SubscriptionHub.StopHostedBindingsAndDrainDeferredCleanup(
                new IFoxRunRos2SubscriptionHostedCleanup[] { binding },
                queue,
                TimeSpan.Zero,
                _ => { },
                out var cleanupComplete);

            Assert.False(cleanupComplete);
            Assert.Equal(new[] { "stop" }, cleanupOrder);
        }

    }
}
#endif
