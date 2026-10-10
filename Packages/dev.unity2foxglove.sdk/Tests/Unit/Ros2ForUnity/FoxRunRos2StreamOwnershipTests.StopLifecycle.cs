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
        public void StopClosesAdmissionBeforeRemovalThenDrainsClearsAndReleases()
        {
            var backend = new FakeBackend();
            var admitted = 0;
            var cleared = 0;
            var binding = Binding(
                backend,
                tryAdmitInput: () =>
                {
                    admitted++;
                    return true;
                },
                materializeOwned: (message, _) => new OwnedSample(message.Data),
                transferOwned: _ => { },
                clearOwned: () =>
                {
                    cleared++;
                    backend.Events.Add("clear");
                });

            Assert.True(binding.TryRegister().Succeeded);
            backend.Invoke(new FakeMessage { Data = "accepted" });
            binding.Stop();
            backend.InvokeLate(new FakeMessage { Data = "late" });
            binding.Stop();

            Assert.Equal(1, admitted);
            Assert.Equal(1, backend.RemoveCount);
            Assert.Equal(1, cleared);
            Assert.Equal(1, backend.ReleaseCount);
            Assert.Equal("remove,clear,release", string.Join(",", backend.Events));
        }

        [Fact]
        public void StopRetainsNativeTokenUntilRemovalSucceeds()
        {
            var backend = new FakeBackend
            {
                RemoveException = new InvalidOperationException("native removal failed")
            };
            var cleared = 0;
            var binding = Binding(
                backend,
                tryAdmitInput: () => true,
                materializeOwned: (message, _) => new OwnedSample(message.Data),
                transferOwned: _ => { },
                clearOwned: () =>
                {
                    cleared++;
                    backend.Events.Add("clear");
                });

            Assert.True(binding.TryRegister().Succeeded);

            var exception = Assert.Throws<InvalidOperationException>(binding.Stop);

            Assert.Equal("native removal failed", exception.Message);
            Assert.Equal(1, backend.RemoveCount);
            Assert.Equal(0, cleared);
            Assert.Equal(0, backend.ReleaseCount);

            backend.RemoveException = null;
            binding.Stop();

            Assert.Equal(2, backend.RemoveCount);
            Assert.Equal(1, cleared);
            Assert.Equal(1, backend.ReleaseCount);
            Assert.Equal("remove,remove,clear,release", string.Join(",", backend.Events));
        }

        [Fact]
        public void StopRetainsNodeOwnershipUntilReleaseSucceeds()
        {
            var backend = new FakeBackend { ReleaseFailuresRemaining = 1 };
            var binding = Binding(
                backend,
                tryAdmitInput: () => true,
                materializeOwned: (message, _) => new OwnedSample(message.Data),
                transferOwned: _ => { },
                clearOwned: () => backend.Events.Add("clear"));

            Assert.True(binding.TryRegister().Succeeded);
            binding.Stop();

            Assert.Equal(1, backend.ReleaseCount);
            Assert.False(((IFoxRunRos2DeferredCleanupStatus)binding).CleanupComplete);

            binding.Stop();

            Assert.Equal(2, backend.ReleaseCount);
            Assert.True(((IFoxRunRos2DeferredCleanupStatus)binding).CleanupComplete);
            Assert.Equal("remove,clear,release,clear,release", string.Join(",", backend.Events));
        }

        [Fact]
        public async Task StopRaceRoutesMaterializedOwnershipThroughStreamDiagnostics()
        {
            var backend = new FakeBackend();
            using var materializeEntered = new ManualResetEventSlim();
            using var finishMaterialize = new ManualResetEventSlim();
            var stream = new FoxRunStream<OwnedSample>(new FoxRunStreamOptions(
                capacity: 4,
                maxInputHz: 1000,
                maxBatch: 4,
                overflow: FoxRunStreamOverflowPolicy.DropOldest));
            Action<OwnedSample> throwingDisposer = _ =>
                throw new InvalidOperationException("owned disposal failed");
            var binding = Binding(
                backend,
                tryAdmitInput: stream.TryAdmitInput,
                materializeOwned: (message, _) =>
                {
                    materializeEntered.Set();
                    Assert.True(finishMaterialize.Wait(TimeSpan.FromSeconds(5)));
                    return new OwnedSample(message.Data);
                },
                transferOwned: owned => stream.TryEnqueueOwnedAfterAdmission(owned, throwingDisposer),
                clearOwned: () => stream.Clear());

            Assert.True(binding.TryRegister().Succeeded);
            var callback = Task.Run(() => backend.Invoke(new FakeMessage { Data = "racing" }));
            Assert.True(materializeEntered.Wait(TimeSpan.FromSeconds(5)));
            var stop = Task.Run(binding.Stop);
            Assert.True(SpinWait.SpinUntil(
                () => Volatile.Read(ref backend.RemoveCount) == 1,
                TimeSpan.FromSeconds(5)));
            finishMaterialize.Set();
            await Task.WhenAll(callback, stop).WaitAsync(TimeSpan.FromSeconds(5));

            var stats = stream.Stats;
            Assert.Equal(1, stats.DisposalFailures);
            Assert.Contains("owned disposal failed", stats.LastDisposalError, StringComparison.Ordinal);
            Assert.Equal(1, stats.Cleared);
        }

        [Fact]
        public async Task StopReturnsWithoutWaitingForAnInFlightNativeCallback()
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
                clearOwned: () => backend.Events.Add("clear"));

            Assert.True(binding.TryRegister().Succeeded);
            var callback = Task.Run(() => backend.Invoke(new FakeMessage { Data = "blocked" }));
            Assert.True(materializeEntered.Wait(TimeSpan.FromSeconds(5)));
            var stop = Task.Run(binding.Stop);
            Assert.True(SpinWait.SpinUntil(
                () => Volatile.Read(ref backend.RemoveCount) == 1,
                TimeSpan.FromSeconds(5)));
            var returnedBeforeCallback = await Task.WhenAny(
                stop,
                Task.Delay(TimeSpan.FromSeconds(1))) == stop;

            finishMaterialize.Set();
            await Task.WhenAll(callback, stop).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(returnedBeforeCallback);
            Assert.Equal(1, backend.RemoveCount);
            Assert.Equal(1, backend.ReleaseCount);
            Assert.Equal("remove,clear,release", string.Join(",", backend.Events));
        }

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

    }
}
#endif
