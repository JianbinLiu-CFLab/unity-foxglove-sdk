// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Ros2ForUnity
// Purpose: Lock the native bounded-stream callback and teardown ownership contract.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Unity.FoxgloveSDK.Components;
using Unity2Foxglove.Ros2ForUnity.Native;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Ros2ForUnity
{


public sealed partial class FoxRunRos2StreamOwnershipTests
    {
        [Fact]
        public void InFlightCallbackMakesPendingTeardownObservableUntilHostCleanupRuns()
        {
            var dispatcher = new TestHostDispatcher(Thread.CurrentThread.ManagedThreadId);
            var backend = new FakeBackend();
            using var materializeEntered = new ManualResetEventSlim();
            using var finishMaterialize = new ManualResetEventSlim();
            var binding = Binding(
                backend,
                tryAdmitInput: () => true,
                materializeOwned: (message, _) =>
                {
                    materializeEntered.Set();
                    Assert.True(finishMaterialize.Wait(TimeSpan.FromSeconds(5)));
                    return new OwnedSample(message.Data);
                },
                transferOwned: _ => { },
                clearOwned: () => backend.Events.Add("clear"),
                dispatchCleanup: dispatcher.Dispatch);
            Assert.True(binding.TryRegister().Succeeded);
            var callback = new Thread(
                () => backend.Invoke(new FakeMessage { Data = "blocked" }))
            {
                IsBackground = true
            };
            callback.Start();
            Assert.True(materializeEntered.Wait(TimeSpan.FromSeconds(5)));

            binding.Stop();

            Assert.True(binding.TryGetSnapshot(7, out var pending));
            Assert.Equal(FoxRunRos2RegistrationError.TeardownFailure, pending.Error);
            Assert.Equal(0, backend.ReleaseCount);
            Assert.False(
                ((IFoxRunRos2DeferredCleanupStatus)binding).CleanupComplete);
            finishMaterialize.Set();
            Assert.True(callback.Join(TimeSpan.FromSeconds(5)));
            Assert.Equal(1, dispatcher.PendingCount);

            dispatcher.RunPending();

            Assert.True(binding.TryGetSnapshot(7, out var stopped));
            Assert.Equal(FoxRunRos2RegistrationError.Stopped, stopped.Error);
            Assert.Equal(1, backend.ReleaseCount);
            Assert.True(
                ((IFoxRunRos2DeferredCleanupStatus)binding).CleanupComplete);
            Assert.Equal("remove,clear,release", string.Join(",", backend.Events));
        }

        [Fact]
        public void RepeatedStopRetriesCleanupAfterHostDispatchFailure()
        {
            var hostThreadId = Thread.CurrentThread.ManagedThreadId;
            var backend = new FakeBackend();
            using var materializeEntered = new ManualResetEventSlim();
            using var finishMaterialize = new ManualResetEventSlim();
            var clearThreadId = 0;
            var binding = Binding(
                backend,
                tryAdmitInput: () => true,
                materializeOwned: (message, _) =>
                {
                    materializeEntered.Set();
                    Assert.True(finishMaterialize.Wait(TimeSpan.FromSeconds(5)));
                    return new OwnedSample(message.Data);
                },
                transferOwned: _ => { },
                clearOwned: () =>
                {
                    clearThreadId = Thread.CurrentThread.ManagedThreadId;
                    backend.Events.Add("clear");
                },
                dispatchCleanup: _ => throw new InvalidOperationException("host unavailable"));
            Assert.True(binding.TryRegister().Succeeded);
            var callback = new Thread(
                () => backend.Invoke(new FakeMessage { Data = "blocked" }))
            {
                IsBackground = true
            };
            callback.Start();
            Assert.True(materializeEntered.Wait(TimeSpan.FromSeconds(5)));

            binding.Stop();
            finishMaterialize.Set();
            Assert.True(callback.Join(TimeSpan.FromSeconds(5)));

            Assert.True(binding.TryGetSnapshot(7, out var failedDispatch));
            Assert.Equal(FoxRunRos2RegistrationError.TeardownFailure, failedDispatch.Error);
            Assert.Equal(0, backend.ReleaseCount);

            binding.Stop();

            Assert.Equal(hostThreadId, clearThreadId);
            Assert.Equal(hostThreadId, backend.ReleaseThreadId);
            Assert.Equal(1, backend.ReleaseCount);
            Assert.True(binding.TryGetSnapshot(7, out var retainedFailure));
            Assert.Equal(FoxRunRos2RegistrationError.TeardownFailure, retainedFailure.Error);
        }

        [Fact]
        public async Task CompetingCleanupCannotReleaseNodeBeforeOwnedValuesFinishClearing()
        {
            var backend = new FakeBackend();
            using var clearEntered = new ManualResetEventSlim();
            using var finishClear = new ManualResetEventSlim();
            var binding = Binding(
                backend,
                tryAdmitInput: () => true,
                materializeOwned: (message, _) => new OwnedSample(message.Data),
                transferOwned: _ => { },
                clearOwned: () =>
                {
                    backend.Events.Add("clear");
                    clearEntered.Set();
                    Assert.True(finishClear.Wait(TimeSpan.FromSeconds(5)));
                });
            var cleanup = binding.GetType().GetMethod(
                "TryCompleteStoppedCleanup",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(cleanup);
            Assert.True(binding.TryRegister().Succeeded);

            var stop = Task.Run(binding.Stop);
            Assert.True(clearEntered.Wait(TimeSpan.FromSeconds(5)));
            var competitor = Task.Run(() => cleanup.Invoke(binding, null));
            await competitor.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(0, backend.ReleaseCount);
            finishClear.Set();
            await stop.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, backend.ReleaseCount);
            Assert.Equal("remove,clear,release", string.Join(",", backend.Events));
        }

        [Fact]
        public async Task DeferredCleanupFailureRemainsVisibleInTheBindingSnapshot()
        {
            var backend = new FakeBackend();
            using var materializeEntered = new ManualResetEventSlim();
            using var finishMaterialize = new ManualResetEventSlim();
            var binding = Binding(
                backend,
                tryAdmitInput: () => true,
                materializeOwned: (message, _) =>
                {
                    materializeEntered.Set();
                    Assert.True(finishMaterialize.Wait(TimeSpan.FromSeconds(5)));
                    return new OwnedSample(message.Data);
                },
                transferOwned: _ => { },
                clearOwned: () => throw new InvalidOperationException("deferred clear failed"));
            Assert.True(binding.TryRegister().Succeeded);
            var callback = Task.Run(() => backend.Invoke(new FakeMessage { Data = "blocked" }));
            Assert.True(materializeEntered.Wait(TimeSpan.FromSeconds(5)));

            binding.Stop();
            finishMaterialize.Set();
            await callback.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(binding.TryGetSnapshot(7, out var snapshot));
            Assert.Equal(FoxRunRos2RegistrationError.TeardownFailure, snapshot.Error);
            Assert.Equal(
                "The native ROS2 subscription did not complete teardown.",
                snapshot.Diagnostic);
            var lastRegistration = binding.GetType().GetField(
                "_lastRegistration",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(lastRegistration);
            var registration = Assert.IsType<FoxRunRos2RegistrationResult>(
                lastRegistration.GetValue(binding));
            Assert.Equal("InvalidOperationException", registration.FailureKind);
            Assert.Equal(1, backend.ReleaseCount);
        }

        [Fact]
        public void DeferredCleanupRunsOnlyThroughTheHostThreadDispatcher()
        {
            var hostThreadId = Thread.CurrentThread.ManagedThreadId;
            var dispatcher = new TestHostDispatcher(hostThreadId);
            var backend = new FakeBackend();
            using var materializeEntered = new ManualResetEventSlim();
            using var finishMaterialize = new ManualResetEventSlim();
            var clearThreadId = 0;
            var binding = Binding(
                backend,
                tryAdmitInput: () => true,
                materializeOwned: (message, _) =>
                {
                    materializeEntered.Set();
                    Assert.True(finishMaterialize.Wait(TimeSpan.FromSeconds(5)));
                    return new OwnedSample(message.Data);
                },
                transferOwned: _ => { },
                clearOwned: () =>
                {
                    clearThreadId = Thread.CurrentThread.ManagedThreadId;
                    backend.Events.Add("clear");
                },
                dispatchCleanup: dispatcher.Dispatch);
            Assert.True(binding.TryRegister().Succeeded);
            var callback = new Thread(
                () => backend.Invoke(new FakeMessage { Data = "blocked" }))
            {
                IsBackground = true
            };
            callback.Start();
            Assert.True(materializeEntered.Wait(TimeSpan.FromSeconds(5)));

            binding.Stop();
            finishMaterialize.Set();
            Assert.True(callback.Join(TimeSpan.FromSeconds(5)));

            Assert.Equal(1, dispatcher.PendingCount);
            Assert.Equal(0, clearThreadId);
            Assert.Equal(0, backend.ReleaseCount);
            dispatcher.RunPending();

            Assert.Equal(hostThreadId, clearThreadId);
            Assert.Equal(hostThreadId, backend.ReleaseThreadId);
            Assert.Equal(1, backend.ReleaseCount);
            Assert.Equal("remove,clear,release", string.Join(",", backend.Events));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void FailedPartialRegistrationRollsBackTokenAndClearsSynchronousOwnedInput(
            bool tokenInspectionThrows)
        {
            var backend = new FakeBackend
            {
                ReturnedToken = tokenInspectionThrows
                    ? new FakeToken(isUsable: true, throwOnInspection: true)
                    : new FakeToken(isUsable: false),
                InvokeSynchronouslyOnRegister = true
            };
            var disposed = 0;
            var cleared = 0;
            var stream = new FoxRunStream<OwnedSample>();
            var binding = Binding(
                backend,
                tryAdmitInput: stream.TryAdmitInput,
                materializeOwned: (message, _) => new OwnedSample(message.Data),
                transferOwned: owned => stream.TryEnqueueOwnedAfterAdmission(
                    owned,
                    _ => Interlocked.Increment(ref disposed)),
                clearOwned: () =>
                {
                    cleared++;
                    backend.Events.Add("clear");
                    stream.Clear();
                });

            var result = binding.TryRegister();

            Assert.False(result.Succeeded);
            Assert.Equal(
                tokenInspectionThrows
                    ? FoxRunRos2RegistrationError.BackendFailure
                    : FoxRunRos2RegistrationError.InvalidSubscriptionToken,
                result.Error);
            Assert.Equal(1, backend.RemoveCount);
            Assert.Equal(1, cleared);
            Assert.Equal(1, disposed);
            Assert.Equal(0, stream.Count);
            Assert.Equal(0, backend.ReleaseCount);
            Assert.Equal("remove,clear", string.Join(",", backend.Events));

            binding.Stop();

            Assert.Equal(2, cleared);
            Assert.Equal(1, backend.ReleaseCount);
            Assert.Equal("remove,clear,clear,release", string.Join(",", backend.Events));
        }

        [Fact]
        public void FailedRegistrationRetainsRollbackTokenUntilRemovalSucceeds()
        {
            var backend = new FakeBackend
            {
                ReturnedToken = new FakeToken(isUsable: false),
                RemoveException = new InvalidOperationException("rollback pending")
            };
            var binding = Binding(
                backend,
                tryAdmitInput: () => true,
                materializeOwned: (message, _) => new OwnedSample(message.Data),
                transferOwned: _ => { },
                clearOwned: () => { });

            var first = binding.TryRegister();

            Assert.False(first.Succeeded);
            Assert.Equal(1, backend.RemoveCount);
            Assert.True(binding.CanRetryRegistration);

            backend.RemoveException = null;
            backend.ReturnedToken = new FakeToken(isUsable: true);

            var second = binding.TryRegister();

            Assert.True(second.Succeeded);
            Assert.Equal(2, backend.RemoveCount);
            binding.Stop();
        }

        [Fact]
        public void RuntimeUnavailableRegistrationCanRetryBeforeTerminalCleanup()
        {
            var backend = new FakeBackend
            {
                RegistrationFailuresRemaining = 1,
                InvokeSynchronouslyOnRegister = true
            };
            var disposed = 0;
            var cleared = 0;
            long ticks = 0;
            var stream = new FoxRunStream<OwnedSample>(
                new FoxRunStreamOptions(),
                () => Interlocked.Increment(ref ticks),
                1000L);
            var binding = Binding(
                backend,
                tryAdmitInput: stream.TryAdmitInput,
                materializeOwned: (message, _) => new OwnedSample(message.Data),
                transferOwned: owned => stream.TryEnqueueOwnedAfterAdmission(
                    owned,
                    _ => Interlocked.Increment(ref disposed)),
                clearOwned: () =>
                {
                    cleared++;
                    backend.Events.Add("clear");
                    stream.Clear();
                });

            var waiting = binding.TryRegister();

            Assert.False(waiting.Succeeded);
            Assert.Equal(FoxRunRos2RegistrationError.RuntimeUnavailable, waiting.Error);
            Assert.Equal(FoxRunRos2SubscriptionBindingState.WaitingForRuntime, binding.State);
            Assert.Equal(1, cleared);
            Assert.Equal(1, disposed);
            Assert.Equal(0, backend.ReleaseCount);

            var ready = binding.TryRegister();

            Assert.True(ready.Succeeded);
            Assert.Equal(FoxRunRos2SubscriptionBindingState.Ready, binding.State);
            Assert.Equal(1, stream.Count);
            Assert.Equal(0, backend.ReleaseCount);

            binding.Stop();

            Assert.Equal(2, cleared);
            Assert.Equal(2, disposed);
            Assert.Equal(1, backend.RemoveCount);
            Assert.Equal(1, backend.ReleaseCount);
            Assert.Equal("clear,remove,clear,release", string.Join(",", backend.Events));
        }
    }
}
#endif
