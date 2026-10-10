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

        [Fact]
        public void FailedRegistrationReturnsWhileItsSynchronousOwnedCallbackIsStillInFlight()
        {
            var hostThreadId = Thread.CurrentThread.ManagedThreadId;
            var dispatcher = new TestHostDispatcher(hostThreadId);
            using var materializeEntered = new ManualResetEventSlim();
            using var finishMaterialize = new ManualResetEventSlim();
            var backend = new FakeBackend
            {
                ReturnedToken = new FakeToken(isUsable: false),
                InvokeAsynchronouslyOnRegister = true,
                AsyncCallbackEntered = materializeEntered
            };
            var cleared = 0;
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
                    cleared++;
                    backend.Events.Add("clear");
                },
                dispatchCleanup: dispatcher.Dispatch);
            FoxRunRos2RegistrationResult registration = default;
            Exception registrationFailure = null;
            var registerThread = new Thread(() =>
            {
                try
                {
                    registration = binding.TryRegister();
                }
                catch (Exception exception)
                {
                    registrationFailure = exception;
                }
            }) { IsBackground = true };
            registerThread.Start();
            Assert.True(materializeEntered.Wait(TimeSpan.FromSeconds(5)));

            var returnedBeforeCallback = registerThread.Join(TimeSpan.FromSeconds(1));
            finishMaterialize.Set();
            Assert.True(backend.JoinRegistrationCallback(TimeSpan.FromSeconds(5)));
            Assert.True(registerThread.Join(TimeSpan.FromSeconds(5)));
            dispatcher.RunPending();

            Assert.True(returnedBeforeCallback);
            Assert.Null(registrationFailure);
            Assert.False(registration.Succeeded);
            Assert.Equal(FoxRunRos2RegistrationError.InvalidSubscriptionToken, registration.Error);
            Assert.Equal(1, cleared);
            Assert.Equal(0, backend.ReleaseCount);

            backend.InvokeAsynchronouslyOnRegister = false;
            backend.ReturnedToken = new FakeToken();
            Assert.True(binding.TryRegister().Succeeded);
            binding.Stop();
            Assert.Equal(1, backend.ReleaseCount);
        }

        [Fact]
        public void StopRetriesFailedRegistrationCleanupAfterHostDispatchFailure()
        {
            using var materializeEntered = new ManualResetEventSlim();
            using var finishMaterialize = new ManualResetEventSlim();
            var backend = new FakeBackend
            {
                ReturnedToken = new FakeToken(isUsable: false),
                InvokeAsynchronouslyOnRegister = true,
                AsyncCallbackEntered = materializeEntered
            };
            var cleared = 0;
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
                    cleared++;
                    backend.Events.Add("clear");
                },
                dispatchCleanup: _ => throw new InvalidOperationException("host unavailable"));

            var registration = binding.TryRegister();
            Assert.False(registration.Succeeded);
            finishMaterialize.Set();
            Assert.True(backend.JoinRegistrationCallback(TimeSpan.FromSeconds(5)));
            Assert.Equal(0, cleared);
            Assert.Equal(0, backend.ReleaseCount);

            binding.Stop();
            binding.Stop();

            Assert.Equal(1, cleared);
            Assert.Equal(1, backend.ReleaseCount);
            Assert.True(binding.TryGetSnapshot(7, out var snapshot));
            Assert.Equal(FoxRunRos2RegistrationError.TeardownFailure, snapshot.Error);
        }

        [Fact]
        public void StopDefersCleanupUntilBlockedRegistrationRollsBackInOwnershipOrder()
        {
            using var registerEntered = new ManualResetEventSlim();
            using var releaseRegister = new ManualResetEventSlim();
            var backend = new FakeBackend
            {
                RegisterEntered = registerEntered,
                ReleaseRegister = releaseRegister
            };
            var binding = Binding(
                backend,
                tryAdmitInput: () => true,
                materializeOwned: (message, _) => new OwnedSample(message.Data),
                transferOwned: _ => { },
                clearOwned: () => backend.Events.Add("clear"));
            FoxRunRos2RegistrationResult registration = default;
            Exception registrationFailure = null;
            var registerThread = new Thread(() =>
            {
                try
                {
                    registration = binding.TryRegister();
                }
                catch (Exception exception)
                {
                    registrationFailure = exception;
                }
            }) { IsBackground = true };
            registerThread.Start();
            Assert.True(registerEntered.Wait(TimeSpan.FromSeconds(5)));

            var stopThread = new Thread(binding.Stop) { IsBackground = true };
            stopThread.Start();

            Assert.True(stopThread.Join(TimeSpan.FromSeconds(2)));
            Assert.Equal(FoxRunRos2SubscriptionBindingState.Stopped, binding.State);
            Assert.Equal(0, backend.RemoveCount);
            Assert.Equal(0, backend.ReleaseCount);
            Assert.Empty(backend.Events);

            releaseRegister.Set();
            Assert.True(registerThread.Join(TimeSpan.FromSeconds(5)));

            Assert.Null(registrationFailure);
            Assert.False(registration.Succeeded);
            Assert.Equal(FoxRunRos2RegistrationError.Stopped, registration.Error);
            Assert.Equal(1, backend.RemoveCount);
            Assert.Equal(1, backend.ReleaseCount);
            Assert.Equal("remove,clear,release", string.Join(",", backend.Events));
        }

        [Fact]
        public void FatalMaterializerFailureEscapesCallbackAndStopStillCleansUp()
        {
            var backend = new FakeBackend();
            var binding = Binding(
                backend,
                tryAdmitInput: () => true,
                materializeOwned: (_, __) => throw new OutOfMemoryException("fatal copy"),
                transferOwned: _ => throw new InvalidOperationException("must not transfer"));

            Assert.True(binding.TryRegister().Succeeded);
            Assert.Throws<OutOfMemoryException>(() => backend.Invoke(new FakeMessage()));
            binding.Stop();

            Assert.Equal(1, backend.RemoveCount);
            Assert.Equal(1, backend.ReleaseCount);
        }

        [Fact]
        public void FatalOwnedClearEscapesOnlyAfterRemoveAndNodeRelease()
        {
            var backend = new FakeBackend();
            var binding = Binding(
                backend,
                tryAdmitInput: () => true,
                materializeOwned: (message, _) => new OwnedSample(message.Data),
                transferOwned: _ => { },
                clearOwned: () =>
                {
                    backend.Events.Add("clear");
                    throw new OutOfMemoryException("fatal clear");
                });

            Assert.True(binding.TryRegister().Succeeded);
            Assert.Throws<OutOfMemoryException>(() => binding.Stop());

            Assert.Equal(1, backend.RemoveCount);
            Assert.Equal(1, backend.ReleaseCount);
            Assert.Equal("remove,clear,release", string.Join(",", backend.Events));
        }

    }
}
#endif
