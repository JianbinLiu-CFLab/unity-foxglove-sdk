// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/FoxRun
// Purpose: Lock the bounded FoxRun stream ownership and accounting contract.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Unity.FoxgloveSDK.Components;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.FoxRun
{
    [Trait("Phase", "184-E")]
    [Trait("Domain", "FoxRunStream")]
    public sealed partial class FoxRunStreamTests
    {
        [Fact]
        public void DefaultsAreFiniteAndBounded()
        {
            var options = new FoxRunStreamOptions();

            Assert.Equal(1024, options.Capacity);
            Assert.Equal(1000d, options.MaxInputHz);
            Assert.Equal(128, options.MaxBatch);
            Assert.Equal(FoxRunStreamOverflowPolicy.DropOldest, options.Overflow);
            Assert.DoesNotContain(
                typeof(FoxRunStreamOverflowPolicy).GetEnumNames(),
                name => name.Contains("Block", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("Latest", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("Unbounded", StringComparison.OrdinalIgnoreCase));
            Assert.Null(typeof(FoxRunStream<int>).GetProperty("Latest"));
        }

        [Theory]
        [InlineData(0, 1d, 1)]
        [InlineData(-1, 1d, 1)]
        [InlineData(1, 0d, 1)]
        [InlineData(1, -1d, 1)]
        [InlineData(1, double.NaN, 1)]
        [InlineData(1, double.PositiveInfinity, 1)]
        [InlineData(1, 1d, 0)]
        [InlineData(1, 1d, -1)]
        public void InvalidOptionsFailClosed(int capacity, double maxInputHz, int maxBatch)
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new FoxRunStreamOptions(capacity, maxInputHz, maxBatch));
        }

        [Fact]
        public void UndefinedOverflowPolicyFailsClosed()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new FoxRunStreamOptions(
                    1,
                    1d,
                    1,
                    (FoxRunStreamOverflowPolicy)0));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new FoxRunStreamOptions(
                    1,
                    1d,
                    1,
                    (FoxRunStreamOverflowPolicy)3));
        }

        [Fact]
        public void StreamOwnsDropOldestAndCountersRemainMonotonic()
        {
            var disposed = new List<int>();
            using var stream = CreateDeterministicallyAdmittedStream<int>(
                new FoxRunStreamOptions(2, 1d, 2, FoxRunStreamOverflowPolicy.DropOldest));

            AdmitAndEnqueue(stream, 1, disposed.Add);
            AdmitAndEnqueue(stream, 2, disposed.Add);
            AdmitAndEnqueue(stream, 3, disposed.Add);

            Assert.Equal(new[] { 1 }, disposed);
            Assert.Equal(2, stream.Count);
            Assert.Equal(3, stream.Stats.Received);
            Assert.Equal(3, stream.Stats.Admitted);
            Assert.Equal(1, stream.Stats.DroppedOldest);
            Assert.Equal(0, stream.Stats.DroppedNewest);
            Assert.Equal(2, stream.Stats.HighWater);

            var drained = new List<int>();
            Assert.Equal(2, stream.Drain(drained.Add));
            Assert.Equal(new[] { 2, 3 }, drained);
            Assert.Equal(new[] { 1, 2, 3 }, disposed);
            Assert.Equal(2, stream.Stats.Drained);
        }

        [Fact]
        public void StreamOwnsRejectedDropNewest()
        {
            var disposed = new List<int>();
            using var stream = CreateDeterministicallyAdmittedStream<int>(
                new FoxRunStreamOptions(1, 1d, 1, FoxRunStreamOverflowPolicy.DropNewest));

            AdmitAndEnqueue(stream, 1, disposed.Add);
            Assert.True(stream.TryAdmitInput());
            Assert.False(stream.TryEnqueueOwnedAfterAdmission(2, disposed.Add));

            Assert.Equal(new[] { 2 }, disposed);
            Assert.Equal(1, stream.Count);
            Assert.Equal(1, stream.Stats.DroppedNewest);
            Assert.True(stream.TryTake(out var sample));
            Assert.Equal(1, sample.Value);
            sample.Dispose();
            Assert.Equal(new[] { 2, 1 }, disposed);
            Assert.Equal(1, stream.Stats.Taken);
        }

        [Fact]
        public void AdmissionGateRejectsBeforeMaterializationBudgetIsSpent()
        {
            using var stream = new FoxRunStream<int>(
                new FoxRunStreamOptions(2, 1d, 1, FoxRunStreamOverflowPolicy.DropOldest),
                () => 0L,
                1L);

            Assert.True(stream.TryAdmitInput());
            Assert.False(stream.TryAdmitInput());
            Assert.Equal(2, stream.Stats.Received);
            Assert.Equal(1, stream.Stats.Admitted);
            Assert.Equal(1, stream.Stats.RateDropped);
        }

        [Fact]
        public void PublicOwnedEnqueueHonorsInputRateAdmission()
        {
            var disposed = new List<int>();
            using var stream = new FoxRunStream<int>(
                new FoxRunStreamOptions(2, 1d, 2),
                () => 0L,
                1L);

            Assert.True(stream.TryEnqueueOwned(1, disposed.Add));
            Assert.False(stream.TryEnqueueOwned(2, disposed.Add));
            Assert.Equal(new[] { 2 }, disposed);
            Assert.Equal(1, stream.Stats.Admitted);
            Assert.Equal(1, stream.Stats.RateDropped);
        }

        [Fact]
        public void PublicOwnedEnqueueConsumesPreAdmittedCredit()
        {
            var disposed = new List<int>();
            using var stream = new FoxRunStream<int>(
                new FoxRunStreamOptions(2, 1d, 2),
                () => 0L,
                1L);

            Assert.True(stream.TryAdmitInput());
            Assert.True(stream.TryEnqueueOwned(1, disposed.Add));

            Assert.Empty(disposed);
            Assert.Equal(1, stream.Count);
            Assert.Equal(1, stream.Stats.Admitted);
            Assert.Equal(0, stream.Stats.RateDropped);
        }

        [Fact]
        public void PublicDeferredEnqueueConsumesPreAdmittedCredit()
        {
            var disposed = new List<int>();
            using var stream = new FoxRunStream<int>(
                new FoxRunStreamOptions(2, 1d, 2),
                () => 0L,
                1L);

            Assert.True(stream.TryAdmitInput());
            Assert.True(stream.TryEnqueueDeferredOwned(
                1,
                static value => value,
                disposed.Add,
                static _ => { }));

            Assert.Empty(disposed);
            Assert.Equal(1, stream.Count);
            Assert.Equal(1, stream.Stats.Admitted);
            Assert.Equal(0, stream.Stats.RateDropped);
        }

        [Fact]
        public void PublicDeferredEnqueueAdmitsExactlyOnceWhenCallerHasNoCredit()
        {
            var disposed = new List<int>();
            using var stream = new FoxRunStream<int>(
                new FoxRunStreamOptions(2, 1d, 2),
                () => 0L,
                1L);

            Assert.True(stream.TryEnqueueDeferredOwned(
                1,
                static value => value,
                disposed.Add,
                static _ => { }));
            Assert.False(stream.TryEnqueueDeferredOwned(
                2,
                static value => value,
                disposed.Add,
                static _ => { }));

            Assert.Equal(new[] { 2 }, disposed);
            Assert.Equal(1, stream.Count);
            Assert.Equal(1, stream.Stats.Admitted);
            Assert.Equal(1, stream.Stats.RateDropped);
        }

        [Fact]
        public void AdmissionCreditCannotBeConsumedByAnotherProducerThread()
        {
            var disposed = new ConcurrentQueue<int>();
            using var stream = new FoxRunStream<int>(
                new FoxRunStreamOptions(2, 1d, 2),
                () => 0L,
                1L);
            using var admitted = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var producerAId = 0;
            var producerBId = 0;
            var producerAAdmitted = false;
            var producerAResult = false;
            var producerBResult = true;

            var producerA = new Thread(() =>
            {
                producerAId = Environment.CurrentManagedThreadId;
                producerAAdmitted = stream.TryAdmitInput();
                admitted.Set();
                release.Wait();
                producerAResult = stream.TryEnqueueOwned(1, disposed.Enqueue);
            });
            var producerB = new Thread(() =>
            {
                producerBId = Environment.CurrentManagedThreadId;
                admitted.Wait();
                producerBResult = stream.TryEnqueueOwned(2, disposed.Enqueue);
            });

            producerA.Start();
            producerB.Start();
            producerB.Join();
            release.Set();
            producerA.Join();

            Assert.NotEqual(producerAId, producerBId);
            Assert.True(producerAAdmitted);
            Assert.False(producerBResult);
            Assert.True(producerAResult);
            Assert.Equal(new[] { 2 }, disposed.ToArray());
            Assert.Equal(1, stream.Count);
        }

        [Fact]
        public void CancelledAdmissionCreditCannotBeReusedByAnotherEnqueue()
        {
            var disposed = new List<int>();
            using var stream = new FoxRunStream<int>(
                new FoxRunStreamOptions(2, 1d, 2),
                () => 0L,
                1L);

            Assert.True(stream.TryAdmitInput());
            Assert.True(stream.CancelAdmissionCredit());
            Assert.False(stream.TryEnqueueOwned(1, disposed.Add));

            Assert.Equal(new[] { 1 }, disposed);
            Assert.Equal(1, stream.Stats.RateDropped);
        }

        [Fact]
        public void GeneratedAfterAdmissionSeamsRequireAnAdmissionCredit()
        {
            var disposed = new List<int>();
            using var stream = new FoxRunStream<int>(
                new FoxRunStreamOptions(2, 1d, 1),
                () => 0L,
                1L);

            Assert.False(stream.TryEnqueueOwnedAfterAdmission(1, disposed.Add));
            Assert.Equal(new[] { 1 }, disposed);

            Assert.False(stream.TryEnqueueDeferredOwnedAfterAdmission(
                2,
                static value => value,
                disposed.Add,
                static _ => { }));
            Assert.Equal(new[] { 1, 2 }, disposed);
        }

        [Fact]
        public void ExtremelySmallFiniteRateCannotOverflowIntoAnUnlimitedAdmissionGate()
        {
            long timestamp = 0;
            using var stream = new FoxRunStream<int>(
                new FoxRunStreamOptions(2, 1e-20d, 1),
                () => Volatile.Read(ref timestamp),
                1L);

            Assert.True(stream.TryAdmitInput());
            Volatile.Write(ref timestamp, 1L);
            Assert.False(stream.TryAdmitInput());
            Assert.Equal(1, stream.Stats.RateDropped);
        }

        [Fact]
        public async Task DeferredOwnedStateMaterializesOnlyOnTheConsumerThread()
        {
            var producerThread = Environment.CurrentManagedThreadId;
            var materializerThread = 0;
            var disposedState = 0;
            var disposedValue = 0;
            using var stream = new FoxRunStream<string>();

            Assert.True(stream.TryEnqueueDeferredOwned(
                184,
                state =>
                {
                    materializerThread = Environment.CurrentManagedThreadId;
                    return "phase-" + state;
                },
                _ => disposedState++,
                _ => disposedValue++));
            Assert.Equal(0, materializerThread);
            Assert.Equal(0, disposedState);

            var consumer = await Task.Run(() =>
            {
                Assert.Equal(1, stream.Drain(value => Assert.Equal("phase-184", value)));
                return Environment.CurrentManagedThreadId;
            });

            Assert.NotEqual(producerThread, consumer);
            Assert.Equal(consumer, materializerThread);
            Assert.Equal(1, disposedState);
            Assert.Equal(1, disposedValue);
        }

        [Fact]
        public void DeferredOwnedStateIsDisposedWithoutMaterializationWhenDroppedOrCleared()
        {
            var materialized = 0;
            var disposed = new List<int>();
            using var stream = CreateDeterministicallyAdmittedStream<string>(
                new FoxRunStreamOptions(1, 1d, 1, FoxRunStreamOverflowPolicy.DropOldest));

            Assert.True(stream.TryAdmitInput());
            Assert.True(stream.TryEnqueueDeferredOwnedAfterAdmission(
                1,
                state =>
                {
                    materialized++;
                    return state.ToString();
                },
                disposed.Add,
                static _ => { }));
            Assert.True(stream.TryAdmitInput());
            Assert.True(stream.TryEnqueueDeferredOwnedAfterAdmission(
                2,
                state =>
                {
                    materialized++;
                    return state.ToString();
                },
                disposed.Add,
                static _ => { }));

            Assert.Equal(new[] { 1 }, disposed);
            Assert.Equal(0, materialized);
            Assert.Equal(1, stream.Clear());
            Assert.Equal(new[] { 1, 2 }, disposed);
            Assert.Equal(0, materialized);
        }

    }
}
