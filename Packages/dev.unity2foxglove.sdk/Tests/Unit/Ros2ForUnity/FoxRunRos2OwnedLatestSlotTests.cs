// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Ros2ForUnity
// Purpose: Verify the capacity-one native callback ownership mailbox.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Unity2Foxglove.Ros2ForUnity.Native;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Ros2ForUnity
{
    [Trait("Phase", "179-C")]
    [Trait("Domain", "Ros2Ownership")]
    public sealed partial class FoxRunRos2OwnedLatestSlotTests
    {
        [Fact]
        public void PendingAndAppliedValuesHaveDistinctOwningThreads()
        {
            var disposeThreads = new ConcurrentDictionary<int, int>();
            var slot = new FoxRunRos2OwnedLatestSlot<OwnedProbe>(probe =>
            {
                probe.Dispose();
                disposeThreads[probe.Value] = Environment.CurrentManagedThreadId;
            });
            var mainThread = Environment.CurrentManagedThreadId;
            var producerThread = 0;

            RunOnThread(() =>
            {
                producerThread = Environment.CurrentManagedThreadId;
                Assert.True(slot.TryPublish(() => new OwnedProbe(1)));
                Assert.True(slot.TryPublish(() => new OwnedProbe(2)));
            });

            OwnedProbe applied = null;
            Assert.True(slot.TryApplyLatest(value => applied = value, value => ReferenceEquals(applied, value)));
            Assert.Equal(2, applied.Value);

            RunOnThread(() => Assert.True(slot.TryPublish(() => new OwnedProbe(3))));
            Assert.True(slot.TryApplyLatest(value => applied = value, value => ReferenceEquals(applied, value)));
            slot.Stop(value =>
            {
                if (!ReferenceEquals(applied, value))
                    return false;
                applied = null;
                return true;
            });

            Assert.Equal(producerThread, disposeThreads[1]);
            Assert.Equal(mainThread, disposeThreads[2]);
            Assert.Equal(mainThread, disposeThreads[3]);
            Assert.Equal(3, slot.ReceivedCount);
            Assert.Equal(1, slot.ReplacedCount);
            Assert.Equal(2, slot.AppliedCount);
            Assert.Equal(0, slot.RejectedAfterStopCount);
            Assert.Equal(0, slot.CopyFailedCount);
        }

        [Fact]
        public void ProducerConsumerBurstNeverBuildsMoreThanOnePendingValue()
        {
            const int count = 20000;
            var created = new ConcurrentBag<OwnedProbe>();
            var samples = new ConcurrentBag<CounterSnapshot>();
            var slot = new FoxRunRos2OwnedLatestSlot<OwnedProbe>(probe => probe.Dispose());
            Exception producerFailure = null;
            var producer = new Thread(() =>
            {
                try
                {
                    for (var value = 1; value <= count; value++)
                    {
                        var captured = value;
                        Assert.True(slot.TryPublish(() =>
                        {
                            var probe = new OwnedProbe(captured);
                            created.Add(probe);
                            return probe;
                        }));
                        Assert.InRange(slot.PendingCount, 0, 1);
                    }
                }
                catch (Exception exception)
                {
                    producerFailure = exception;
                }
            })
            {
                IsBackground = true,
            };
            producer.Start();

            var appliedValues = new List<int>();
            var deadlineUtc = DateTime.UtcNow.AddSeconds(10);
            while (producer.IsAlive || slot.PendingCount != 0)
            {
                Assert.True(
                    DateTime.UtcNow < deadlineUtc,
                    "Producer/consumer burst did not drain within ten seconds.");
                slot.TryApplyLatest(
                    probe => appliedValues.Add(probe.Value),
                    _ => false);
                samples.Add(CounterSnapshot.Read(slot));
                Thread.Yield();
            }
            producer.Join();
            if (producerFailure != null)
                throw producerFailure;
            slot.Stop(_ => false);

            Assert.Equal(count, slot.ReceivedCount);
            Assert.Equal(count, created.Count);
            Assert.NotEmpty(appliedValues);
            Assert.Equal(count, appliedValues[^1]);
            Assert.Equal(count, slot.ReplacedCount + slot.AppliedCount);
            Assert.All(created, probe => Assert.Equal(1, probe.DisposeCount));
            AssertMonotonic(samples);
        }

        [Fact]
        public void ReplacedPendingAndPriorAppliedCanBeDisposedConcurrentlyWithoutLosingLatest()
        {
            using var pendingDisposeEntered = new ManualResetEventSlim();
            using var appliedDisposeEntered = new ManualResetEventSlim();
            using var releaseDisposers = new ManualResetEventSlim();
            var disposalCounts = new ConcurrentDictionary<int, int>();
            var snapshots = new ConcurrentQueue<CounterSnapshot>();
            var slot = new FoxRunRos2OwnedLatestSlot<OwnedProbe>(probe =>
            {
                if (probe.Value == 2)
                {
                    pendingDisposeEntered.Set();
                    Assert.True(releaseDisposers.Wait(TimeSpan.FromSeconds(10)));
                }
                else if (probe.Value == 1)
                {
                    appliedDisposeEntered.Set();
                    Assert.True(releaseDisposers.Wait(TimeSpan.FromSeconds(10)));
                }
                disposalCounts.AddOrUpdate(probe.Value, 1, (_, count) => count + 1);
                probe.Dispose();
            });
            OwnedProbe applied = null;

            Assert.True(slot.TryPublish(() => new OwnedProbe(1)));
            Assert.True(slot.TryApplyLatest(value => applied = value, value => ReferenceEquals(applied, value)));
            Assert.True(slot.TryPublish(() => new OwnedProbe(2)));
            snapshots.Enqueue(CounterSnapshot.Read(slot));

            Exception producerFailure = null;
            Exception consumerFailure = null;
            var producerResult = false;
            var consumerResult = false;
            var producer = new Thread(() =>
            {
                try
                {
                    producerResult = slot.TryPublish(() => new OwnedProbe(3));
                }
                catch (Exception exception)
                {
                    producerFailure = exception;
                }
            });
            producer.Start();
            Assert.True(pendingDisposeEntered.Wait(TimeSpan.FromSeconds(10)));
            snapshots.Enqueue(CounterSnapshot.Read(slot));

            var consumer = new Thread(() =>
            {
                try
                {
                    consumerResult = slot.TryApplyLatest(
                        value => applied = value,
                        value => ReferenceEquals(applied, value));
                }
                catch (Exception exception)
                {
                    consumerFailure = exception;
                }
            });
            consumer.Start();
            Assert.True(appliedDisposeEntered.Wait(TimeSpan.FromSeconds(10)));
            snapshots.Enqueue(CounterSnapshot.Read(slot));
            releaseDisposers.Set();

            producer.Join();
            consumer.Join();
            if (producerFailure != null)
                throw producerFailure;
            if (consumerFailure != null)
                throw consumerFailure;
            Assert.True(producerResult);
            Assert.True(consumerResult);
            snapshots.Enqueue(CounterSnapshot.Read(slot));
            Assert.Equal(3, applied.Value);
            slot.Stop(value =>
            {
                if (!ReferenceEquals(applied, value))
                    return false;
                applied = null;
                return true;
            });

            Assert.Equal(new[] { 1, 2, 3 }, disposalCounts.Keys.OrderBy(value => value));
            Assert.All(disposalCounts.Values, count => Assert.Equal(1, count));
            Assert.Equal(3, slot.ReceivedCount);
            Assert.Equal(1, slot.ReplacedCount);
            Assert.Equal(2, slot.AppliedCount);
            AssertMonotonic(snapshots);
        }

        [Fact]
        public void ApplyFailureClearsAndDisposesCandidateWithoutReplacingPriorApplied()
        {
            var first = new OwnedProbe(1);
            var failing = new OwnedProbe(2);
            OwnedProbe target = null;
            var slot = new FoxRunRos2OwnedLatestSlot<OwnedProbe>(probe => probe.Dispose());
            Assert.True(slot.TryPublish(() => first));
            Assert.True(slot.TryApplyLatest(value => target = value, value => ReferenceEquals(target, value)));
            Assert.True(slot.TryPublish(() => failing));

            Assert.Throws<InvalidOperationException>(() => slot.TryApplyLatest(
                value =>
                {
                    target = value;
                    throw new InvalidOperationException("setter failed");
                },
                value =>
                {
                    if (!ReferenceEquals(target, value))
                        return false;
                    target = null;
                    return true;
                }));

            Assert.Equal(1, failing.DisposeCount);
            Assert.Equal(0, first.DisposeCount);
            Assert.Equal(1, slot.AppliedCount);
            slot.Stop(_ => false);
            Assert.Equal(1, first.DisposeCount);
        }

        [Fact]
        public void StopDisposesPendingOrAppliedExactlyOnceAndRejectsLateCallbacks()
        {
            var pending = new OwnedProbe(1);
            var pendingSlot = new FoxRunRos2OwnedLatestSlot<OwnedProbe>(probe => probe.Dispose());
            Assert.True(pendingSlot.TryPublish(() => pending));
            pendingSlot.Stop(_ => false);
            pendingSlot.Stop(_ => false);
            Assert.Equal(1, pending.DisposeCount);
            Assert.False(pendingSlot.TryPublish(() => throw new InvalidOperationException("must not copy")));
            Assert.Equal(1, pendingSlot.RejectedAfterStopCount);
            Assert.Equal(0, pendingSlot.CopyFailedCount);

            var applied = new OwnedProbe(2);
            OwnedProbe target = null;
            var appliedSlot = new FoxRunRos2OwnedLatestSlot<OwnedProbe>(probe => probe.Dispose());
            Assert.True(appliedSlot.TryPublish(() => applied));
            Assert.True(appliedSlot.TryApplyLatest(value => target = value, value => ReferenceEquals(target, value)));
            appliedSlot.Stop(value =>
            {
                if (!ReferenceEquals(target, value))
                    return false;
                target = null;
                return true;
            });
            appliedSlot.Stop(_ => false);

            Assert.Null(target);
            Assert.Equal(1, applied.DisposeCount);
        }

        [Fact]
        public void StopWaitsForInFlightApplyThenClearsAndDisposesOnStopCaller()
        {
            using var applyEntered = new ManualResetEventSlim();
            using var releaseApply = new ManualResetEventSlim();
            var mainThread = Environment.CurrentManagedThreadId;
            var applyThread = 0;
            var clearThread = 0;
            var disposeThread = 0;
            Exception applierFailure = null;
            Exception coordinatorFailure = null;
            OwnedProbe target = null;
            var owned = new OwnedProbe(1);
            var slot = new FoxRunRos2OwnedLatestSlot<OwnedProbe>(probe =>
            {
                disposeThread = Environment.CurrentManagedThreadId;
                probe.Dispose();
            });
            Assert.True(slot.TryPublish(() => owned));

            var applier = new Thread(() =>
            {
                try
                {
                    slot.TryApplyLatest(value =>
                    {
                        applyThread = Environment.CurrentManagedThreadId;
                        applyEntered.Set();
                        Assert.True(releaseApply.Wait(TimeSpan.FromSeconds(10)));
                        target = value;
                    }, value => ReferenceEquals(target, value));
                }
                catch (Exception exception)
                {
                    applierFailure = exception;
                }
            });
            applier.Start();
            Assert.True(applyEntered.Wait(TimeSpan.FromSeconds(10)));

            var coordinator = new Thread(() =>
            {
                try
                {
                    Assert.True(SpinWait.SpinUntil(() => slot.IsStopping, TimeSpan.FromSeconds(10)));
                    releaseApply.Set();
                }
                catch (Exception exception)
                {
                    coordinatorFailure = exception;
                    releaseApply.Set();
                }
            });
            coordinator.Start();

            slot.Stop(value =>
            {
                clearThread = Environment.CurrentManagedThreadId;
                if (!ReferenceEquals(target, value))
                    return false;
                target = null;
                return true;
            });
            applier.Join();
            coordinator.Join();
            if (applierFailure != null)
                throw applierFailure;
            if (coordinatorFailure != null)
                throw coordinatorFailure;

            Assert.Null(target);
            Assert.Equal(1, owned.DisposeCount);
            Assert.Equal(applier.ManagedThreadId, applyThread);
            Assert.Equal(mainThread, clearThread);
            Assert.Equal(mainThread, disposeThread);
        }
    }
}
#endif
