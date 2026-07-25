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
    public sealed class FoxRunStreamTests
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
            Assert.False(stream.TryEnqueueOwned(2, disposed.Add));

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
            using var stream = new FoxRunStream<string>(
                new FoxRunStreamOptions(1, 1000d, 1, FoxRunStreamOverflowPolicy.DropOldest));

            Assert.True(stream.TryEnqueueDeferredOwned(
                1,
                state =>
                {
                    materialized++;
                    return state.ToString();
                },
                disposed.Add,
                static _ => { }));
            Assert.True(stream.TryEnqueueDeferredOwned(
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

        [Fact]
        public void DeferredMaterializationFailureDisposesStateAndPropagatesFromConsumer()
        {
            var disposedState = 0;
            using var stream = new FoxRunStream<int>();
            Assert.True(stream.TryEnqueueDeferredOwned(
                7,
                _ => throw new InvalidOperationException("deferred-map"),
                _ => disposedState++,
                static _ => { }));

            var failure = Assert.Throws<InvalidOperationException>(() => stream.Drain(_ => { }));

            Assert.Equal("deferred-map", failure.Message);
            Assert.Equal(1, disposedState);
            Assert.Equal(0, stream.Count);
            Assert.Equal(0, stream.Stats.Drained);
        }

        [Fact]
        public void DrainHonorsMaximumBatchAndDisposesCurrentOnCallbackFailure()
        {
            var disposed = new List<int>();
            using var stream = CreateDeterministicallyAdmittedStream<int>(
                new FoxRunStreamOptions(4, 1d, 2, FoxRunStreamOverflowPolicy.DropOldest));
            AdmitAndEnqueue(stream, 1, disposed.Add);
            AdmitAndEnqueue(stream, 2, disposed.Add);
            AdmitAndEnqueue(stream, 3, disposed.Add);

            var error = Assert.Throws<InvalidOperationException>(
                () => stream.Drain(value =>
                {
                    Assert.Equal(1, value);
                    throw new InvalidOperationException("consumer");
                }));
            Assert.Equal("consumer", error.Message);
            Assert.Equal(new[] { 1 }, disposed);
            Assert.Equal(2, stream.Count);
            Assert.Equal(0, stream.Stats.Drained);

            var drained = new List<int>();
            Assert.Equal(2, stream.Drain(drained.Add));
            Assert.Equal(new[] { 2, 3 }, drained);
            Assert.Equal(new[] { 1, 2, 3 }, disposed);
            Assert.Equal(2, stream.Stats.Drained);
        }

        [Fact]
        public void TakeLatestDisposesOlderAndLeaseDisposalIsIdempotent()
        {
            var disposeCounts = new ConcurrentDictionary<int, int>();
            using var stream = CreateDeterministicallyAdmittedStream<int>(
                new FoxRunStreamOptions(4, 1d, 4, FoxRunStreamOverflowPolicy.DropOldest));
            for (var value = 1; value <= 3; value++)
                AdmitAndEnqueue(stream, value, item => disposeCounts.AddOrUpdate(item, 1, (_, count) => count + 1));

            Assert.True(stream.TryTakeLatest(out var latest));
            Assert.Equal(3, latest.Value);
            Assert.Equal(0, stream.Count);
            Assert.Equal(2, stream.Stats.Cleared);
            Assert.Equal(1, stream.Stats.Taken);
            latest.Dispose();
            latest.Dispose();

            Assert.Equal(1, disposeCounts[1]);
            Assert.Equal(1, disposeCounts[2]);
            Assert.Equal(1, disposeCounts[3]);
        }

        [Fact]
        public void ClearAndDisposeContinueAfterDisposerFailures()
        {
            var attempts = new List<int>();
            var stream = CreateDeterministicallyAdmittedStream<int>(
                new FoxRunStreamOptions(4, 1d, 4, FoxRunStreamOverflowPolicy.DropOldest));
            for (var value = 1; value <= 3; value++)
            {
                var captured = value;
                AdmitAndEnqueue(stream, captured, item =>
                {
                    attempts.Add(item);
                    if (item != 2)
                        throw new InvalidOperationException("dispose-" + item);
                });
            }

            Assert.Equal(3, stream.Clear());
            Assert.Equal(new[] { 1, 2, 3 }, attempts);
            Assert.Equal(3, stream.Stats.Cleared);
            Assert.Equal(2, stream.Stats.DisposalFailures);
            Assert.False(string.IsNullOrWhiteSpace(stream.Stats.LastDisposalError));
            Assert.True(stream.Stats.LastDisposalError.Length <= 512);
            stream.Dispose();
        }

        [Fact]
        public void DisposalDiagnosticIsLengthBounded()
        {
            using var stream = new FoxRunStream<int>();
            stream.TryEnqueueOwned(
                1,
                _ => throw new InvalidOperationException(new string('x', 4096)));

            stream.Clear();

            Assert.Equal(1, stream.Stats.DisposalFailures);
            Assert.Equal(512, stream.Stats.LastDisposalError.Length);
        }

        [Fact]
        public void DisposedStreamRejectsAndDisposesProducerOwnership()
        {
            var disposed = 0;
            var stream = new FoxRunStream<int>();
            stream.Dispose();

            Assert.False(stream.TryAdmitInput());
            Assert.False(stream.TryEnqueueOwned(7, _ => disposed++));
            Assert.Equal(1, disposed);
            Assert.True(stream.IsDisposed);
            Assert.Equal(0, stream.Count);
        }

        [Fact]
        public async Task ConcurrentProducerConsumerDisposesEveryOwnedValueExactlyOnce()
        {
            const int producerCount = 4;
            const int perProducer = 2000;
            var disposeCounts = new ConcurrentDictionary<int, int>();
            using var stream = new FoxRunStream<int>(
                new FoxRunStreamOptions(32, 1_000_000d, 17, FoxRunStreamOverflowPolicy.DropOldest));
            using var start = new ManualResetEventSlim();
            var producers = Enumerable.Range(0, producerCount).Select(producer => Task.Run(() =>
            {
                start.Wait();
                for (var index = 0; index < perProducer; index++)
                {
                    var value = producer * perProducer + index;
                    stream.TryEnqueueOwned(
                        value,
                        item => disposeCounts.AddOrUpdate(item, 1, (_, count) => count + 1));
                }
            })).ToArray();
            var consumer = Task.Run(() =>
            {
                start.Wait();
                while (producers.Any(task => !task.IsCompleted) || stream.Count != 0)
                    stream.Drain(_ => { });
            });

            start.Set();
            await Task.WhenAll(producers);
            await consumer;
            stream.Clear();

            Assert.Equal(producerCount * perProducer, disposeCounts.Count);
            Assert.All(disposeCounts, pair => Assert.Equal(1, pair.Value));
            Assert.Equal(0, stream.Count);
        }

        [Fact]
        public void EveryLongCounterSaturatesInsteadOfWrapping()
        {
            using var stream = new FoxRunStream<int>(
                new FoxRunStreamOptions(1, 1000d, 1, FoxRunStreamOverflowPolicy.DropOldest));
            foreach (var fieldName in new[]
                     {
                         "_received", "_admitted", "_drained", "_taken", "_droppedOldest",
                         "_droppedNewest", "_rateDropped", "_cleared", "_highWater",
                         "_disposalFailures"
                     })
                SetCounter(stream, fieldName, long.MaxValue);

            stream.TryAdmitInput();
            stream.TryEnqueueOwned(1, _ => throw new InvalidOperationException("bounded"));
            stream.TryEnqueueOwned(2, _ => throw new InvalidOperationException("bounded"));
            stream.Drain(_ => { });
            stream.Clear();

            var stats = stream.Stats;
            Assert.Equal(long.MaxValue, stats.Received);
            Assert.Equal(long.MaxValue, stats.Admitted);
            Assert.Equal(long.MaxValue, stats.Drained);
            Assert.Equal(long.MaxValue, stats.Taken);
            Assert.Equal(long.MaxValue, stats.DroppedOldest);
            Assert.Equal(long.MaxValue, stats.DroppedNewest);
            Assert.Equal(long.MaxValue, stats.RateDropped);
            Assert.Equal(long.MaxValue, stats.Cleared);
            Assert.Equal(long.MaxValue, stats.HighWater);
            Assert.Equal(long.MaxValue, stats.DisposalFailures);
        }

        private static void AdmitAndEnqueue<T>(
            FoxRunStream<T> stream,
            T value,
            Action<T> disposer)
        {
            Assert.True(stream.TryAdmitInput());
            Assert.True(stream.TryEnqueueOwned(value, disposer));
        }

        private static FoxRunStream<T> CreateDeterministicallyAdmittedStream<T>(
            FoxRunStreamOptions options)
        {
            long timestamp = 0;
            return new FoxRunStream<T>(
                options,
                () => Interlocked.Increment(ref timestamp),
                1L);
        }

        private static void SetCounter<T>(FoxRunStream<T> stream, string name, long value)
        {
            var field = typeof(FoxRunStream<T>).GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            field.SetValue(stream, value);
        }
    }
}
