// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Ros2ForUnity
// Purpose: Lock the native bounded-stream callback and teardown ownership contract.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using System.Threading;
using System.Threading.Tasks;
using Unity.FoxgloveSDK.Components;
using Unity2Foxglove.Ros2ForUnity.Native;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Ros2ForUnity
{
    [Trait("Phase", "184-E")]
    [Trait("Domain", "Ros2StreamOwnership")]
    public sealed class FoxRunRos2StreamOwnershipTests
    {
        [Fact]
        public void AdmissionRunsBeforeMaterializationAndRejectedBorrowedInputIsNotDisposed()
        {
            var backend = new FakeBackend();
            var materialized = 0;
            var transferred = 0;
            var binding = Binding(
                backend,
                tryAdmitInput: () => false,
                materializeOwned: (message, context) =>
                {
                    materialized++;
                    return new OwnedSample(message.Data);
                },
                transferOwned: _ => transferred++);

            Assert.True(binding.TryRegister().Succeeded);
            backend.Invoke(new FakeMessage { Data = "borrowed" });

            Assert.Equal(0, materialized);
            Assert.Equal(0, transferred);
            binding.Stop();
        }

        [Fact]
        public void TransferCallMovesOwnershipEvenWhenTransferThrows()
        {
            var backend = new FakeBackend();
            var owned = new OwnedSample("owned");
            var binding = Binding(
                backend,
                tryAdmitInput: () => true,
                materializeOwned: (_, __) => owned,
                transferOwned: sample =>
                {
                    sample.DisposeCount++;
                    throw new InvalidOperationException("stream rejected after taking ownership");
                });

            Assert.True(binding.TryRegister().Succeeded);
            backend.Invoke(new FakeMessage());

            Assert.Equal(1, owned.DisposeCount);
            binding.Stop();
        }

        [Fact]
        public void MaterializerOwnsPartialCopyCleanupWhenItThrows()
        {
            var backend = new FakeBackend();
            var partial = new OwnedSample("partial");
            var binding = Binding(
                backend,
                tryAdmitInput: () => true,
                materializeOwned: (_, __) =>
                {
                    partial.DisposeCount++;
                    throw new InvalidOperationException("copy failed");
                },
                transferOwned: _ => throw new InvalidOperationException("must not transfer"));

            Assert.True(binding.TryRegister().Succeeded);
            backend.Invoke(new FakeMessage());

            Assert.Equal(1, partial.DisposeCount);
            binding.Stop();
        }

        [Fact]
        public void NullMaterializerResultIsRejectedBeforeOwnershipTransfer()
        {
            var backend = new FakeBackend();
            var transferred = 0;
            var binding = Binding(
                backend,
                tryAdmitInput: () => true,
                materializeOwned: (_, __) => null,
                transferOwned: _ => transferred++);

            Assert.True(binding.TryRegister().Succeeded);
            backend.Invoke(new FakeMessage());

            Assert.Equal(0, transferred);
            Assert.True(binding.TryGetSnapshot(7, out var snapshot));
            Assert.Equal(1, snapshot.CopyFailed);
            binding.Stop();
        }

        [Fact]
        public void BorrowedMaterializerResultIsRejectedBeforeOwnershipTransfer()
        {
            var backend = new FakeBackend();
            var transferred = 0;
            var binding = new FoxRunRos2StreamSubscriptionBinding<FakeMessage, FakeMessage>(
                Contract(),
                7,
                () => 7,
                1024,
                () => true,
                (message, _) => message,
                _ => transferred++,
                () => { },
                backend,
                FoxRunResolvedQos.Default,
                new ManagedQosFactory());

            Assert.True(binding.TryRegister().Succeeded);
            backend.Invoke(new FakeMessage());

            Assert.Equal(0, transferred);
            Assert.True(binding.TryGetSnapshot(7, out var snapshot));
            Assert.Equal(1, snapshot.CopyFailed);
            binding.Stop();
        }

        [Fact]
        public void StreamMaterializationReusesTheThreadLocalCopyContext()
        {
            var backend = new FakeBackend();
            FoxRunRos2CopyContext first = null;
            FoxRunRos2CopyContext second = null;
            var calls = 0;
            var binding = Binding(
                backend,
                tryAdmitInput: () => true,
                materializeOwned: (message, context) =>
                {
                    if (calls++ == 0)
                        first = context;
                    else
                        second = context;
                    return new OwnedSample(message.Data);
                },
                transferOwned: sample => sample.DisposeCount++);

            Assert.True(binding.TryRegister().Succeeded);
            backend.Invoke(new FakeMessage { Data = "first" });
            backend.Invoke(new FakeMessage { Data = "second" });

            Assert.NotNull(first);
            Assert.Same(first, second);
            binding.Stop();
        }

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
        public void StopStillClearsAndReleasesWhenNativeRemovalThrows()
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
            Assert.Equal(1, cleared);
            Assert.Equal(1, backend.ReleaseCount);
            Assert.Equal("remove,clear,release", string.Join(",", backend.Events));
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
                tryAdmitInput: () => true,
                materializeOwned: (message, _) =>
                {
                    materializeEntered.Set();
                    Assert.True(finishMaterialize.Wait(TimeSpan.FromSeconds(5)));
                    return new OwnedSample(message.Data);
                },
                transferOwned: owned => stream.TryEnqueueOwned(owned, throwingDisposer),
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
                tryAdmitInput: () => true,
                materializeOwned: (message, _) => new OwnedSample(message.Data),
                transferOwned: owned => stream.TryEnqueueOwned(
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
        public void RuntimeUnavailableRegistrationCanRetryBeforeTerminalCleanup()
        {
            var backend = new FakeBackend
            {
                RegistrationFailuresRemaining = 1,
                InvokeSynchronouslyOnRegister = true
            };
            var disposed = 0;
            var cleared = 0;
            var stream = new FoxRunStream<OwnedSample>();
            var binding = Binding(
                backend,
                tryAdmitInput: () => true,
                materializeOwned: (message, _) => new OwnedSample(message.Data),
                transferOwned: owned => stream.TryEnqueueOwned(
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

        private static FoxRunRos2StreamSubscriptionBinding<FakeMessage, OwnedSample> Binding(
            FakeBackend backend,
            Func<bool> tryAdmitInput,
            Func<FakeMessage, FoxRunRos2CopyContext, OwnedSample> materializeOwned,
            Action<OwnedSample> transferOwned,
            Action clearOwned = null)
            => new FoxRunRos2StreamSubscriptionBinding<FakeMessage, OwnedSample>(
                Contract(),
                7,
                () => 7,
                1024,
                tryAdmitInput,
                materializeOwned,
                transferOwned,
                clearOwned ?? (() => { }),
                backend,
                FoxRunResolvedQos.Default,
                new ManagedQosFactory());

        private static FoxRunRos2GeneratedContract Contract()
            => new FoxRunRos2GeneratedContract(
                "stream-contract",
                "/stream",
                "Demo.Receiver",
                "_stream",
                "std_msgs/msg/String",
                FoxRunFlow.Subscribe,
                FoxRunEndpoint.Ros2Native,
                FoxRunQosProfile.Default,
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

        private sealed class OwnedSample
        {
            public OwnedSample(string value) => Value = value;
            public string Value { get; }
            public int DisposeCount { get; set; }
        }

        private sealed class FakeMessage : ROS2.Message
        {
            public string Data { get; set; }
            public bool IsDisposed { get; private set; }
            public void Dispose() => IsDisposed = true;
        }

        private sealed class FakeBackend : IFoxRunRos2NativeBackend
        {
            private Action<FakeMessage> _callback;
            private Action<FakeMessage> _lateCallback;

            public System.Collections.Generic.List<string> Events { get; }
                = new System.Collections.Generic.List<string>();
            public int RemoveCount;
            public int ReleaseCount { get; private set; }
            public IFoxRunRos2NativeSubscriptionToken ReturnedToken { get; set; }
                = new FakeToken();
            public bool InvokeSynchronouslyOnRegister { get; set; }
            public int RegistrationFailuresRemaining { get; set; }
            public Exception RemoveException { get; set; }
            public ManualResetEventSlim RegisterEntered { get; set; }
            public ManualResetEventSlim ReleaseRegister { get; set; }

            public FoxRunRos2NativeBackendRegistration Register<T>(
                FoxRunRos2GeneratedContract contract,
                IFoxRunRos2NativeQosProfile qosProfile,
                Action<T> callback)
                where T : ROS2.Message, new()
            {
                _callback = message => callback((T)(ROS2.Message)message);
                _lateCallback = _callback;
                if (InvokeSynchronouslyOnRegister)
                    _callback(new FakeMessage { Data = "synchronous" });
                RegisterEntered?.Set();
                if (ReleaseRegister != null
                    && !ReleaseRegister.Wait(TimeSpan.FromSeconds(5)))
                {
                    throw new TimeoutException("Timed out waiting to release native registration.");
                }
                if (RegistrationFailuresRemaining > 0)
                {
                    RegistrationFailuresRemaining--;
                    return FoxRunRos2NativeBackendRegistration.Failure(
                        FoxRunRos2RegistrationError.RuntimeUnavailable,
                        "runtime unavailable");
                }
                return FoxRunRos2NativeBackendRegistration.Success(ReturnedToken);
            }

            public void RemoveSubscription(IFoxRunRos2NativeSubscriptionToken token)
            {
                Interlocked.Increment(ref RemoveCount);
                Events.Add("remove");
                _callback = null;
                if (RemoveException != null)
                    throw RemoveException;
            }

            public void ReleaseNodeOwnership()
            {
                ReleaseCount++;
                Events.Add("release");
            }

            public void Invoke(FakeMessage message) => _callback(message);
            public void InvokeLate(FakeMessage message) => _lateCallback(message);
        }

        private sealed class FakeToken : IFoxRunRos2NativeSubscriptionToken
        {
            private readonly bool _isUsable;
            private readonly bool _throwOnInspection;

            public FakeToken(bool isUsable = true, bool throwOnInspection = false)
            {
                _isUsable = isUsable;
                _throwOnInspection = throwOnInspection;
            }

            public bool IsUsable
                => _throwOnInspection
                    ? throw new InvalidOperationException("token inspection failed")
                    : _isUsable;
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
    }
}
#endif
