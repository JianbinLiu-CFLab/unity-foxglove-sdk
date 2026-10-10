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
        public void SubscriptionHubReleasesHostOwnershipWhenDeferredCleanupTimesOut()
        {
            var cleanupOrder = new List<string>();
            var queue = new FoxRunRos2HostCleanupQueue(
                Thread.CurrentThread.ManagedThreadId);
            var binding = new FakeDeferredHostedCleanup(
                queue,
                cleanupOrder,
                dispatchCleanup: false);
            var hostReleaseCount = 0;

            FoxRunRos2SubscriptionHub.StopHostedBindingsAndDrainDeferredCleanupThenReleaseHost(
                new IFoxRunRos2SubscriptionHostedCleanup[] { binding },
                queue,
                TimeSpan.Zero,
                _ => { },
                () =>
                {
                    hostReleaseCount++;
                    return true;
                },
                out var cleanupComplete);

            Assert.False(cleanupComplete);
            Assert.Equal(1, hostReleaseCount);
            Assert.Equal(new[] { "stop" }, cleanupOrder);
        }

        [Fact]
        [Trait("Phase", "184-G")]
        public void SubscriptionHubRehydratesHostCleanupQueueBeforeLifecyclePause()
        {
            var hub = new FoxRunRos2SubscriptionHub();
            var queueField = typeof(FoxRunRos2SubscriptionHub).GetField(
                "_hostCleanupQueue",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var pause = typeof(FoxRunRos2SubscriptionHub).GetMethod(
                "PauseForLifecycleWindow",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(queueField);
            Assert.NotNull(pause);
            Assert.Null(queueField.GetValue(hub));

            var failure = Record.Exception(() => pause.Invoke(hub, null));

            Assert.Null(failure);
            var queue = Assert.IsType<FoxRunRos2HostCleanupQueue>(
                queueField.GetValue(hub));
            var cleanupCount = 0;
            queue.Dispatch(() => cleanupCount++);
            Assert.Equal(1, cleanupCount);
        }

        [Fact]
        public void HostCleanupQueuePostsAHostDrainIndependentOfHubUpdate()
        {
            var hostContext = new CapturingSynchronizationContext();
            var queue = new FoxRunRos2HostCleanupQueue(
                Thread.CurrentThread.ManagedThreadId,
                hostContext,
                _ => { });
            var cleanupCount = 0;
            var dispatchThread = new Thread(
                () => queue.Dispatch(() => cleanupCount++))
            {
                IsBackground = true
            };

            dispatchThread.Start();
            Assert.True(dispatchThread.Join(TimeSpan.FromSeconds(5)));
            Assert.Equal(0, cleanupCount);
            Assert.True(hostContext.RunOne());
            Assert.Equal(1, cleanupCount);
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
            Assert.True(binding.CleanupPending);
            Assert.Equal(0, backend.ReleaseCount);
            Assert.True(binding.TryGetSnapshot(32, out var snapshot));
            Assert.Equal(FoxRunRos2RegistrationError.TeardownFailure, snapshot.Error);
            Assert.Equal("The native ROS2 subscription did not complete teardown.", snapshot.Diagnostic);
            Assert.Equal(
                new[] { "remove-subscription", "clear-applied", "dispose-owned" },
                events);
            Assert.Equal(1, backend.RemoveCount);

            backend.RemoveException = null;
            backend.ReleaseException = null;
            binding.Stop();

            Assert.False(binding.CleanupPending);
            Assert.Equal(2, backend.RemoveCount);
            Assert.Equal(1, backend.ReleaseCount);
            Assert.Equal(
                new[]
                {
                    "remove-subscription",
                    "clear-applied",
                    "dispose-owned",
                    "remove-subscription",
                    "release-node"
                },
                events);
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

    }
}
#endif
