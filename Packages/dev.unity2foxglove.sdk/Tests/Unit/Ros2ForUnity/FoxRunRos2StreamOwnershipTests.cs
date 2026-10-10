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

    }
}
#endif
