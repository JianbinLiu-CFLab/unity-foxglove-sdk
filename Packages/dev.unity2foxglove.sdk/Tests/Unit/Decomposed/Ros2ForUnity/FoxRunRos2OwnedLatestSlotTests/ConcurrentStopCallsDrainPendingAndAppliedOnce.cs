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


public sealed partial class FoxRunRos2OwnedLatestSlotTests
    {
        [Fact]
        public void ConcurrentStopCallsDrainPendingAndAppliedOnce()
        {
            using var firstDisposeEntered = new ManualResetEventSlim();
            using var releaseFirstDispose = new ManualResetEventSlim();
            using var secondStopEntered = new ManualResetEventSlim();
            using var secondStopReturned = new ManualResetEventSlim();
            var disposeEntries = 0;
            var applied = new OwnedProbe(1);
            var pending = new OwnedProbe(2);
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
            Assert.True(slot.TryPublish(() => applied));
            Assert.True(slot.TryApplyLatest(value => target = value, value => ReferenceEquals(target, value)));
            Assert.True(slot.TryPublish(() => pending));

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
            Assert.Equal(2, disposeEntries);
            Assert.Equal(1, applied.DisposeCount);
            Assert.Equal(1, pending.DisposeCount);
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
    }
}
#endif
