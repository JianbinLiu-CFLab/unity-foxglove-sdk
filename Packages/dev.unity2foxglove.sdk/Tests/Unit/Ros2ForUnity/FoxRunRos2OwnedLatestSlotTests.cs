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
    public sealed class FoxRunRos2OwnedLatestSlotTests
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
            });
            producer.Start();

            var appliedValues = new List<int>();
            while (producer.IsAlive || slot.PendingCount != 0)
            {
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
        public void StopClearsAndDisposesSharedPendingAppliedReferenceExactlyOnce()
        {
            var owned = new OwnedProbe(1);
            OwnedProbe target = null;
            var slot = new FoxRunRos2OwnedLatestSlot<OwnedProbe>(probe => probe.Dispose());
            Assert.True(slot.TryPublish(() => owned));
            Assert.True(slot.TryApplyLatest(value => target = value, value => ReferenceEquals(target, value)));
            Assert.True(slot.TryPublish(() => owned));

            slot.Stop(value =>
            {
                if (!ReferenceEquals(target, value))
                    return false;
                target = null;
                return true;
            });

            Assert.Null(target);
            Assert.Equal(1, owned.DisposeCount);
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

        [Fact]
        public void ConcurrentStopCallsDrainSharedPendingAppliedReferenceOnce()
        {
            using var firstDisposeEntered = new ManualResetEventSlim();
            using var releaseFirstDispose = new ManualResetEventSlim();
            using var secondStopEntered = new ManualResetEventSlim();
            using var secondStopReturned = new ManualResetEventSlim();
            var disposeEntries = 0;
            var owned = new OwnedProbe(1);
            OwnedProbe target = null;
            var slot = new FoxRunRos2OwnedLatestSlot<OwnedProbe>(probe =>
            {
                if (Interlocked.Increment(ref disposeEntries) == 1)
                {
                    firstDisposeEntered.Set();
                    Assert.True(releaseFirstDispose.Wait(TimeSpan.FromSeconds(10)));
                }
                probe.Dispose();
            });
            Assert.True(slot.TryPublish(() => owned));
            Assert.True(slot.TryApplyLatest(value => target = value, value => ReferenceEquals(target, value)));
            Assert.True(slot.TryPublish(() => owned));

            Exception firstFailure = null;
            Exception secondFailure = null;
            var firstStop = new Thread(() =>
            {
                try
                {
                    slot.Stop(value => ClearTarget(ref target, value));
                }
                catch (Exception exception)
                {
                    firstFailure = exception;
                }
            });
            firstStop.Start();
            Assert.True(firstDisposeEntered.Wait(TimeSpan.FromSeconds(10)));
            var secondStop = new Thread(() =>
            {
                try
                {
                    secondStopEntered.Set();
                    slot.Stop(value => ClearTarget(ref target, value));
                }
                catch (Exception exception)
                {
                    secondFailure = exception;
                }
                finally
                {
                    secondStopReturned.Set();
                }
            });
            secondStop.Start();
            Assert.True(secondStopEntered.Wait(TimeSpan.FromSeconds(10)));
            Assert.False(secondStopReturned.IsSet);
            releaseFirstDispose.Set();
            firstStop.Join();
            secondStop.Join();

            Assert.True(secondStopReturned.IsSet);
            Assert.Null(firstFailure);
            Assert.Null(secondFailure);
            Assert.Null(target);
            Assert.Equal(1, disposeEntries);
            Assert.Equal(1, owned.DisposeCount);
        }

        [Fact]
        public void InFlightCopyRejectedByStopIsDisposedOnProducerThread()
        {
            using var copyEntered = new ManualResetEventSlim();
            using var releaseCopy = new ManualResetEventSlim();
            var producerThread = 0;
            var disposeThread = 0;
            var producerResult = true;
            OwnedProbe produced = null;
            Exception producerFailure = null;
            Exception coordinatorFailure = null;
            var slot = new FoxRunRos2OwnedLatestSlot<OwnedProbe>(probe =>
            {
                disposeThread = Environment.CurrentManagedThreadId;
                probe.Dispose();
            });
            var producer = new Thread(() =>
            {
                try
                {
                    producerThread = Environment.CurrentManagedThreadId;
                    producerResult = slot.TryPublish(() =>
                    {
                        copyEntered.Set();
                        Assert.True(releaseCopy.Wait(TimeSpan.FromSeconds(10)));
                        produced = new OwnedProbe(1);
                        return produced;
                    });
                }
                catch (Exception exception)
                {
                    producerFailure = exception;
                }
            });
            producer.Start();
            Assert.True(copyEntered.Wait(TimeSpan.FromSeconds(10)));

            var coordinator = new Thread(() =>
            {
                try
                {
                    Assert.True(SpinWait.SpinUntil(() => slot.IsStopping, TimeSpan.FromSeconds(10)));
                    releaseCopy.Set();
                }
                catch (Exception exception)
                {
                    coordinatorFailure = exception;
                    releaseCopy.Set();
                }
            });
            coordinator.Start();
            slot.Stop(_ => false);
            producer.Join();
            coordinator.Join();
            if (producerFailure != null)
                throw producerFailure;
            if (coordinatorFailure != null)
                throw coordinatorFailure;

            Assert.False(producerResult);
            Assert.NotNull(produced);
            Assert.Equal(1, produced.DisposeCount);
            Assert.Equal(producerThread, disposeThread);
            Assert.Equal(1, slot.ReceivedCount);
            Assert.Equal(1, slot.RejectedAfterStopCount);
            Assert.Equal(0, slot.CopyFailedCount);
            Assert.Equal(0, slot.PendingCount);
        }

        [Fact]
        public void StopContinuesDistinctCleanupAfterDisposerThrowsAndRethrowsFirstFailure()
        {
            var applied = new OwnedProbe(1);
            var pending = new OwnedProbe(2);
            OwnedProbe target = null;
            var slot = new FoxRunRos2OwnedLatestSlot<OwnedProbe>(probe =>
            {
                probe.Dispose();
                if (probe.Value == pending.Value)
                    throw new InvalidOperationException("pending dispose failed");
            });
            Assert.True(slot.TryPublish(() => applied));
            Assert.True(slot.TryApplyLatest(value => target = value, value => ReferenceEquals(target, value)));
            Assert.True(slot.TryPublish(() => pending));

            var failure = Assert.Throws<InvalidOperationException>(() => slot.Stop(
                value => ClearTarget(ref target, value)));

            Assert.Equal("pending dispose failed", failure.Message);
            Assert.Null(target);
            Assert.Equal(1, pending.DisposeCount);
            Assert.Equal(1, applied.DisposeCount);
            slot.Stop(_ => false);
            Assert.Equal(1, pending.DisposeCount);
            Assert.Equal(1, applied.DisposeCount);
        }

        [Fact]
        public void ApplyCanRequestStopSynchronouslyWithoutSelfWaiting()
        {
            var owned = new OwnedProbe(1);
            OwnedProbe target = null;
            var slot = new FoxRunRos2OwnedLatestSlot<OwnedProbe>(probe => probe.Dispose());
            Func<OwnedProbe, bool> clear = value => ClearTarget(ref target, value);
            Assert.True(slot.TryPublish(() => owned));

            RunBounded(() => Assert.True(slot.TryApplyLatest(value =>
            {
                target = value;
                slot.Stop(clear);
            }, clear)));

            Assert.True(slot.IsStopping);
            Assert.Null(target);
            Assert.Equal(1, owned.DisposeCount);
            Assert.Equal(1, slot.AppliedCount);
            Assert.Equal(0, slot.PendingCount);
        }

        [Fact]
        public void StopCleanupCanReenterStopWithoutSelfWaitingOrDuplicateDispose()
        {
            var applied = new OwnedProbe(1);
            var pending = new OwnedProbe(2);
            OwnedProbe target = null;
            FoxRunRos2OwnedLatestSlot<OwnedProbe> slot = null;
            slot = new FoxRunRos2OwnedLatestSlot<OwnedProbe>(probe =>
            {
                slot.Stop(_ => false);
                probe.Dispose();
            });
            Assert.True(slot.TryPublish(() => applied));
            Assert.True(slot.TryApplyLatest(value => target = value, value => ReferenceEquals(target, value)));
            Assert.True(slot.TryPublish(() => pending));

            RunBounded(() => slot.Stop(value =>
            {
                slot.Stop(_ => false);
                return ClearTarget(ref target, value);
            }));

            Assert.Null(target);
            Assert.Equal(1, pending.DisposeCount);
            Assert.Equal(1, applied.DisposeCount);
            slot.Stop(_ => false);
            Assert.Equal(1, pending.DisposeCount);
            Assert.Equal(1, applied.DisposeCount);
        }

        [Fact]
        public void CopyCanRequestStopSynchronouslyAndOwnedResultIsRejectedAndDisposed()
        {
            var produced = new OwnedProbe(1);
            var producerResult = true;
            var slot = new FoxRunRos2OwnedLatestSlot<OwnedProbe>(probe => probe.Dispose());

            RunBounded(() => producerResult = slot.TryPublish(() =>
            {
                slot.Stop(_ => false);
                return produced;
            }));

            Assert.False(producerResult);
            Assert.Equal(1, produced.DisposeCount);
            Assert.Equal(1, slot.ReceivedCount);
            Assert.Equal(1, slot.RejectedAfterStopCount);
            Assert.Equal(0, slot.PendingCount);
        }

        [Fact]
        public void ProducerStopRequestLeavesPriorAppliedOwnershipForMainThreadStop()
        {
            var applied = new OwnedProbe(1);
            var copied = new OwnedProbe(2);
            OwnedProbe target = null;
            var mainThread = Environment.CurrentManagedThreadId;
            var producerThread = 0;
            var disposeThreads = new ConcurrentDictionary<int, int>();
            var slot = new FoxRunRos2OwnedLatestSlot<OwnedProbe>(probe =>
            {
                disposeThreads[probe.Value] = Environment.CurrentManagedThreadId;
                probe.Dispose();
            });
            Func<OwnedProbe, bool> clear = value => ClearTarget(ref target, value);
            Assert.True(slot.TryPublish(() => applied));
            Assert.True(slot.TryApplyLatest(value => target = value, clear));

            RunOnThread(() =>
            {
                producerThread = Environment.CurrentManagedThreadId;
                Assert.False(slot.TryPublish(() =>
                {
                    slot.Stop(clear);
                    return copied;
                }));
            });

            Assert.Same(applied, target);
            Assert.Equal(0, applied.DisposeCount);
            Assert.Equal(1, copied.DisposeCount);
            Assert.Equal(producerThread, disposeThreads[copied.Value]);
            Assert.False(disposeThreads.ContainsKey(applied.Value));

            slot.Stop(clear);

            Assert.Null(target);
            Assert.Equal(1, applied.DisposeCount);
            Assert.Equal(mainThread, disposeThreads[applied.Value]);
        }

        [Fact]
        public void ApplyFailureRemainsPrimaryWhenDeferredStopCleanupAlsoThrows()
        {
            var applied = new OwnedProbe(1);
            var candidate = new OwnedProbe(2);
            OwnedProbe target = null;
            var slot = new FoxRunRos2OwnedLatestSlot<OwnedProbe>(probe =>
            {
                probe.Dispose();
                if (probe.Value == applied.Value)
                    throw new InvalidOperationException("deferred cleanup failed");
            });
            Func<OwnedProbe, bool> clear = value => ClearTarget(ref target, value);
            Assert.True(slot.TryPublish(() => applied));
            Assert.True(slot.TryApplyLatest(value => target = value, clear));
            Assert.True(slot.TryPublish(() => candidate));

            var failure = Assert.Throws<ApplicationException>(() => slot.TryApplyLatest(value =>
            {
                target = value;
                slot.Stop(clear);
                throw new ApplicationException("apply failed");
            }, clear));

            Assert.Equal("apply failed", failure.Message);
            Assert.Null(target);
            Assert.Equal(1, candidate.DisposeCount);
            Assert.Equal(1, applied.DisposeCount);
            slot.Stop(_ => false);
        }

        [Fact]
        public void CopyFailureAndStopRequestReturnFailureBeforeMainThreadCleanupThrows()
        {
            var applied = new OwnedProbe(1);
            OwnedProbe target = null;
            var mainThread = Environment.CurrentManagedThreadId;
            var producerThread = 0;
            var disposeThread = 0;
            var producerResult = true;
            Exception producerFailure = null;
            Exception copyFailure = null;
            var expectedCopyFailure = new ApplicationException("copy failed");
            var slot = new FoxRunRos2OwnedLatestSlot<OwnedProbe>(probe =>
            {
                disposeThread = Environment.CurrentManagedThreadId;
                probe.Dispose();
                throw new InvalidOperationException("applied cleanup failed");
            });
            Func<OwnedProbe, bool> clear = value => ClearTarget(ref target, value);
            Assert.True(slot.TryPublish(() => applied));
            Assert.True(slot.TryApplyLatest(value => target = value, clear));

            var producer = new Thread(() =>
            {
                try
                {
                    producerThread = Environment.CurrentManagedThreadId;
                    producerResult = slot.TryPublish(() =>
                    {
                        slot.Stop(clear);
                        throw expectedCopyFailure;
                    }, out copyFailure);
                }
                catch (Exception exception)
                {
                    producerFailure = exception;
                }
            });
            producer.Start();
            producer.Join();

            Assert.Null(producerFailure);
            Assert.False(producerResult);
            Assert.Same(expectedCopyFailure, copyFailure);
            Assert.Same(applied, target);
            Assert.Equal(0, applied.DisposeCount);
            Assert.NotEqual(producerThread, disposeThread);

            var cleanupFailure = Assert.Throws<InvalidOperationException>(() => slot.Stop(clear));

            Assert.Equal("applied cleanup failed", cleanupFailure.Message);
            Assert.Null(target);
            Assert.Equal(1, applied.DisposeCount);
            Assert.Equal(mainThread, disposeThread);
        }

        private static bool ClearTarget(ref OwnedProbe target, OwnedProbe value)
        {
            if (!ReferenceEquals(target, value))
                return false;
            target = null;
            return true;
        }

        private static void AssertMonotonic(IEnumerable<CounterSnapshot> snapshots)
        {
            CounterSnapshot previous = default;
            foreach (var current in snapshots.OrderBy(snapshot => snapshot.Sequence))
            {
                Assert.True(current.Received >= previous.Received);
                Assert.True(current.Replaced >= previous.Replaced);
                Assert.True(current.Applied >= previous.Applied);
                Assert.True(current.Rejected >= previous.Rejected);
                Assert.True(current.CopyFailed >= previous.CopyFailed);
                previous = current;
            }
        }

        private static void RunOnThread(Action action)
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            });
            thread.Start();
            thread.Join();
            if (failure != null)
                throw failure;
        }

        private static void RunBounded(Action action)
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            })
            {
                IsBackground = true,
            };
            thread.Start();
            Assert.True(
                thread.Join(TimeSpan.FromSeconds(2)),
                "Slot operation did not complete within the reentrancy bound.");
            if (failure != null)
                throw failure;
        }

        private sealed class OwnedProbe
        {
            private int _disposeCount;

            public OwnedProbe(int value) => Value = value;

            public int Value { get; }
            public int DisposeCount => Volatile.Read(ref _disposeCount);

            public void Dispose()
            {
                if (Interlocked.Increment(ref _disposeCount) != 1)
                    throw new InvalidOperationException("Owned probe disposed more than once.");
            }
        }

        private readonly struct CounterSnapshot
        {
            private static long _sequence;

            private CounterSnapshot(long received, long replaced, long applied, long rejected, long copyFailed)
            {
                Sequence = Interlocked.Increment(ref _sequence);
                Received = received;
                Replaced = replaced;
                Applied = applied;
                Rejected = rejected;
                CopyFailed = copyFailed;
            }

            public long Sequence { get; }
            public long Received { get; }
            public long Replaced { get; }
            public long Applied { get; }
            public long Rejected { get; }
            public long CopyFailed { get; }

            public static CounterSnapshot Read(FoxRunRos2OwnedLatestSlot<OwnedProbe> slot)
                => new CounterSnapshot(
                    slot.ReceivedCount,
                    slot.ReplacedCount,
                    slot.AppliedCount,
                    slot.RejectedAfterStopCount,
                    slot.CopyFailedCount);
        }
    }
}
#endif
