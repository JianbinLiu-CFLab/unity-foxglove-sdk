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
    [Trait("Phase", "184-E")]
    [Trait("Domain", "Ros2StreamOwnership")]
    public sealed partial class FoxRunRos2StreamOwnershipTests
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
        public void MaterializerFailureReturnsAdmissionCreditToTheStream()
        {
            var backend = new FakeBackend();
            var disposed = new System.Collections.Generic.List<int>();
            using var stream = new FoxRunStream<int>(
                new FoxRunStreamOptions(2, 1d, 2),
                () => 0L,
                1L);
            var binding = Binding(
                backend,
                tryAdmitInput: stream.TryAdmitInput,
                materializeOwned: (_, __) => throw new InvalidOperationException("copy failed"),
                transferOwned: _ => throw new InvalidOperationException("must not transfer"),
                cancelAdmissionCredit: stream.CancelAdmissionCredit);

            Assert.True(binding.TryRegister().Succeeded);
            backend.Invoke(new FakeMessage());

            Assert.False(stream.TryEnqueueOwned(1, disposed.Add));
            Assert.Equal(new[] { 1 }, disposed);
            Assert.Equal(1, stream.Stats.RateDropped);
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
                action => action(),
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
    }
}
#endif
