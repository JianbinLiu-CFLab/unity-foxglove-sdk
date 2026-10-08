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

        [Fact]
        public void BoundedExternalStopReturnsUndrainedUntilALaterStopDrains()
        {
            var applied = new OwnedProbe(1);
            OwnedProbe target = null;
            Func<OwnedProbe, bool> clear = value =>
            {
                if (!ReferenceEquals(target, value))
                    return false;
                target = null;
                return true;
            };
            var slot = new FoxRunRos2OwnedLatestSlot<OwnedProbe>(probe => probe.Dispose());
            Assert.True(slot.TryPublish(() => applied));
            Assert.True(slot.TryApplyLatest((Action<OwnedProbe>)(value => target = value), clear));

            using var copyEntered = new ManualResetEventSlim();
            using var releaseCopy = new ManualResetEventSlim();
            var producer = new Thread(() => slot.TryPublish(() =>
            {
                copyEntered.Set();
                releaseCopy.Wait(TimeSpan.FromSeconds(30));
                return new OwnedProbe(2);
            }))
            {
                IsBackground = true,
            };
            Thread stopper = null;
            try
            {
                producer.Start();
                Assert.True(copyEntered.Wait(TimeSpan.FromSeconds(10)));

                stopper = new Thread(() => slot.Stop(clear)) { IsBackground = true };
                stopper.Start();
                Assert.True(
                    stopper.Join(TimeSpan.FromSeconds(5)),
                    "A bounded external Stop must return while a publisher is still active.");
                Assert.False(slot.IsStopped);
                Assert.True(slot.IsStopping);
                Assert.Same(applied, target);
                Assert.Equal(0, applied.DisposeCount);

                releaseCopy.Set();
                Assert.True(producer.Join(TimeSpan.FromSeconds(10)));

                slot.Stop(clear);
                Assert.True(slot.IsStopped);
                Assert.Null(target);
                Assert.Equal(1, applied.DisposeCount);
            }
            finally
            {
                releaseCopy.Set();
                producer.Join(TimeSpan.FromSeconds(10));
                stopper?.Join(TimeSpan.FromSeconds(10));
            }
        }

        [Fact]
        public void ApplyFailureDisposesCandidateBeforeFatalClearEscapes()
        {
            var candidate = new OwnedProbe(1);
            var slot = new FoxRunRos2OwnedLatestSlot<OwnedProbe>(probe => probe.Dispose());
            Assert.True(slot.TryPublish(() => candidate));

            var thrown = Record.Exception(() => slot.TryApplyLatest(
                (Action<OwnedProbe>)(_ => throw new ApplicationException("apply failed")),
                _ => throw new OutOfMemoryException("clear fatal")));

            Assert.IsType<OutOfMemoryException>(thrown);
            Assert.Equal(1, candidate.DisposeCount);
        }

        [Fact]
        public void ApplyAndRetainFailureDisposesCandidateBeforeFatalClearEscapes()
        {
            var candidate = new OwnedProbe(1);
            var slot = new FoxRunRos2OwnedLatestSlot<OwnedProbe>(probe => probe.Dispose());
            Assert.True(slot.TryPublish(() => candidate));

            var thrown = Record.Exception(() => slot.TryApplyLatest(
                (Func<OwnedProbe, bool>)(_ => throw new ApplicationException("apply failed")),
                _ => throw new OutOfMemoryException("clear fatal")));

            Assert.IsType<OutOfMemoryException>(thrown);
            Assert.Equal(1, candidate.DisposeCount);
        }

        [Fact]
        public void DecidedApplyFailureDisposesCandidateBeforeFatalClearEscapes()
        {
            var candidate = new OwnedProbe(1);
            var slot = new FoxRunRos2OwnedLatestSlot<OwnedProbe>(probe => probe.Dispose());
            Assert.True(slot.TryPublish(() => candidate));

            var thrown = Record.Exception(() => slot.TryApplyLatest(
                (next, current) => FoxRunRos2PendingDecision.Apply,
                _ => throw new ApplicationException("apply failed"),
                _ => throw new OutOfMemoryException("clear fatal")));

            Assert.IsType<OutOfMemoryException>(thrown);
            Assert.Equal(1, candidate.DisposeCount);
        }

        [Fact]
        public void StopDrainPrefersFatalCleanupOverEarlierRecoverableFailure()
        {
            var applied = new OwnedProbe(1);
            var pending = new OwnedProbe(2);
            OwnedProbe target = null;
            var slot = new FoxRunRos2OwnedLatestSlot<OwnedProbe>(probe =>
            {
                probe.Dispose();
                if (probe.Value == 2)
                    throw new OutOfMemoryException("pending dispose fatal");
            });
            Assert.True(slot.TryPublish(() => applied));
            Assert.True(slot.TryApplyLatest((Action<OwnedProbe>)(value => target = value), value => ReferenceEquals(target, value)));
            Assert.True(slot.TryPublish(() => pending));

            var thrown = Record.Exception(() => slot.Stop(
                _ => throw new InvalidOperationException("clear recoverable")));

            Assert.IsType<OutOfMemoryException>(thrown);
            Assert.Equal(1, pending.DisposeCount);
            Assert.Equal(1, applied.DisposeCount);
            Assert.True(slot.IsStopped);
        }

        [Fact]
        public void ApplyFailurePrefersFatalDeferredStopCleanup()
            => AssertApplyFailurePrefersFatalDeferredStopCleanup(
                (slot, apply, clear) => slot.TryApplyLatest(apply, clear));

        [Fact]
        public void ApplyAndRetainFailurePrefersFatalDeferredStopCleanup()
            => AssertApplyFailurePrefersFatalDeferredStopCleanup(
                (slot, apply, clear) => slot.TryApplyLatest(
                    (Func<OwnedProbe, bool>)(value =>
                    {
                        apply(value);
                        return true;
                    }),
                    clear));

        [Fact]
        public void DecidedApplyFailurePrefersFatalDeferredStopCleanup()
            => AssertApplyFailurePrefersFatalDeferredStopCleanup(
                (slot, apply, clear) => slot.TryApplyLatest(
                    (next, current) => FoxRunRos2PendingDecision.Apply,
                    apply,
                    clear));

        private static void AssertApplyFailurePrefersFatalDeferredStopCleanup(
            Func<FoxRunRos2OwnedLatestSlot<OwnedProbe>, Action<OwnedProbe>, Func<OwnedProbe, bool>, bool> applyLatest)
        {
            var applied = new OwnedProbe(1);
            var candidate = new OwnedProbe(2);
            OwnedProbe target = null;
            var slot = new FoxRunRos2OwnedLatestSlot<OwnedProbe>(probe =>
            {
                probe.Dispose();
                if (probe.Value == 1)
                    throw new OutOfMemoryException("deferred cleanup fatal");
            });
            Func<OwnedProbe, bool> clear = value =>
            {
                if (!ReferenceEquals(target, value))
                    return false;
                target = null;
                return true;
            };
            Assert.True(slot.TryPublish(() => applied));
            Assert.True(slot.TryApplyLatest((Action<OwnedProbe>)(value => target = value), clear));
            Assert.True(slot.TryPublish(() => candidate));

            var thrown = Record.Exception(() => applyLatest(
                slot,
                value =>
                {
                    target = value;
                    slot.Stop(clear);
                    throw new ApplicationException("apply failed");
                },
                clear));

            Assert.IsType<OutOfMemoryException>(thrown);
            Assert.Equal(1, applied.DisposeCount);
            Assert.Equal(1, candidate.DisposeCount);
            Assert.True(slot.IsStopped);
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
