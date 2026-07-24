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
using System.Threading;
using Unity2Foxglove.Ros2ForUnity.Native;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Ros2ForUnity
{
    [Trait("Phase", "179-C")]
    [Trait("Domain", "Ros2NativeSubscription")]
    public sealed class FoxRunRos2NativeSubscriptionAdapterTests
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
                Unity.FoxgloveSDK.Components.FoxRunResolvedQos.Default,
                pending.Qos);
            Assert.Equal(
                Unity.FoxgloveSDK.Components.FoxRunQosProfile.Default,
                pending.Qos.Profile);
            Assert.Equal(
                Unity.FoxgloveSDK.Components.FoxRunQosReliability.Reliable,
                pending.Qos.Reliability);
            Assert.Equal(
                Unity.FoxgloveSDK.Components.FoxRunQosDurability.Volatile,
                pending.Qos.Durability);
            Assert.Equal(
                Unity.FoxgloveSDK.Components.FoxRunQosHistory.KeepLast,
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
        public void StopDoesNotWaitForBlockedRegisterAndDeferredReleaseIsUnique()
        {
            using var registerEntered = new ManualResetEventSlim();
            using var releaseRegister = new ManualResetEventSlim();
            var backend = new FakeBackend
            {
                RegisterEntered = registerEntered,
                ReleaseRegister = releaseRegister
            };
            var binding = CreateBinding(backend, 2, () => 2, _ => { }, _ => false);
            FoxRunRos2RegistrationResult registration = default;
            var registerThread = new Thread(() => registration = binding.TryRegister()) { IsBackground = true };
            registerThread.Start();
            Assert.True(registerEntered.Wait(TimeSpan.FromSeconds(5)));

            var stopThread = new Thread(binding.Stop) { IsBackground = true };
            stopThread.Start();

            Assert.True(stopThread.Join(TimeSpan.FromSeconds(2)));
            Assert.Equal(FoxRunRos2SubscriptionBindingState.Stopped, binding.State);
            Assert.Equal(0, backend.ReleaseCount);
            releaseRegister.Set();
            Assert.True(registerThread.Join(TimeSpan.FromSeconds(5)));
            Assert.False(registration.Succeeded);
            Assert.Equal(FoxRunRos2RegistrationError.Stopped, registration.Error);
            Assert.Equal(1, backend.RemoveCount);
            Assert.Equal(1, backend.ReleaseCount);
            binding.Stop();
            Assert.Equal(1, backend.ReleaseCount);
        }

        [Fact]
        public void GenerationChangeWhileBackendRegistersRollsBackReturnedToken()
        {
            var generation = 21L;
            var backend = new FakeBackend { AfterRegister = () => generation = 22 };
            var binding = CreateBinding(backend, 21, () => generation, _ => { }, _ => false);

            var result = binding.TryRegister();

            Assert.False(result.Succeeded);
            Assert.Equal(FoxRunRos2RegistrationError.StaleGeneration, result.Error);
            Assert.Equal(1, backend.RemoveCount);
            Assert.NotEqual(FoxRunRos2SubscriptionBindingState.Ready, binding.State);
        }

        [Fact]
        public void SnapshotGenerationProviderCanReenterStopWithoutLockInversion()
        {
            var reenter = false;
            FoxRunRos2SubscriptionBinding<FakeMessage> binding = null;
            binding = CreateBinding(
                new FakeBackend(),
                31,
                () =>
                {
                    if (reenter)
                        binding.Stop();
                    return 31;
                },
                _ => { },
                _ => false);
            Assert.True(binding.TryRegister().Succeeded);
            reenter = true;
            var snapshotResult = true;
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try { snapshotResult = binding.TryGetSnapshot(31, out _); }
                catch (Exception exception) { failure = exception; }
            }) { IsBackground = true };
            thread.Start();

            Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
            Assert.Null(failure);
            Assert.False(snapshotResult);
            Assert.Equal(FoxRunRos2SubscriptionBindingState.Stopped, binding.State);
        }

        [Fact]
        public void CopyContextRentReusesWarmInstanceAndResetsInlineBudget()
        {
            var first = FoxRunRos2CopyContext.Rent(32);
            first.RequireBytes(12);
            first.Return();

            var second = FoxRunRos2CopyContext.Rent(48);

            Assert.Same(first, second);
            Assert.Equal(48, second.RemainingBytes);
            second.Return();
            var slotField = typeof(FoxRunRos2SubscriptionBinding<FakeMessage>)
                .GetField("_slot", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(slotField);
            Assert.Equal(typeof(object), slotField.FieldType.GetGenericArguments()[0]);
            Assert.DoesNotContain(
                typeof(FoxRunRos2SubscriptionBinding<FakeMessage>).GetNestedTypes(
                    System.Reflection.BindingFlags.NonPublic),
                type => type.Name.Contains("OwnedMessage", StringComparison.Ordinal));
            Assert.DoesNotContain(
                typeof(FoxRunRos2CopyContext).GetFields(
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic),
                field => field.FieldType == typeof(FoxRunRos2CopyBudget));
            Assert.Equal(
                typeof(Func<FakeMessage, object>),
                typeof(FoxRunRos2SubscriptionBinding<FakeMessage>)
                    .GetField("_copyBorrowed", System.Reflection.BindingFlags.Instance |
                                                   System.Reflection.BindingFlags.NonPublic)
                    ?.FieldType);
            Assert.Equal(
                typeof(Action<object>),
                typeof(FoxRunRos2SubscriptionBinding<FakeMessage>)
                    .GetField("_applyOwned", System.Reflection.BindingFlags.Instance |
                                                System.Reflection.BindingFlags.NonPublic)
                    ?.FieldType);
        }

        [Fact]
        public void WarmCallbackAddsNoAllocationBeyondTheRequiredOwnedGraph()
        {
            var backend = new FakeBackend();
            var owned = Message("warm");
            FakeMessage applied = null;
            var binding = CreateBinding(
                backend,
                34,
                () => 34,
                value => applied = value,
                value =>
                {
                    if (!ReferenceEquals(applied, value))
                        return false;
                    applied = null;
                    return true;
                },
                _ => owned);
            Assert.True(binding.TryRegister().Succeeded);
            using var borrowed = Message("borrowed");
            backend.Invoke(borrowed);
            Assert.True(binding.TryApplyLatest(34));
            owned = Message("measured");

            var before = GC.GetAllocatedBytesForCurrentThread();
            backend.Invoke(borrowed);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(0, allocated);
            binding.Stop();
        }

        [Fact]
        public void TeardownFailureIsObservableAndLaterCleanupStillRuns()
        {
            var events = new List<string>();
            var backend = new FakeBackend(events)
            {
                RemoveException = new InvalidOperationException("remove exploded"),
                ReleaseException = new InvalidOperationException("release exploded")
            };
            FakeMessage applied = null;
            var binding = CreateBinding(
                backend,
                32,
                () => 32,
                value => applied = value,
                value =>
                {
                    events.Add("clear-applied");
                    applied = null;
                    return true;
                },
                dispose: value =>
                {
                    events.Add("dispose-owned");
                    value.Dispose();
                });
            Assert.True(binding.TryRegister().Succeeded);
            using var borrowed = Message("owned");
            backend.Invoke(borrowed);
            Assert.True(binding.TryApplyLatest(32));

            binding.Stop();

            Assert.Equal(FoxRunRos2SubscriptionBindingState.Stopped, binding.State);
            Assert.True(binding.TryGetSnapshot(32, out var snapshot));
            Assert.Equal(FoxRunRos2RegistrationError.TeardownFailure, snapshot.Error);
            Assert.Equal("The native ROS2 subscription did not complete teardown.", snapshot.Diagnostic);
            Assert.Equal(
                new[] { "remove-subscription", "clear-applied", "dispose-owned", "release-node" },
                events);
            Assert.Equal(1, backend.ReleaseCount);
        }

        [Fact]
        public void DeferredNodeReleaseFailureIsRecordedAfterBlockedRegistrationCompletes()
        {
            using var registerEntered = new ManualResetEventSlim();
            using var releaseRegister = new ManualResetEventSlim();
            var backend = new FakeBackend
            {
                RegisterEntered = registerEntered,
                ReleaseRegister = releaseRegister,
                ReleaseException = new InvalidOperationException("deferred release")
            };
            var binding = CreateBinding(backend, 33, () => 33, _ => { }, _ => false);
            var registerThread = new Thread(() => binding.TryRegister()) { IsBackground = true };
            registerThread.Start();
            Assert.True(registerEntered.Wait(TimeSpan.FromSeconds(5)));
            binding.Stop();
            releaseRegister.Set();
            Assert.True(registerThread.Join(TimeSpan.FromSeconds(5)));

            Assert.True(binding.TryGetSnapshot(33, out var snapshot));
            Assert.Equal(FoxRunRos2RegistrationError.TeardownFailure, snapshot.Error);
            Assert.Equal("The native ROS2 subscription did not complete teardown.", snapshot.Diagnostic);
            Assert.Equal(1, backend.ReleaseCount);
        }

        [Fact]
        public void NullOrNoOpTokenCanNeverReportRegistrationSuccess()
        {
            var backend = new FakeBackend
            {
                Next = FoxRunRos2NativeBackendRegistration.Success(null)
            };
            var binding = CreateBinding(backend, 1, () => 1, _ => { }, _ => false);

            var result = binding.TryRegister();

            Assert.False(result.Succeeded);
            Assert.Equal(FoxRunRos2RegistrationError.InvalidSubscriptionToken, result.Error);
            Assert.Equal(FoxRunRos2SubscriptionBindingState.Failed, binding.State);

            var events = new List<string>();
            var noOpBackend = new FakeBackend(events)
            {
                Next = FoxRunRos2NativeBackendRegistration.Success(new FakeToken(false))
            };
            var noOpBinding = CreateBinding(noOpBackend, 1, () => 1, _ => { }, _ => false);
            var noOpResult = noOpBinding.TryRegister();
            Assert.False(noOpResult.Succeeded);
            Assert.Equal(FoxRunRos2RegistrationError.InvalidSubscriptionToken, noOpResult.Error);
            Assert.Equal(1, noOpBackend.RemoveCount);
            Assert.Equal(new[] { "remove-subscription" }, events);
        }

        [Fact]
        public void StaleGenerationCallbackAndDiagnosticsAreRejected()
        {
            var activeGeneration = 9L;
            var copied = 0;
            var backend = new FakeBackend();
            var binding = CreateBinding(
                backend,
                9,
                () => activeGeneration,
                _ => { },
                _ => false,
                source =>
                {
                    copied++;
                    return Message(source.Data);
                });
            Assert.True(binding.TryRegister().Succeeded);
            Assert.True(binding.TryGetSnapshot(9, out var currentSnapshot));
            Assert.Equal(binding.ContractId, currentSnapshot.ContractId);
            activeGeneration = 10;

            using var borrowed = Message("stale");
            backend.Invoke(borrowed);

            Assert.Equal(0, copied);
            Assert.Equal(1, binding.StaleCallbackCount);
            Assert.False(binding.TryApplyLatest(9));
            Assert.False(binding.TryApplyLatest(activeGeneration));
            Assert.False(binding.TryGetSnapshot(9, out _));
            Assert.False(binding.TryGetSnapshot(activeGeneration, out _));
        }

        [Fact]
        public void GenerationProviderFailureRejectsDrainAndDiagnostics()
        {
            var throwProvider = false;
            var backend = new FakeBackend();
            var binding = CreateBinding(
                backend,
                11,
                () => throwProvider ? throw new InvalidOperationException("generation") : 11,
                _ => { },
                _ => false);
            Assert.True(binding.TryRegister().Succeeded);
            throwProvider = true;

            Assert.False(binding.TryApplyLatest(11));
            Assert.False(binding.TryGetSnapshot(11, out _));
        }

        [Fact]
        public void StaleGenerationCannotRegisterANewEndpoint()
        {
            var backend = new FakeBackend();
            var binding = CreateBinding(backend, 4, () => 5, _ => { }, _ => false);

            var result = binding.TryRegister();

            Assert.False(result.Succeeded);
            Assert.Equal(FoxRunRos2RegistrationError.StaleGeneration, result.Error);
            Assert.Equal(FoxRunRos2SubscriptionBindingState.Failed, binding.State);
            Assert.Equal(0, backend.RegisterCount);
        }

        [Fact]
        public void CallbackCopyFailureAndLateCallbackNeverEscapeExecutor()
        {
            var backend = new FakeBackend();
            var binding = CreateBinding(
                backend,
                3,
                () => 3,
                _ => { },
                _ => false,
                _ => throw new InvalidOperationException("copy failed"));
            Assert.True(binding.TryRegister().Succeeded);
            using var borrowed = Message("boom");

            var copyException = Record.Exception(() => backend.Invoke(borrowed));
            Assert.Null(copyException);
            Assert.Equal(1, binding.CopyFailedCount);

            binding.Stop();
            var lateException = Record.Exception(() => backend.InvokeLate(borrowed));
            Assert.Null(lateException);
            Assert.Equal(1, binding.RejectedAfterStopCount);
        }

        [Fact]
        public void BorrowedCallbackReferenceCanNeverBecomeFrameworkOwned()
        {
            var backend = new FakeBackend();
            var binding = CreateBinding(
                backend,
                3,
                () => 3,
                _ => { },
                _ => false,
                source => source);
            Assert.True(binding.TryRegister().Succeeded);
            using var borrowed = Message("borrowed");

            var exception = Record.Exception(() => backend.Invoke(borrowed));

            Assert.Null(exception);
            Assert.Equal(1, binding.CopyFailedCount);
            Assert.Equal(0, borrowed.DisposeCount);
            Assert.False(binding.TryApplyLatest(3));
            binding.Stop();
            Assert.Equal(0, borrowed.DisposeCount);
        }

        [Fact]
        public void StopIsIdempotentAndHonorsTransportOwnershipOrder()
        {
            var events = new List<string>();
            var backend = new FakeBackend(events);
            FakeMessage applied = null;
            var binding = CreateBinding(
                backend,
                5,
                () => 5,
                value => applied = value,
                value =>
                {
                    events.Add("clear-applied");
                    if (!ReferenceEquals(applied, value))
                        return false;
                    applied = null;
                    return true;
                },
                source => Message(source.Data),
                value =>
                {
                    events.Add("dispose-" + value.Data);
                    value.Dispose();
                });
            Assert.True(binding.TryRegister().Succeeded);
            using var first = Message("applied");
            using var second = Message("pending");
            backend.Invoke(first);
            Assert.True(binding.TryApplyLatest(5));
            backend.Invoke(second);

            binding.Stop();
            binding.Stop();

            Assert.Equal(
                new[]
                {
                    "remove-subscription",
                    "clear-applied",
                    "dispose-pending",
                    "dispose-applied",
                    "release-node"
                },
                events);
            Assert.Null(applied);
            Assert.Equal(1, backend.RemoveCount);
            Assert.Equal(1, backend.ReleaseCount);
        }

        [Fact]
        public void RegistrationExceptionBecomesBoundedStableFailure()
        {
            var backend = new FakeBackend { RegistrationException = new InvalidOperationException(new string('z', 2048)) };
            var binding = CreateBinding(backend, 1, () => 1, _ => { }, _ => false);

            var result = binding.TryRegister();

            Assert.False(result.Succeeded);
            Assert.Equal(FoxRunRos2RegistrationError.BackendFailure, result.Error);
            Assert.Equal(FoxRunRos2SubscriptionBindingState.Failed, binding.State);
            Assert.True(result.Diagnostic.Length <= FoxRunRos2RegistrationResult.MaximumDiagnosticLength);
        }

        [Fact]
        public void TeardownFailureDoesNotSkipOwnedCleanupOrNodeRelease()
        {
            var events = new List<string>();
            var backend = new FakeBackend(events) { RemoveException = new InvalidOperationException("remove") };
            FakeMessage applied = null;
            var binding = CreateBinding(
                backend,
                6,
                () => 6,
                value => applied = value,
                value =>
                {
                    events.Add("clear-applied");
                    applied = null;
                    return true;
                },
                dispose: value =>
                {
                    events.Add("dispose-owned");
                    value.Dispose();
                });
            Assert.True(binding.TryRegister().Succeeded);
            using var borrowed = Message("owned");
            backend.Invoke(borrowed);
            Assert.True(binding.TryApplyLatest(6));

            var exception = Record.Exception(binding.Stop);

            Assert.Null(exception);
            Assert.Equal(
                new[] { "remove-subscription", "clear-applied", "dispose-owned", "release-node" },
                events);
            Assert.Equal(FoxRunRos2SubscriptionBindingState.Stopped, binding.State);
        }

        [Fact]
        public void StopClosesCallbackAdmissionBeforeBlockingTransportRemoval()
        {
            using var removeEntered = new ManualResetEventSlim();
            using var releaseRemove = new ManualResetEventSlim();
            var copied = 0;
            var backend = new FakeBackend
            {
                RemoveEntered = removeEntered,
                ReleaseRemove = releaseRemove
            };
            var binding = CreateBinding(
                backend,
                12,
                () => 12,
                _ => { },
                _ => false,
                source =>
                {
                    Interlocked.Increment(ref copied);
                    return Message(source.Data);
                });
            Assert.True(binding.TryRegister().Succeeded);
            Exception stopFailure = null;
            var stopThread = new Thread(() =>
            {
                try { binding.Stop(); }
                catch (Exception exception) { stopFailure = exception; }
            });
            stopThread.Start();
            Assert.True(removeEntered.Wait(TimeSpan.FromSeconds(10)));
            using var late = Message("late");

            var callbackFailure = Record.Exception(() => backend.InvokeLate(late));

            Assert.Null(callbackFailure);
            Assert.Equal(0, Volatile.Read(ref copied));
            Assert.Equal(1, binding.RejectedAfterStopCount);
            Assert.False(binding.TryApplyLatest(12));
            releaseRemove.Set();
            Assert.True(stopThread.Join(TimeSpan.FromSeconds(10)));
            Assert.Null(stopFailure);
            Assert.Equal(FoxRunRos2SubscriptionBindingState.Stopped, binding.State);
        }

        [Fact]
        public void StopWaitsForInFlightCopyAndDisposesItWithoutApplying()
        {
            using var copyEntered = new ManualResetEventSlim();
            using var releaseCopy = new ManualResetEventSlim();
            using var removeEntered = new ManualResetEventSlim();
            var disposed = 0;
            var applied = 0;
            var backend = new FakeBackend { RemoveEntered = removeEntered };
            var binding = CreateBinding(
                backend,
                13,
                () => 13,
                _ => Interlocked.Increment(ref applied),
                _ => false,
                source =>
                {
                    copyEntered.Set();
                    Assert.True(releaseCopy.Wait(TimeSpan.FromSeconds(10)));
                    return Message(source.Data);
                },
                value =>
                {
                    Interlocked.Increment(ref disposed);
                    value.Dispose();
                });
            Assert.True(binding.TryRegister().Succeeded);
            using var borrowed = Message("in-flight");
            Exception callbackFailure = null;
            var callbackThread = new Thread(() =>
            {
                try { backend.Invoke(borrowed); }
                catch (Exception exception) { callbackFailure = exception; }
            });
            callbackThread.Start();
            Assert.True(copyEntered.Wait(TimeSpan.FromSeconds(10)));
            Exception stopFailure = null;
            var stopThread = new Thread(() =>
            {
                try { binding.Stop(); }
                catch (Exception exception) { stopFailure = exception; }
            });
            stopThread.Start();
            Assert.True(removeEntered.Wait(TimeSpan.FromSeconds(10)));
            releaseCopy.Set();
            Assert.True(callbackThread.Join(TimeSpan.FromSeconds(10)));
            Assert.True(stopThread.Join(TimeSpan.FromSeconds(10)));

            Assert.Null(callbackFailure);
            Assert.Null(stopFailure);
            Assert.Equal(0, Volatile.Read(ref applied));
            Assert.Equal(1, Volatile.Read(ref disposed));
            Assert.Equal(1, binding.RejectedAfterStopCount);
            Assert.Equal(FoxRunRos2SubscriptionBindingState.Stopped, binding.State);
        }

        [Fact]
        public void ApplyReentrantStopReleasesLeaseAfterSlotCompletesItsDeferredDrain()
        {
            var backend = new FakeBackend();
            FakeMessage applied = null;
            FoxRunRos2SubscriptionBinding<FakeMessage> binding = null;
            binding = CreateBinding(
                backend,
                129,
                () => 129,
                value =>
                {
                    applied = value;
                    binding.Stop();
                },
                value =>
                {
                    if (!ReferenceEquals(applied, value))
                        return false;
                    applied = null;
                    return true;
                });
            Assert.True(binding.TryRegister().Succeeded);
            using var borrowed = Message("first");
            backend.Invoke(borrowed);

            Assert.True(binding.TryApplyLatest(129));

            Assert.Null(applied);
            Assert.Equal(1, backend.RemoveCount);
            Assert.Equal(1, backend.ReleaseCount);
            binding.Stop();
            Assert.Equal(1, backend.RemoveCount);
            Assert.Equal(1, backend.ReleaseCount);
        }

        [Fact]
        public void ApplyReentrantStopFinalizesInFlightCallbackOnMainThreadAndReleasesLease()
        {
            using var copyEntered = new ManualResetEventSlim();
            using var releaseCopy = new ManualResetEventSlim();
            using var stopRequested = new ManualResetEventSlim();
            var mainThread = Environment.CurrentManagedThreadId;
            var disposeThreads = new ConcurrentDictionary<string, int>();
            var backend = new FakeBackend();
            FakeMessage applied = null;
            FakeMessage firstOwned = null;
            FakeMessage lateOwned = null;
            FoxRunRos2SubscriptionBinding<FakeMessage> binding = null;
            binding = CreateBinding(
                backend,
                130,
                () => 130,
                value =>
                {
                    applied = value;
                    stopRequested.Set();
                    binding.Stop();
                },
                value =>
                {
                    if (!ReferenceEquals(applied, value))
                        return false;
                    applied = null;
                    return true;
                },
                source =>
                {
                    var owned = Message(source.Data);
                    if (source.Data == "first")
                    {
                        firstOwned = owned;
                    }
                    else
                    {
                        lateOwned = owned;
                        copyEntered.Set();
                        Assert.True(releaseCopy.Wait(TimeSpan.FromSeconds(10)));
                    }
                    return owned;
                },
                value =>
                {
                    disposeThreads[value.Data] = Environment.CurrentManagedThreadId;
                    value.Dispose();
                });
            Assert.True(binding.TryRegister().Succeeded);
            using var first = Message("first");
            using var late = Message("late");
            backend.Invoke(first);

            Exception callbackFailure = null;
            var callback = new Thread(() =>
            {
                try { backend.Invoke(late); }
                catch (Exception exception) { callbackFailure = exception; }
            }) { IsBackground = true };
            callback.Start();
            Assert.True(copyEntered.Wait(TimeSpan.FromSeconds(10)));

            var slot = typeof(FoxRunRos2SubscriptionBinding<FakeMessage>)
                .GetField("_slot", System.Reflection.BindingFlags.Instance |
                                   System.Reflection.BindingFlags.NonPublic)
                ?.GetValue(binding);
            Assert.NotNull(slot);
            var stopState = slot.GetType().GetField(
                "_stopState",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var activeAppliers = slot.GetType().GetField(
                "_activeAppliers",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(stopState);
            Assert.NotNull(activeAppliers);
            Exception releaseFailure = null;
            var release = new Thread(() =>
            {
                try
                {
                    Assert.True(stopRequested.Wait(TimeSpan.FromSeconds(10)));
                    var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
                    while (DateTime.UtcNow < deadline)
                    {
                        if ((int)activeAppliers.GetValue(slot) == 0
                            && (int)stopState.GetValue(slot) != 0)
                        {
                            releaseCopy.Set();
                            return;
                        }
                        Thread.Yield();
                    }
                    throw new TimeoutException("The apply operation did not request deferred stop completion.");
                }
                catch (Exception exception)
                {
                    releaseFailure = exception;
                }
            }) { IsBackground = true };
            release.Start();

            try
            {
                Assert.True(binding.TryApplyLatest(130));
                Assert.True(callback.Join(TimeSpan.FromSeconds(10)));
                Assert.True(release.Join(TimeSpan.FromSeconds(10)));

                Assert.Null(callbackFailure);
                Assert.Null(releaseFailure);
                Assert.Null(applied);
                Assert.Equal(1, firstOwned.DisposeCount);
                Assert.Equal(1, lateOwned.DisposeCount);
                Assert.Equal(mainThread, disposeThreads["first"]);
                Assert.NotEqual(mainThread, disposeThreads["late"]);
                Assert.Equal(1, backend.RemoveCount);
                Assert.Equal(1, backend.ReleaseCount);

                binding.Stop();
                Assert.Equal(1, backend.RemoveCount);
                Assert.Equal(1, backend.ReleaseCount);
            }
            finally
            {
                releaseCopy.Set();
                callback.Join(TimeSpan.FromSeconds(10));
                release.Join(TimeSpan.FromSeconds(10));
                binding.Stop();
            }
        }

        [Fact]
        public void ConcurrentRegisterStopAndSnapshotNeverExposeTornOutcome()
        {
            using var removeEntered = new ManualResetEventSlim();
            using var releaseRemove = new ManualResetEventSlim();
            var backend = new FakeBackend
            {
                RemoveEntered = removeEntered,
                ReleaseRemove = releaseRemove
            };
            var binding = CreateBinding(backend, 14, () => 14, _ => { }, _ => false);
            Assert.True(binding.TryRegister().Succeeded);
            var snapshots = new ConcurrentQueue<FoxRunRos2SubscriptionBindingSnapshot>();
            var stopThread = new Thread(binding.Stop);
            stopThread.Start();
            Assert.True(removeEntered.Wait(TimeSpan.FromSeconds(10)));
            var registerResult = default(FoxRunRos2RegistrationResult);
            var registerThread = new Thread(() => registerResult = binding.TryRegister());
            registerThread.Start();
            Exception snapshotFailure = null;
            var snapshotThread = new Thread(() =>
            {
                try
                {
                    for (var i = 0; i < 100; i++)
                    {
                        if (binding.TryGetSnapshot(14, out var snapshot))
                            snapshots.Enqueue(snapshot);
                    }
                }
                catch (Exception exception)
                {
                    snapshotFailure = exception;
                }
            });
            snapshotThread.Start();
            releaseRemove.Set();
            Assert.True(stopThread.Join(TimeSpan.FromSeconds(10)));
            Assert.True(registerThread.Join(TimeSpan.FromSeconds(10)));
            Assert.True(snapshotThread.Join(TimeSpan.FromSeconds(10)));
            Assert.Null(snapshotFailure);
            Assert.False(registerResult.Succeeded);
            Assert.Equal(FoxRunRos2RegistrationError.Stopped, registerResult.Error);
            Assert.All(snapshots, snapshot =>
            {
                var coherentReady = (snapshot.State == FoxRunRos2SubscriptionBindingState.Ready
                                     || snapshot.State == FoxRunRos2SubscriptionBindingState.Receiving)
                                    && snapshot.Error == FoxRunRos2RegistrationError.None;
                var coherentStopped = snapshot.State == FoxRunRos2SubscriptionBindingState.Stopped
                                      && snapshot.Error == FoxRunRos2RegistrationError.Stopped;
                Assert.True(coherentReady || coherentStopped);
            });
        }

        [Fact]
        public void ApplyFailureBecomesTerminalAndPreservesPrimaryDiagnosticThroughTeardown()
        {
            FakeMessage owned = null;
            var copies = 0;
            var backend = new FakeBackend
            {
                RemoveException = new InvalidOperationException("secondary remove failure")
            };
            var binding = CreateBinding(
                backend,
                15,
                () => 15,
                _ => throw new InvalidOperationException("setter exploded"),
                _ => false,
                source =>
                {
                    copies++;
                    owned = Message(source.Data);
                    return owned;
                });
            Assert.True(binding.TryRegister().Succeeded);
            using var borrowed = Message("first");
            backend.Invoke(borrowed);

            var failure = Assert.Throws<InvalidOperationException>(() => binding.TryApplyLatest(15));
            binding.RecordApplyFailure(failure);

            Assert.Equal(FoxRunRos2SubscriptionBindingState.Failed, binding.State);
            Assert.True(binding.TryGetSnapshot(15, out var snapshot));
            Assert.Equal(FoxRunRos2RegistrationError.ApplyFailure, snapshot.Error);
            Assert.Equal("The native ROS2 subscription could not apply the copied message.", snapshot.Diagnostic);
            Assert.Equal(1, backend.RemoveCount);
            Assert.Equal(1, backend.ReleaseCount);
            Assert.Equal(1, owned.DisposeCount);

            using var late = Message("late");
            backend.InvokeLate(late);
            Assert.Equal(1, copies);
            Assert.Equal(1, binding.RejectedAfterStopCount);
            Assert.False(binding.TryApplyLatest(15));
            binding.Stop();
            Assert.Equal(1, backend.RemoveCount);
            Assert.Equal(1, backend.ReleaseCount);
            Assert.Equal(FoxRunRos2SubscriptionBindingState.Failed, binding.State);
        }

        [Fact]
        public void NativeTransportAdmissionPreservesTheNewestEligibleSampleBeforeDeepCopy()
        {
            var backend = new FakeBackend();
            FakeMessage applied = null;
            var copies = 0;
            var interval = (Stopwatch.Frequency + 1L) / 2L;
            var timestamps = new Queue<long>(new[]
            {
                100L,
                100L + Math.Max(1L, interval / 2L),
                100L + interval,
            });
            var binding = CreateBinding(
                backend,
                182,
                () => 182,
                value => applied = value,
                value => ReferenceEquals(applied, value),
                copy: value =>
                {
                    copies++;
                    return Message(value.Data);
                },
                transportAdmissionRateLimitHz: 2,
                admissionTimestamp: () => timestamps.Dequeue());
            binding.WaitForRuntime();
            Assert.True(binding.TryRegister().Succeeded);

            using var first = Message("first");
            using var rejected = Message("rejected-before-copy");
            using var newest = Message("newest");
            backend.Invoke(first);
            backend.Invoke(rejected);
            backend.Invoke(newest);

            Assert.Equal(2, copies);
            Assert.Equal(1, binding.TransportAdmissionDropCount);
            Assert.True(binding.TryApplyLatest(182, 0d));
            Assert.Equal("newest", applied.Data);
            binding.Stop();
        }

        [Fact]
        public void ChangePolicyDropsEqualNativeValuesAndAppliesChanges()
        {
            var backend = new FakeBackend();
            FakeMessage applied = null;
            var binding = CreateBinding(
                backend,
                183,
                () => 183,
                value => applied = value,
                value => ReferenceEquals(applied, value),
                contract: PolicyContract(Unity.FoxgloveSDK.Components.FoxRunPolicy.Change),
                valuesEqual: (left, right) => left.Data == right.Data);
            binding.WaitForRuntime();
            Assert.True(binding.TryRegister().Succeeded);

            backend.Invoke(Message("first"));
            Assert.True(binding.TryApplyLatest(183, 0d));
            backend.Invoke(Message("first"));
            Assert.False(binding.TryApplyLatest(183, 1d));
            backend.Invoke(Message("changed"));
            Assert.True(binding.TryApplyLatest(183, 2d));
            Assert.Equal("changed", applied.Data);
            binding.Stop();
        }

        [Fact]
        public void NativeOnlyIfDropsPendingAndInvalidatesSemanticHistoryUntilRecovery()
        {
            var backend = new FakeBackend();
            FakeMessage applied = null;
            var condition = true;
            var binding = CreateBinding(
                backend,
                186,
                () => 186,
                value => applied = value,
                value =>
                {
                    if (!ReferenceEquals(applied, value))
                        return false;
                    applied = null;
                    return true;
                },
                contract: PolicyContract(Unity.FoxgloveSDK.Components.FoxRunPolicy.Change),
                valuesEqual: (left, right) => left.Data == right.Data,
                canApply: () => condition);
            binding.WaitForRuntime();
            Assert.True(binding.TryRegister().Succeeded);

            backend.Invoke(Message("same"));
            Assert.True(binding.TryApplyLatest(186, 0d));
            Assert.Equal(1, binding.AppliedCount);
            Assert.Equal("same", applied.Data);

            condition = false;
            backend.Invoke(Message("same"));
            Assert.False(binding.TryApplyLatest(186, 1d));
            Assert.Equal(1, binding.AppliedCount);
            Assert.Equal("same", applied.Data);

            condition = true;
            backend.Invoke(Message("same"));
            Assert.True(binding.TryApplyLatest(186, 2d));
            Assert.Equal(2, binding.AppliedCount);
            Assert.Equal("same", applied.Data);
            binding.Stop();
        }

        [Fact]
        public void ChangeWithHeartbeatDefersFreshDuplicateUntilItsInterval()
        {
            var backend = new FakeBackend();
            FakeMessage applied = null;
            var binding = CreateBinding(
                backend,
                184,
                () => 184,
                value => applied = value,
                value => ReferenceEquals(applied, value),
                contract: PolicyContract(
                    Unity.FoxgloveSDK.Components.FoxRunPolicy.Change,
                    heartbeatIntervalSeconds: 2f),
                valuesEqual: (left, right) => left.Data == right.Data);
            binding.WaitForRuntime();
            Assert.True(binding.TryRegister().Succeeded);

            backend.Invoke(Message("same"));
            Assert.True(binding.TryApplyLatest(184, 0d));
            backend.Invoke(Message("same"));
            Assert.False(binding.TryApplyLatest(184, 1d));
            Assert.True(binding.TryApplyLatest(184, 2d));
            binding.Stop();
        }

        [Fact]
        public void TriggerPolicyKeepsOnlyNewestNativeValueUntilExplicitApply()
        {
            var backend = new FakeBackend();
            FakeMessage applied = null;
            var trigger = false;
            var binding = CreateBinding(
                backend,
                185,
                () => 185,
                value => applied = value,
                value => ReferenceEquals(applied, value),
                contract: PolicyContract(Unity.FoxgloveSDK.Components.FoxRunPolicy.Trigger),
                valuesEqual: (left, right) => left.Data == right.Data,
                consumeTrigger: () =>
                {
                    var requested = trigger;
                    trigger = false;
                    return requested;
                });
            binding.WaitForRuntime();
            Assert.True(binding.TryRegister().Succeeded);

            backend.Invoke(Message("first"));
            Assert.False(binding.TryApplyLatest(185, 0d));
            backend.Invoke(Message("latest"));
            trigger = true;
            Assert.True(binding.TryApplyLatest(185, 1d));
            Assert.Equal("latest", applied.Data);
            binding.Stop();
        }

        private static FoxRunRos2SubscriptionBinding<FakeMessage> CreateBinding(
            FakeBackend backend,
            long generation,
            Func<long> activeGeneration,
            Action<FakeMessage> apply,
            Func<FakeMessage, bool> clearIfOwned,
            Func<FakeMessage, FakeMessage> copy = null,
            Action<FakeMessage> dispose = null,
            FoxRunRos2GeneratedContract contract = null,
            Func<FakeMessage, FakeMessage, bool> valuesEqual = null,
            Func<bool> consumeTrigger = null,
            Func<bool> canApply = null,
            int transportAdmissionRateLimitHz = int.MaxValue,
            Func<long> admissionTimestamp = null)
        {
            return new FoxRunRos2SubscriptionBinding<FakeMessage>(
                contract ?? Contract(),
                generation,
                activeGeneration,
                4L * 1024L * 1024L,
                (source, _) => (copy ?? (value => Message(value.Data)))(source),
                dispose ?? (value => value.Dispose()),
                apply,
                clearIfOwned,
                backend,
                Unity.FoxgloveSDK.Components.FoxRunResolvedQos.Default,
                new ManagedQosFactory(),
                valuesEqual: valuesEqual,
                consumeTrigger: consumeTrigger,
                canApply: canApply,
                transportAdmissionRateLimitHz: transportAdmissionRateLimitHz,
                admissionTimestamp: admissionTimestamp);
        }

        private static FoxRunRos2GeneratedContract PolicyContract(
            Unity.FoxgloveSDK.Components.FoxRunPolicy policy,
            float heartbeatIntervalSeconds = 0f)
            => new FoxRunRos2GeneratedContract(
                "policy-contract-" + policy,
                "/native/policy",
                "Demo.Receiver",
                "_incoming",
                "std_msgs/msg/String",
                Unity.FoxgloveSDK.Components.FoxRunFlow.Subscribe,
                Unity.FoxgloveSDK.Components.FoxRunEndpoint.Ros2Native,
                Unity.FoxgloveSDK.Components.FoxRunQosProfile.Default,
                hasExplicitQosProfile: true,
                qosReliability: default,
                hasExplicitQosReliability: false,
                qosDurability: default,
                hasExplicitQosDurability: false,
                qosHistory: default,
                hasExplicitQosHistory: false,
                qosDepth: 0,
                hasExplicitQosDepth: false,
                supportsRos2Native: true,
                policy: policy,
                hz: 0f,
                hasExplicitHz: false,
                heartbeatIntervalSeconds: heartbeatIntervalSeconds);

        private static FoxRunRos2GeneratedContract Contract()
            => new FoxRunRos2GeneratedContract(
                "contract-1",
                "/native/string",
                "Demo.Receiver",
                "_incoming",
                "std_msgs/msg/String",
                Unity.FoxgloveSDK.Components.FoxRunFlow.Subscribe,
                Unity.FoxgloveSDK.Components.FoxRunEndpoint.Ros2Native,
                Unity.FoxgloveSDK.Components.FoxRunQosProfile.Default,
                hasExplicitQosProfile: true,
                qosReliability: default,
                hasExplicitQosReliability: false,
                qosDurability: default,
                hasExplicitQosDurability: false,
                qosHistory: default,
                hasExplicitQosHistory: false,
                qosDepth: 0,
                hasExplicitQosDepth: false,
                supportsRos2Native: true);

        private static FakeMessage Message(string value)
            => new FakeMessage { Data = value };

        private sealed class FakeBackend : IFoxRunRos2NativeBackend
        {
            private readonly List<string> _events;
            private Action<FakeMessage> _callback;
            private Action<FakeMessage> _lateCallback;
            private readonly List<Action<FakeMessage>> _callbacks = new List<Action<FakeMessage>>();
            private readonly Queue<FoxRunRos2NativeBackendRegistration> _registrations =
                new Queue<FoxRunRos2NativeBackendRegistration>();

            public FakeBackend(List<string> events = null)
            {
                _events = events;
                Next = FoxRunRos2NativeBackendRegistration.Success(new FakeToken());
            }

            public FoxRunRos2NativeBackendRegistration Next { get; set; }
            public Exception RegistrationException { get; set; }
            public FakeMessage SynchronousMessage { get; set; }
            public Action DuringRegister { get; set; }
            public Action AfterRegister { get; set; }
            public ManualResetEventSlim RegisterEntered { get; set; }
            public ManualResetEventSlim ReleaseRegister { get; set; }
            public Exception RemoveException { get; set; }
            public Exception ReleaseException { get; set; }
            public ManualResetEventSlim RemoveEntered { get; set; }
            public ManualResetEventSlim ReleaseRemove { get; set; }
            public int RegisterCount { get; private set; }
            public int RemoveCount { get; private set; }
            public int ReleaseCount { get; private set; }

            public FoxRunRos2NativeBackendRegistration Register<T>(
                FoxRunRos2GeneratedContract contract,
                IFoxRunRos2NativeQosProfile qosProfile,
                Action<T> callback)
                where T : ROS2.Message, new()
            {
                if (RegistrationException != null)
                    throw RegistrationException;
                RegisterCount++;
                RegisterEntered?.Set();
                if (ReleaseRegister != null)
                    Assert.True(ReleaseRegister.Wait(TimeSpan.FromSeconds(10)));
                _callback = message => callback((T)(ROS2.Message)message);
                _lateCallback = _callback;
                _callbacks.Add(_callback);
                if (SynchronousMessage != null)
                    _callback(SynchronousMessage);
                DuringRegister?.Invoke();
                var result = _registrations.Count == 0 ? Next : _registrations.Dequeue();
                AfterRegister?.Invoke();
                return result;
            }

            public void RemoveSubscription(IFoxRunRos2NativeSubscriptionToken token)
            {
                RemoveCount++;
                _events?.Add("remove-subscription");
                RemoveEntered?.Set();
                if (ReleaseRemove != null)
                    Assert.True(ReleaseRemove.Wait(TimeSpan.FromSeconds(10)));
                _callback = null;
                if (RemoveException != null)
                    throw RemoveException;
            }

            public void ReleaseNodeOwnership()
            {
                ReleaseCount++;
                _events?.Add("release-node");
                if (ReleaseException != null)
                    throw ReleaseException;
            }

            public void Invoke(FakeMessage value) => _callback(value);
            public void InvokeLate(FakeMessage value) => _lateCallback(value);
            public void InvokeAttempt(int attemptIndex, FakeMessage value) => _callbacks[attemptIndex](value);
            public void EnqueueRegistration(FoxRunRos2NativeBackendRegistration registration)
                => _registrations.Enqueue(registration);
        }

        private sealed class FakeToken : IFoxRunRos2NativeSubscriptionToken
        {
            public FakeToken(bool isUsable = true)
            {
                IsUsable = isUsable;
            }

            public bool IsUsable { get; }
        }

        private sealed class ManagedQosFactory : IFoxRunRos2NativeQosProfileFactory
        {
            public IFoxRunRos2NativeQosProfile Create(ROS2.QosPresetProfile preset)
                => new ManagedQosProfile();
        }

        private sealed class ManagedQosProfile : IFoxRunRos2NativeQosProfile
        {
            public ROS2.QualityOfServiceProfile NativeProfile => null;

            public void SetHistory(ROS2.HistoryPolicy history, int depth) { }

            public void SetPolicies(
                ROS2.HistoryPolicy history,
                int depth,
                ROS2.ReliabilityPolicy reliability,
                ROS2.DurabilityPolicy durability) { }

            public void Dispose() { }
        }

        private sealed class FakeMessage : ROS2.Message, IDisposable
        {
            public string Data { get; set; }
            public int DisposeCount { get; private set; }
            public bool IsDisposed { get; private set; }

            public void Dispose()
            {
                DisposeCount++;
                IsDisposed = true;
            }
        }
    }

    [Trait("Phase", "179-C")]
    [Trait("Domain", "Ros2NativeSubscriptionHost")]
    public sealed class FoxRunRos2SubscriptionHostTests
    {
        [Fact]
        public void ActiveSessionStateFailClosesBeforeAndAfterCapturedGeneration()
        {
            var state = new FoxRunRos2ActiveSessionState();

            Assert.Equal(-1, state.ReadGeneration());
            state.Activate(12);
            Assert.Equal(12, state.ReadGeneration());

            state.Deactivate();
            Assert.Equal(-1, state.ReadGeneration());
            state.Activate(13);
            Assert.Equal(13, state.ReadGeneration());
        }

        [Fact]
        public void NativeRuntimeAdmissionDoesNotRetainTheUnityHost()
        {
            var fields = typeof(FoxRunRos2NativeRuntimeAdmission).GetFields(
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Public);

            Assert.DoesNotContain(
                fields,
                field => typeof(UnityEngine.MonoBehaviour).IsAssignableFrom(field.FieldType));
            Assert.DoesNotContain(
                fields,
                field => field.FieldType == typeof(FoxRunRos2SubscriptionHub));
        }

        [Fact]
        public void DeniedBootstrapRetriesWithoutCreatingNativeStateAndShutdownStaysInert()
        {
            var retry = new FoxRunRos2BootstrapRetryState();

            Assert.False(retry.ShouldCreateHost(canBootstrap: false, shuttingDown: false));
            Assert.False(retry.HasCreatedHost);
            Assert.True(retry.ShouldCreateHost(canBootstrap: true, shuttingDown: false));
            Assert.True(retry.HasCreatedHost);
            Assert.False(retry.ShouldCreateHost(canBootstrap: true, shuttingDown: false));
            retry.RecordCreateFailed();
            Assert.False(retry.HasCreatedHost);
            Assert.True(retry.ShouldCreateHost(canBootstrap: true, shuttingDown: false));

            var shutdownRetry = new FoxRunRos2BootstrapRetryState();
            Assert.False(shutdownRetry.ShouldCreateHost(canBootstrap: true, shuttingDown: true));
            Assert.False(shutdownRetry.ShouldCreateHost(canBootstrap: false, shuttingDown: true));
            Assert.False(shutdownRetry.HasCreatedHost);
        }

        [Fact]
        public void NativeNodeRetriesAreBurstBoundedButEventuallyResume()
        {
            var retry = new FoxRunRos2BoundedRetryGate(4, 5.0);
            for (var i = 0; i < 4; i++)
            {
                Assert.True(retry.TryBegin(10.0));
                retry.RecordFailure(10.0);
            }
            Assert.False(retry.TryBegin(14.999));
            Assert.True(retry.TryBegin(15.0));
            retry.RecordSuccess();
            Assert.True(retry.TryBegin(15.0));
        }

        [Fact]
        public void ContractActivationRequiresCapturedNativeSubscribeCapability()
        {
            var nativePolicy = new Unity.FoxgloveSDK.Components.FoxRunSubscriptionSessionPolicy(
                12,
                true,
                Unity.FoxgloveSDK.Components.FoxRunEndpoint.Ros2Native,
                Unity.FoxgloveSDK.Components.FoxRunEncoding.Protobuf,
                Unity.FoxgloveSDK.Components.FoxRunResolvedQos.SensorData,
                4096,
                120,
                20);
            var inherited = Contract("inherit", "inherit");
            Assert.True(FoxRunRos2ContractActivation.TryResolve(
                inherited,
                nativePolicy,
                out var qos,
                out var diagnostic));
            AssertResolvedQos(
                Unity.FoxgloveSDK.Components.FoxRunResolvedQos.SensorData,
                qos);
            Assert.Equal(string.Empty, diagnostic);

            Assert.True(FoxRunRos2ContractActivation.TryResolve(
                Contract(
                    "inherit",
                    "inherit",
                    Unity.FoxgloveSDK.Components.FoxRunFlow.PublishAndSubscribe),
                nativePolicy,
                out _,
                out diagnostic));
            Assert.Equal(string.Empty, diagnostic);

            Assert.False(FoxRunRos2ContractActivation.TryResolve(
                Contract(
                    "inherit",
                    "inherit",
                    Unity.FoxgloveSDK.Components.FoxRunFlow.Publish),
                nativePolicy,
                out _,
                out diagnostic));
            Assert.Contains("Subscribe", diagnostic, StringComparison.Ordinal);

            Assert.False(FoxRunRos2ContractActivation.TryResolve(
                Contract("inherit", "inherit", supportsNative: false),
                nativePolicy,
                out _,
                out diagnostic));
            Assert.Contains("capability", diagnostic, StringComparison.OrdinalIgnoreCase);

            var disabled = new Unity.FoxgloveSDK.Components.FoxRunSubscriptionSessionPolicy(
                12,
                false,
                Unity.FoxgloveSDK.Components.FoxRunEndpoint.Ros2Native,
                Unity.FoxgloveSDK.Components.FoxRunEncoding.Protobuf,
                Unity.FoxgloveSDK.Components.FoxRunResolvedQos.Default,
                4096,
                120,
                20);
            Assert.False(FoxRunRos2ContractActivation.TryResolve(
                inherited,
                disabled,
                out _,
                out diagnostic));
            Assert.Contains("disabled", diagnostic, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void LegacyStringContractSurfaceIsAbsent()
        {
            var contractType = typeof(FoxRunRos2GeneratedContract);

            Assert.Null(contractType.GetProperty("DeclaredSource"));
            Assert.Null(contractType.GetProperty("Ros2Qos"));
            foreach (var constructor in contractType.GetConstructors())
            {
                Assert.DoesNotContain(
                    constructor.GetParameters(),
                    parameter => string.Equals(
                        parameter.Name,
                        "ros2Qos",
                        StringComparison.Ordinal));
            }
        }

        [Fact]
        public void ContractActivationPermitsCompleteCustomNativePublishAndSubscribe()
        {
            var nativePolicy = new Unity.FoxgloveSDK.Components.FoxRunSubscriptionSessionPolicy(
                13,
                true,
                Unity.FoxgloveSDK.Components.FoxRunEndpoint.Ros2Native,
                Unity.FoxgloveSDK.Components.FoxRunEncoding.Protobuf,
                Unity.FoxgloveSDK.Components.FoxRunResolvedQos.Default,
                4096,
                120,
                20);

            var custom = CustomContract(
                Unity.FoxgloveSDK.Components.FoxRunEndpoint.Ros2Native,
                Unity.FoxgloveSDK.Components.FoxRunEncoding.JSON);
            Assert.True(custom.HasCompleteCustomMetadata);
            Assert.True(FoxRunRos2ContractActivation.TryResolve(
                custom,
                nativePolicy,
                out var qos,
                out var error,
                out var diagnostic));
            AssertResolvedQos(
                Unity.FoxgloveSDK.Components.FoxRunResolvedQos.Default,
                qos);
            Assert.Equal(FoxRunRos2RegistrationError.None, error);
            Assert.Equal(string.Empty, diagnostic);

            var withoutNativeProvider = CustomContract(
                Unity.FoxgloveSDK.Components.FoxRunEndpoint.Foxglove,
                Unity.FoxgloveSDK.Components.FoxRunEncoding.JSON);
            Assert.False(FoxRunRos2ContractActivation.TryResolve(
                withoutNativeProvider,
                nativePolicy,
                out _,
                out _,
                out diagnostic));
            Assert.False(string.IsNullOrWhiteSpace(diagnostic));
        }

        [Fact]
        public void ExplicitNativeContractDoesNotDependOnOutputOrManagerDefaultSource()
        {
            var policy = new Unity.FoxgloveSDK.Components.FoxRunSubscriptionSessionPolicy(
                15,
                true,
                Unity.FoxgloveSDK.Components.FoxRunEndpoint.Foxglove,
                Unity.FoxgloveSDK.Components.FoxRunEncoding.JSON,
                Unity.FoxgloveSDK.Components.FoxRunResolvedQos.SensorData,
                8192,
                120,
                60);

            Assert.True(FoxRunRos2ContractActivation.TryResolve(
                Contract("ros2-native", "reliable"),
                policy,
                out var qos,
                out _));
            AssertResolvedQos(
                Unity.FoxgloveSDK.Components.FoxRunResolvedQos.Default,
                qos);
        }

        [Fact]
        public void ApplyRateGateDrainsAtMostOneValuePerCapturedPeriod()
        {
            var gate = new FoxRunRos2ApplyRateGate(10);

            Assert.True(gate.TryAcquire(100.0));
            Assert.False(gate.TryAcquire(100.01));
            Assert.False(gate.TryAcquire(100.099));
            Assert.True(gate.TryAcquire(100.1));
            Assert.False(gate.TryAcquire(100.1));

            var emptyGate = new FoxRunRos2ApplyRateGate(10);
            Assert.False(emptyGate.TryExecute(200.0, () => false));
            Assert.True(emptyGate.TryExecute(200.0, () => true));
            Assert.False(emptyGate.TryExecute(200.01, () => true));
        }

        [Fact]
        public void DiscoveryKeysSortByTypeInstanceTopicAndMember()
        {
            var keys = new List<FoxRunRos2DiscoveryKey>
            {
                new FoxRunRos2DiscoveryKey("B.Type", 1, "/a", "a"),
                new FoxRunRos2DiscoveryKey("A.Type", 2, "/a", "a"),
                new FoxRunRos2DiscoveryKey("A.Type", 1, "/b", "a"),
                new FoxRunRos2DiscoveryKey("A.Type", 1, "/a", "b"),
                new FoxRunRos2DiscoveryKey("A.Type", 1, "/a", "a")
            };

            keys.Sort();

            Assert.Equal(
                new[] { "A.Type|1|/a|a", "A.Type|1|/a|b", "A.Type|1|/b|a", "A.Type|2|/a|a", "B.Type|1|/a|a" },
                keys.ConvertAll(key => key.ToString()));
        }

        [Fact]
        public void NativeOnlyGeneratedSourceIsDiscoverableWithoutWebSocketInputInterface()
        {
            var source = new NativeOnlySource { isActiveAndEnabled = true };

            Assert.True(FoxRunRos2SourceDiscovery.TryGet(source, out var discovered));
            Assert.Same(source, discovered);
            Assert.False((object)source is Unity.FoxgloveSDK.Components.IFoxgloveInputSource);
        }

        [Fact]
        public void CustomNativeOnlyGeneratedSourceUsesTheExistingSubscriptionRegistrarDiscovery()
        {
            var source = new CustomNativeOnlySource { isActiveAndEnabled = true };

            Assert.True(FoxRunRos2SourceDiscovery.TryGetCustom(source, out var discovered));
            Assert.Same(source, discovered);
            Assert.False((object)source is IFoxRunRos2SubscriptionSource);
        }

        [Fact]
        public void ProductionBackendBorrowsOneQosAndSharedNodeReleaseIsUnique()
        {
            var driver = new FakeR2fuNodeDriver();
            var owner = new Ros2ForUnityFoxRunNodeOwner(driver);
            var first = owner.AcquireBackend();
            var second = owner.AcquireBackend();
            var qos = new HostManagedQosProfile();

            var firstResult = first.Register<FakeHostMessage>(
                Contract("ros2-native", "default"),
                qos,
                _ => { });
            var secondResult = second.Register<FakeHostMessage>(
                Contract("ros2-native", "default"),
                qos,
                _ => { });

            Assert.True(firstResult.Succeeded);
            Assert.True(secondResult.Succeeded);
            Assert.Equal(2, driver.CreateCount);
            Assert.All(driver.SeenQos, seen => Assert.Same(qos.NativeProfile, seen));
            first.RemoveSubscription(firstResult.Token);
            Assert.Equal(1, driver.RemoveCount);

            first.ReleaseNodeOwnership();
            first.ReleaseNodeOwnership();
            owner.ReleaseHostOwnership();
            Assert.Equal(0, driver.ReleaseNodeCount);
            second.ReleaseNodeOwnership();
            second.ReleaseNodeOwnership();
            Assert.Equal(1, driver.ReleaseNodeCount);
        }

        [Fact]
        public void MigratedDefaultFixtureKeepsThePortableDefaultProfile()
        {
            var contract = Contract("ros2-native", "default");

            Assert.True(contract.HasExplicitQosProfile);
            Assert.Equal(
                Unity.FoxgloveSDK.Components.FoxRunQosProfile.Default,
                contract.QosProfile);
        }

        [Fact]
        public void ProductionBackendRechecksLifecycleAdmissionImmediatelyBeforeSubscriptionCreation()
        {
            var driver = new FakeR2fuNodeDriver();
            var lifecycleReady = false;
            var owner = new Ros2ForUnityFoxRunNodeOwner(driver, () => lifecycleReady);
            var backend = owner.AcquireBackend();
            var qos = new HostManagedQosProfile();

            var denied = backend.Register<FakeHostMessage>(
                Contract("ros2-native", "default"),
                qos,
                _ => { });

            Assert.False(denied.Succeeded);
            Assert.Equal(FoxRunRos2RegistrationError.RuntimeUnavailable, denied.Error);
            Assert.Equal(0, driver.CreateCount);

            lifecycleReady = true;
            var accepted = backend.Register<FakeHostMessage>(
                Contract("ros2-native", "default"),
                qos,
                _ => { });
            Assert.True(accepted.Succeeded);
            Assert.Equal(1, driver.CreateCount);

            backend.RemoveSubscription(accepted.Token);
            backend.ReleaseNodeOwnership();
            owner.ReleaseHostOwnership();
        }

        [Fact]
        public void ProductionBackendMakesMissingSubscriptionRemovalObservable()
        {
            var driver = new FakeR2fuNodeDriver { RemoveReturns = false };
            var owner = new Ros2ForUnityFoxRunNodeOwner(driver);
            var backend = owner.AcquireBackend();
            var registration = backend.Register<FakeHostMessage>(
                Contract("ros2-native", "default"),
                new HostManagedQosProfile(),
                _ => { });
            Assert.True(registration.Succeeded);

            var failure = Assert.Throws<InvalidOperationException>(
                () => backend.RemoveSubscription(registration.Token));
            Assert.Contains("not found", failure.Message, StringComparison.OrdinalIgnoreCase);
            backend.ReleaseNodeOwnership();
            owner.ReleaseHostOwnership();
            Assert.Equal(1, driver.ReleaseNodeCount);
        }

        [Fact]
        public void DiagnosticsKeepContractsIsolatedAndDebounceIdenticalFailures()
        {
            var diagnostics = new FoxRunRos2SubscriptionDiagnostics();
            var ready = Snapshot("ready", FoxRunRos2SubscriptionBindingState.Ready, FoxRunRos2RegistrationError.None, string.Empty);
            var failed = Snapshot("failed", FoxRunRos2SubscriptionBindingState.Failed, FoxRunRos2RegistrationError.BackendFailure, "boom");

            diagnostics.Update("source:11|ready", ready);
            diagnostics.Update("source:12|failed", failed);

            Assert.Equal(2, diagnostics.Count);
            Assert.True(diagnostics.TryGet("source:11|ready", out var readyResult));
            Assert.Equal(FoxRunRos2SubscriptionBindingState.Ready, readyResult.State);
            Assert.True(diagnostics.ShouldLog("source:12|failed", failed));
            Assert.False(diagnostics.ShouldLog("source:12|failed", failed));
            Assert.False(diagnostics.ShouldLog("source:99|failed", failed));
            var sameCodeDifferentMessage = Snapshot(
                "failed",
                FoxRunRos2SubscriptionBindingState.Failed,
                FoxRunRos2RegistrationError.BackendFailure,
                "backend was retried");
            Assert.False(diagnostics.ShouldLog("source:12|failed", sameCodeDifferentMessage));

            var healthy = Snapshot(
                "failed",
                FoxRunRos2SubscriptionBindingState.Ready,
                FoxRunRos2RegistrationError.None,
                string.Empty);
            diagnostics.Update("source:12|failed", healthy);
            Assert.False(diagnostics.ShouldLog("source:12|failed", healthy));
            diagnostics.Update("source:12|failed", failed);
            Assert.True(diagnostics.ShouldLog("source:12|failed", failed));
        }

        [Fact]
        public void DiagnosticsDoNotRelogFailureWhenHealthySiblingSharesContract()
        {
            var diagnostics = new FoxRunRos2SubscriptionDiagnostics();
            var failed = Snapshot(
                "shared-contract",
                FoxRunRos2SubscriptionBindingState.Failed,
                FoxRunRos2RegistrationError.BackendFailure,
                "backend failed");
            var healthy = Snapshot(
                "shared-contract",
                FoxRunRos2SubscriptionBindingState.Ready,
                FoxRunRos2RegistrationError.None,
                string.Empty);

            diagnostics.Update("source:1|shared-contract", failed);
            diagnostics.Update("source:2|shared-contract", healthy);
            Assert.True(diagnostics.ShouldLog("source:1|shared-contract", failed));

            // A normal sibling must not clear a still-active contract/error signature.
            diagnostics.Update("source:2|shared-contract", healthy);
            Assert.False(diagnostics.ShouldLog("source:2|shared-contract", healthy));
            diagnostics.Update("source:1|shared-contract", failed);
            Assert.False(diagnostics.ShouldLog("source:1|shared-contract", failed));

            // Once the last failed sibling recovers, the next recurrence is reportable.
            diagnostics.Update("source:1|shared-contract", healthy);
            Assert.False(diagnostics.ShouldLog("source:1|shared-contract", healthy));
            diagnostics.Update("source:1|shared-contract", failed);
            Assert.True(diagnostics.ShouldLog("source:1|shared-contract", failed));
        }

        [Fact]
        public void DiagnosticsSeparateIdenticalContractsByRuntimeEndpointIdentity()
        {
            var diagnostics = new FoxRunRos2SubscriptionDiagnostics();
            var first = Snapshot("stable-contract", FoxRunRos2SubscriptionBindingState.Ready,
                FoxRunRos2RegistrationError.None, string.Empty, received: 3);
            var second = Snapshot("stable-contract", FoxRunRos2SubscriptionBindingState.Failed,
                FoxRunRos2RegistrationError.BackendFailure, "second failed", received: 9);

            diagnostics.Update("source:101|stable-contract", first);
            diagnostics.Update("source:202|stable-contract", second);

            Assert.Equal(2, diagnostics.Count);
            Assert.True(diagnostics.TryGet("source:101|stable-contract", out var firstResult));
            Assert.True(diagnostics.TryGet("source:202|stable-contract", out var secondResult));
            Assert.Equal("stable-contract", firstResult.ContractId);
            Assert.Equal(3, firstResult.Received);
            Assert.Equal(9, secondResult.Received);

            diagnostics.RemoveExcept(new HashSet<string>(StringComparer.Ordinal)
            {
                "source:202|stable-contract"
            });
            Assert.False(diagnostics.TryGet("source:101|stable-contract", out _));
            Assert.True(diagnostics.TryGet("source:202|stable-contract", out secondResult));
            Assert.Equal(9, secondResult.Received);
        }

        [Fact]
        public void RuntimeDiagnosticsExposeSortedBoundedContractAndTransportSnapshots()
        {
            var diagnostics = new FoxRunRos2SubscriptionDiagnostics();
            var zeta = new FoxRunRos2GeneratedContract(
                "zeta", "/zeta", "Demo.Zeta", "_incoming", "std_msgs/msg/String",
                Unity.FoxgloveSDK.Components.FoxRunFlow.Subscribe,
                Unity.FoxgloveSDK.Components.FoxRunEndpoint.Ros2Native,
                Unity.FoxgloveSDK.Components.FoxRunQosProfile.Default,
                hasExplicitQosProfile: true,
                qosReliability: default,
                hasExplicitQosReliability: false,
                qosDurability: default,
                hasExplicitQosDurability: false,
                qosHistory: default,
                hasExplicitQosHistory: false,
                qosDepth: 0,
                hasExplicitQosDepth: false,
                supportsRos2Native: true);
            var alpha = new FoxRunRos2GeneratedContract(
                "alpha", "/alpha", "Demo.Alpha", "_incoming", "geometry_msgs/msg/Twist",
                Unity.FoxgloveSDK.Components.FoxRunFlow.Subscribe,
                Unity.FoxgloveSDK.Components.FoxRunEndpoint.Ros2Native,
                Unity.FoxgloveSDK.Components.FoxRunQosProfile.SensorData,
                hasExplicitQosProfile: true,
                qosReliability: default,
                hasExplicitQosReliability: false,
                qosDurability: default,
                hasExplicitQosDurability: false,
                qosHistory: default,
                hasExplicitQosHistory: false,
                qosDepth: 0,
                hasExplicitQosDepth: false,
                supportsRos2Native: true);

            diagnostics.Update(
                "source:2|zeta",
                new FoxRunRos2SubscriptionBindingSnapshot(
                    zeta,
                    Unity.FoxgloveSDK.Components.FoxRunResolvedQos.Default,
                    9,
                    FoxRunRos2SubscriptionBindingState.Failed,
                    FoxRunRos2RegistrationError.BackendFailure,
                    new string('z', FoxRunRos2RegistrationResult.MaximumDiagnosticLength + 9),
                    7, 3, 2, 1, 4, 5, 6, 101, 102),
                new FoxRunRos2RuntimeDiagnosticContext("lyrical", "rmw_zenoh_cpp"));
            diagnostics.Update(
                "source:1|alpha",
                new FoxRunRos2SubscriptionBindingSnapshot(
                    alpha,
                    Unity.FoxgloveSDK.Components.FoxRunResolvedQos.SensorData,
                    8,
                    FoxRunRos2SubscriptionBindingState.Receiving,
                    FoxRunRos2RegistrationError.None,
                    string.Empty,
                    11, 1, 10, 0, 0, 0, 0, 201, 202),
                new FoxRunRos2RuntimeDiagnosticContext("jazzy", "rmw_fastrtps_cpp"));

            var snapshots = diagnostics.GetSnapshots();

            Assert.Equal(2, snapshots.Length);
            Assert.Equal("alpha", snapshots[0].ContractId);
            Assert.Equal("/alpha", snapshots[0].Topic);
            Assert.Equal("Demo.Alpha", snapshots[0].DeclaringType);
            Assert.Equal("geometry_msgs/msg/Twist", snapshots[0].CanonicalRosType);
            Assert.Equal("jazzy", snapshots[0].RosDistro);
            Assert.Equal("rmw_fastrtps_cpp", snapshots[0].RmwImplementation);
            Assert.Equal("fastdds", snapshots[0].CommunicationMode);
            Assert.Equal("ROS2 Native / FastDDS (DDS)", snapshots[0].TransportLabel);
            AssertResolvedQos(
                Unity.FoxgloveSDK.Components.FoxRunResolvedQos.SensorData,
                snapshots[0].Qos);
            Assert.Equal(201, snapshots[0].LastReceiveStopwatchTimestamp);
            Assert.Equal(202, snapshots[0].LastApplyStopwatchTimestamp);

            Assert.Equal("zeta", snapshots[1].ContractId);
            Assert.Equal("zenoh", snapshots[1].CommunicationMode);
            Assert.Equal("ROS2 Native / Zenoh", snapshots[1].TransportLabel);
            AssertResolvedQos(
                Unity.FoxgloveSDK.Components.FoxRunResolvedQos.Default,
                snapshots[1].Qos);
            Assert.Equal("BackendFailure", snapshots[1].LastErrorCode);
            Assert.Equal("The native ROS2 backend failed while operating the subscription.", snapshots[1].LastErrorMessage);
            Assert.Equal(7, snapshots[1].Received);
            Assert.Equal(3, snapshots[1].Replaced);
            Assert.Equal(2, snapshots[1].Applied);
            Assert.Equal(1, snapshots[1].Pending);
            Assert.Equal(4, snapshots[1].RejectedAfterStop);
            Assert.Equal(5, snapshots[1].CopyFailed);
            Assert.Equal(6, snapshots[1].StaleCallbacks);

            var unknownRmw = new FoxRunRos2RuntimeDiagnosticContext(
                "future", "rmw_custom_cpp");
            Assert.Equal("unknown", unknownRmw.CommunicationMode);
            Assert.Equal("ROS2 Native / rmw_custom_cpp", unknownRmw.TransportLabel);

            var getSnapshots = typeof(FoxRunRos2SubscriptionRuntimeDiagnostics).GetMethod("GetSnapshots");
            Assert.NotNull(getSnapshots);
            Assert.Equal(typeof(FoxRunRos2SubscriptionDiagnosticSnapshot[]), getSnapshots.ReturnType);
        }

        [Fact]
        public void RuntimeDiagnosticOrderingUsesEndpointAsTheFinalDeterministicTieBreaker()
        {
            var diagnostics = new FoxRunRos2SubscriptionDiagnostics();
            var snapshot = Snapshot(
                "same-contract",
                FoxRunRos2SubscriptionBindingState.Receiving,
                FoxRunRos2RegistrationError.None,
                string.Empty,
                received: 9);
            diagnostics.Update("source:9|same-contract", snapshot);
            diagnostics.Update(
                "source:1|same-contract",
                Snapshot(
                    "same-contract",
                    FoxRunRos2SubscriptionBindingState.Receiving,
                    FoxRunRos2RegistrationError.None,
                    string.Empty,
                    received: 1));

            var snapshots = diagnostics.GetSnapshots();

            Assert.Equal(2, snapshots.Length);
            Assert.Equal(1, snapshots[0].Received);
            Assert.Equal(9, snapshots[1].Received);
        }

        [Fact]
        public void OneRegistrationFailureDoesNotPreventTheNextContract()
        {
            var failures = 0;
            var laterRegistrations = 0;

            Assert.False(FoxRunRos2RegistrationIsolation.TryRun(
                () => throw new InvalidOperationException("first failed"),
                _ => failures++));
            Assert.True(FoxRunRos2RegistrationIsolation.TryRun(
                () => laterRegistrations++,
                _ => failures++));

            Assert.Equal(1, failures);
            Assert.Equal(1, laterRegistrations);
        }

        [Fact]
        public void OneApplyFailureIsTerminalAndDoesNotPreventTheNextContract()
        {
            var first = new FakeHostBinding(
                "first",
                () => throw new InvalidOperationException("first setter failed"));
            var secondApplies = 0;
            var second = new FakeHostBinding("second", () =>
            {
                secondApplies++;
                return true;
            });

            Assert.False(FoxRunRos2ApplyIsolation.TryRun(first, 1, out var firstFailure));
            Assert.IsType<InvalidOperationException>(firstFailure);
            Assert.True(FoxRunRos2ApplyIsolation.TryRun(second, 1, out var secondFailure));
            Assert.Null(secondFailure);
            Assert.Equal(1, secondApplies);
            Assert.Equal(FoxRunRos2SubscriptionBindingState.Failed, first.State);
            Assert.True(first.TryGetSnapshot(1, out var snapshot));
            Assert.Equal(FoxRunRos2RegistrationError.ApplyFailure, snapshot.Error);
            Assert.Equal("The native ROS2 subscription could not apply the copied message.", snapshot.Diagnostic);
        }

        private static FoxRunRos2GeneratedContract Contract(
            string provider,
            string qos,
            Unity.FoxgloveSDK.Components.FoxRunFlow mode =
                Unity.FoxgloveSDK.Components.FoxRunFlow.Subscribe,
            bool supportsNative = true)
        {
            var hasExplicitQosProfile = TryParseQosProfile(qos, out var qosProfile);
            return new FoxRunRos2GeneratedContract(
                "host-contract-" + provider + "-" + qos,
                "/native/string",
                "Demo.HostReceiver",
                "_incoming",
                "std_msgs/msg/String",
                mode,
                ParseProvider(provider),
                qosProfile,
                hasExplicitQosProfile,
                qosReliability: default,
                hasExplicitQosReliability: false,
                qosDurability: default,
                hasExplicitQosDurability: false,
                qosHistory: default,
                hasExplicitQosHistory: false,
                qosDepth: 0,
                hasExplicitQosDepth: false,
                supportsRos2Native: supportsNative);
        }

        private static FoxRunRos2GeneratedContract CustomContract(
            Unity.FoxgloveSDK.Components.FoxRunEndpoint provider,
            Unity.FoxgloveSDK.Components.FoxRunEncoding encoding)
            => new FoxRunRos2GeneratedContract(
                "custom-contract",
                "/native/custom",
                "Demo.CustomReceiver",
                "_incoming",
                "unity2foxglove_foxrun_interfaces_v1/msg/CustomEnvelope",
                Unity.FoxgloveSDK.Components.FoxRunFlow.PublishAndSubscribe,
                provider,
                Unity.FoxgloveSDK.Components.FoxRunQosProfile.Default,
                hasExplicitQosProfile: true,
                qosReliability: default,
                hasExplicitQosReliability: false,
                qosDurability: default,
                hasExplicitQosDurability: false,
                qosHistory: default,
                hasExplicitQosHistory: false,
                qosDepth: 0,
                hasExplicitQosDepth: false,
                supportsRos2Native: true,
                declaredSubscriptionEncoding: encoding,
                contractKind: FoxRunRos2GeneratedContractKind.CustomInterface,
                staticInterfacePackageId: "dev.unity2foxglove.foxrun.ros2.interfaces",
                rosPackageName: "unity2foxglove_foxrun_interfaces_v1",
                interfaceRevision: 1,
                interfaceDigest: "120864853239fae290b5199cd02dbf02f107299bccd8972b06d8cf59fc7594fd",
                baseRuntimePackageId: "dev.unity2foxglove.ros2forunity.runtime.jazzy.win64",
                canonicalPayloadType: "unity2foxglove_foxrun_interfaces_v1/msg/Custom");

        private static Unity.FoxgloveSDK.Components.FoxRunEndpoint ParseProvider(string provider)
            => provider == "ros2-native"
                ? Unity.FoxgloveSDK.Components.FoxRunEndpoint.Ros2Native
                : (Unity.FoxgloveSDK.Components.FoxRunEndpoint)0;

        private static bool TryParseQosProfile(
            string qos,
            out Unity.FoxgloveSDK.Components.FoxRunQosProfile profile)
        {
            switch (qos)
            {
                case "reliable":
                    profile = Unity.FoxgloveSDK.Components.FoxRunQosProfile.Default;
                    return true;
                case "sensor-data":
                    profile = Unity.FoxgloveSDK.Components.FoxRunQosProfile.SensorData;
                    return true;
                case "default":
                    profile = Unity.FoxgloveSDK.Components.FoxRunQosProfile.Default;
                    return true;
                case "inherit":
                    profile = default;
                    return false;
                default:
                    throw new ArgumentOutOfRangeException(nameof(qos), qos, "Unknown test QoS.");
            }
        }

        private static void AssertResolvedQos(
            Unity.FoxgloveSDK.Components.FoxRunResolvedQos expected,
            Unity.FoxgloveSDK.Components.FoxRunResolvedQos actual)
        {
            Assert.Equal(expected, actual);
            Assert.Equal(expected.Profile, actual.Profile);
            Assert.Equal(expected.Reliability, actual.Reliability);
            Assert.Equal(expected.Durability, actual.Durability);
            Assert.Equal(expected.History, actual.History);
            Assert.Equal(expected.Depth, actual.Depth);
        }

        private static FoxRunRos2SubscriptionBindingSnapshot Snapshot(
            string id,
            FoxRunRos2SubscriptionBindingState state,
            FoxRunRos2RegistrationError error,
            string diagnostic,
            long received = 0)
            => new FoxRunRos2SubscriptionBindingSnapshot(
                id,
                1,
                state,
                error,
                diagnostic,
                received,
                0,
                0,
                0,
                0,
                0,
                0);

        private sealed class FakeHostMessage : ROS2.Message, IDisposable
        {
            public bool IsDisposed { get; private set; }

            public void Dispose() => IsDisposed = true;
        }

        private sealed class FakeHostBinding : IFoxRunRos2HostBinding
        {
            private readonly Func<bool> _apply;
            private FoxRunRos2RegistrationResult _result = FoxRunRos2RegistrationResult.Success();

            internal FakeHostBinding(string id, Func<bool> apply)
            {
                Contract = new FoxRunRos2GeneratedContract(
                    id, "/" + id, "Demo.Host", "_message", "std_msgs/msg/String",
                    Unity.FoxgloveSDK.Components.FoxRunFlow.Subscribe,
                    Unity.FoxgloveSDK.Components.FoxRunEndpoint.Ros2Native,
                    Unity.FoxgloveSDK.Components.FoxRunQosProfile.Default,
                    hasExplicitQosProfile: true,
                    qosReliability: default,
                    hasExplicitQosReliability: false,
                    qosDurability: default,
                    hasExplicitQosDurability: false,
                    qosHistory: default,
                    hasExplicitQosHistory: false,
                    qosDepth: 0,
                    hasExplicitQosDepth: false,
                    supportsRos2Native: true);
                _apply = apply;
                State = FoxRunRos2SubscriptionBindingState.Ready;
            }

            public FoxRunRos2GeneratedContract Contract { get; }
            public string ContractId => Contract.Id;
            public long SessionGeneration => 1;
            public FoxRunRos2SubscriptionBindingState State { get; private set; }
            public FoxRunRos2RegistrationResult TryRegister() => _result;
            public bool TryApplyLatest(long activeSessionGeneration) => _apply();
            public void RecordApplyFailure(Exception exception)
            {
                State = FoxRunRos2SubscriptionBindingState.Failed;
                _result = FoxRunRos2RegistrationResult.Failure(
                    FoxRunRos2RegistrationError.ApplyFailure,
                    exception.GetType().Name + ": " + exception.Message);
            }
            public bool TryGetSnapshot(long activeSessionGeneration, out FoxRunRos2SubscriptionBindingSnapshot snapshot)
            {
                snapshot = Snapshot(ContractId, State, _result.Error, _result.Diagnostic);
                return activeSessionGeneration == SessionGeneration;
            }
            public FoxRunRos2AcceptanceArmStatus ArmAcceptanceAttempt(
                out FoxRunRos2AcceptanceAttemptSnapshot snapshot)
            {
                snapshot = default;
                return FoxRunRos2AcceptanceArmStatus.EndpointUnavailable;
            }
            public bool TryGetAcceptanceAttempt(out FoxRunRos2AcceptanceAttemptSnapshot snapshot)
            {
                snapshot = default;
                return false;
            }
            public bool EndAcceptanceAttempt(long epoch) => false;
            public bool TryCompleteAcceptanceAttempt(
                long epoch,
                out FoxRunRos2AcceptanceAttemptSnapshot snapshot)
            {
                snapshot = default;
                return false;
            }
            public void Stop() => State = FoxRunRos2SubscriptionBindingState.Stopped;
        }

        private sealed class NativeOnlySource : UnityEngine.MonoBehaviour, IFoxRunRos2SubscriptionSource
        {
            public int FoxRunRos2SubscriptionCount => 1;

            public void FoxRunRos2RegisterSubscriptions(IFoxRunRos2SubscriptionRegistrar registrar)
            {
            }
        }

        private sealed class CustomNativeOnlySource : UnityEngine.MonoBehaviour, IFoxRunRos2CustomSubscriptionSource
        {
            public int FoxRunRos2CustomSubscriptionCount => 1;

            public void FoxRunRos2RegisterCustomSubscriptions(IFoxRunRos2SubscriptionRegistrar registrar)
            {
            }
        }

        private sealed class HostManagedQosProfile : IFoxRunRos2NativeQosProfile
        {
            public ROS2.QualityOfServiceProfile NativeProfile { get; } = null;
            public void SetHistory(ROS2.HistoryPolicy history, int depth) { }
            public void SetPolicies(ROS2.HistoryPolicy history, int depth, ROS2.ReliabilityPolicy reliability, ROS2.DurabilityPolicy durability) { }
            public void Dispose() { }
        }

        private sealed class FakeR2fuNodeDriver : IFoxRunRos2R2fuNodeDriver
        {
            public int CreateCount { get; private set; }
            public int RemoveCount { get; private set; }
            public int ReleaseNodeCount { get; private set; }
            public List<ROS2.QualityOfServiceProfile> SeenQos { get; } = new List<ROS2.QualityOfServiceProfile>();
            public bool RemoveReturns { get; set; } = true;

            public object CreateSubscription<T>(string topic, Action<T> callback, ROS2.QualityOfServiceProfile qos)
                where T : ROS2.Message, new()
            {
                CreateCount++;
                SeenQos.Add(qos);
                return new object();
            }

            public bool IsSubscriptionUsable(object subscription) => subscription != null;

            public bool RemoveSubscription(object subscription)
            {
                Assert.NotNull(subscription);
                RemoveCount++;
                return RemoveReturns;
            }

            public object CreatePublisher<T>(string topic, ROS2.QualityOfServiceProfile qos)
                where T : ROS2.Message, new()
                => new object();

            public bool IsPublisherUsable<T>(object publisher)
                where T : ROS2.Message, new()
                => publisher != null;

            public bool Publish<T>(object publisher, T message)
                where T : ROS2.Message, new()
                => publisher != null && message != null;

            public bool RemovePublisher<T>(object publisher)
                where T : ROS2.Message, new()
                => publisher != null;

            public void ReleaseNode() => ReleaseNodeCount++;
        }
    }
}
#endif
