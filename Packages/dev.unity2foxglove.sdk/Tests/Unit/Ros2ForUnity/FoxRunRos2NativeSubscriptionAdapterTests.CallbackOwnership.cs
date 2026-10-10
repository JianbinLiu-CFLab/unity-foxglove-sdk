// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Ros2ForUnity
// Purpose: Verify typed native subscription binding lifecycle and ownership.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using Unity2Foxglove.Ros2ForUnity.Native;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Ros2ForUnity
{
    public sealed partial class FoxRunRos2NativeSubscriptionAdapterTests
    {
        [Fact]
        public void FatalCallbackCopyFailurePassesThroughTheExecutorBoundary()
        {
            var backend = new FakeBackend();
            var binding = CreateBinding(
                backend,
                3,
                () => 3,
                _ => { },
                _ => false,
                _ => throw new OutOfMemoryException("fatal"));
            Assert.True(binding.TryRegister().Succeeded);
            using var borrowed = Message("boom");

            Assert.Throws<OutOfMemoryException>(() => backend.Invoke(borrowed));
            Assert.Equal(1, binding.CopyFailedCount);

            binding.Stop();
        }

        [Fact]
        public void SelfOriginEnvelopeIsDroppedBeforeApplyWhileRemoteOriginStillApplies()
        {
            var backend = new FakeBackend();
            FakeMessage applied = null;
            var binding = CreateBinding(
                backend,
                3,
                () => 3,
                value => applied = value,
                _ => false,
                dropBeforeApply: value => value.Data == "self");
            Assert.True(binding.TryRegister().Succeeded);

            backend.Invoke(Message("self"));
            Assert.False(binding.TryApplyLatest(3));
            Assert.Null(applied);
            Assert.Equal(1, binding.SameOriginDropCount);

            backend.Invoke(Message("remote"));
            Assert.True(binding.TryApplyLatest(3));
            Assert.Equal("remote", applied.Data);

            binding.Stop();
        }

        [Fact]
        public void BorrowedCallbackReferenceCanNeverBecomeFrameworkOwned()
        {
            var backend = new FakeBackend();
            var binding = CreateBinding(
                backend,
                3,
                () => 3,
                _ => { },
                _ => false,
                source => source);
            Assert.True(binding.TryRegister().Succeeded);
            using var borrowed = Message("borrowed");

            var exception = Record.Exception(() => backend.Invoke(borrowed));

            Assert.Null(exception);
            Assert.Equal(1, binding.CopyFailedCount);
            Assert.Equal(0, borrowed.DisposeCount);
            Assert.False(binding.TryApplyLatest(3));
            binding.Stop();
            Assert.Equal(0, borrowed.DisposeCount);
        }

        [Fact]
        public void StopIsIdempotentAndHonorsTransportOwnershipOrder()
        {
            var events = new List<string>();
            var backend = new FakeBackend(events);
            FakeMessage applied = null;
            var binding = CreateBinding(
                backend,
                5,
                () => 5,
                value => applied = value,
                value =>
                {
                    events.Add("clear-applied");
                    if (!ReferenceEquals(applied, value))
                        return false;
                    applied = null;
                    return true;
                },
                source => Message(source.Data),
                value =>
                {
                    events.Add("dispose-" + value.Data);
                    value.Dispose();
                });
            Assert.True(binding.TryRegister().Succeeded);
            using var first = Message("applied");
            using var second = Message("pending");
            backend.Invoke(first);
            Assert.True(binding.TryApplyLatest(5));
            backend.Invoke(second);

            binding.Stop();
            binding.Stop();

            Assert.Equal(
                new[]
                {
                    "remove-subscription",
                    "clear-applied",
                    "dispose-pending",
                    "dispose-applied",
                    "release-node"
                },
                events);
            Assert.Null(applied);
            Assert.Equal(1, backend.RemoveCount);
            Assert.Equal(1, backend.ReleaseCount);
        }

        [Fact]
        public void RegistrationExceptionBecomesBoundedStableFailure()
        {
            var backend = new FakeBackend { RegistrationException = new InvalidOperationException(new string('z', 2048)) };
            var binding = CreateBinding(backend, 1, () => 1, _ => { }, _ => false);

            var result = binding.TryRegister();

            Assert.False(result.Succeeded);
            Assert.Equal(FoxRunRos2RegistrationError.BackendFailure, result.Error);
            Assert.Equal(FoxRunRos2SubscriptionBindingState.Failed, binding.State);
            Assert.True(result.Diagnostic.Length <= FoxRunRos2RegistrationResult.MaximumDiagnosticLength);
        }

        [Fact]
        public void TeardownFailureDoesNotSkipOwnedCleanupOrNodeRelease()
        {
            var events = new List<string>();
            var backend = new FakeBackend(events) { RemoveException = new InvalidOperationException("remove") };
            FakeMessage applied = null;
            var binding = CreateBinding(
                backend,
                6,
                () => 6,
                value => applied = value,
                value =>
                {
                    events.Add("clear-applied");
                    applied = null;
                    return true;
                },
                dispose: value =>
                {
                    events.Add("dispose-owned");
                    value.Dispose();
                });
            Assert.True(binding.TryRegister().Succeeded);
            using var borrowed = Message("owned");
            backend.Invoke(borrowed);
            Assert.True(binding.TryApplyLatest(6));

            var exception = Record.Exception(binding.Stop);

            Assert.Null(exception);
            Assert.Equal(
                new[] { "remove-subscription", "clear-applied", "dispose-owned" },
                events);
            Assert.True(binding.CleanupPending);
            backend.RemoveException = null;
            binding.Stop();
            Assert.Equal(
                new[]
                {
                    "remove-subscription",
                    "clear-applied",
                    "dispose-owned",
                    "remove-subscription",
                    "release-node"
                },
                events);
            Assert.Equal(FoxRunRos2SubscriptionBindingState.Stopped, binding.State);
        }

        [Fact]
        public void StopClosesCallbackAdmissionBeforeBlockingTransportRemoval()
        {
            using var removeEntered = new ManualResetEventSlim();
            using var releaseRemove = new ManualResetEventSlim();
            var copied = 0;
            var backend = new FakeBackend
            {
                RemoveEntered = removeEntered,
                ReleaseRemove = releaseRemove
            };
            var binding = CreateBinding(
                backend,
                12,
                () => 12,
                _ => { },
                _ => false,
                source =>
                {
                    Interlocked.Increment(ref copied);
                    return Message(source.Data);
                });
            Assert.True(binding.TryRegister().Succeeded);
            Exception stopFailure = null;
            var stopThread = new Thread(() =>
            {
                try { binding.Stop(); }
                catch (Exception exception) { stopFailure = exception; }
            });
            stopThread.Start();
            Assert.True(removeEntered.Wait(TimeSpan.FromSeconds(10)));
            using var late = Message("late");

            var callbackFailure = Record.Exception(() => backend.InvokeLate(late));

            Assert.Null(callbackFailure);
            Assert.Equal(0, Volatile.Read(ref copied));
            Assert.Equal(1, binding.RejectedAfterStopCount);
            Assert.False(binding.TryApplyLatest(12));
            releaseRemove.Set();
            Assert.True(stopThread.Join(TimeSpan.FromSeconds(10)));
            Assert.Null(stopFailure);
            Assert.Equal(FoxRunRos2SubscriptionBindingState.Stopped, binding.State);
        }

        [Fact]
        public void StopWaitsForInFlightCopyAndDisposesItWithoutApplying()
        {
            using var copyEntered = new ManualResetEventSlim();
            using var releaseCopy = new ManualResetEventSlim();
            using var removeEntered = new ManualResetEventSlim();
            var disposed = 0;
            var applied = 0;
            var backend = new FakeBackend { RemoveEntered = removeEntered };
            var binding = CreateBinding(
                backend,
                13,
                () => 13,
                _ => Interlocked.Increment(ref applied),
                _ => false,
                source =>
                {
                    copyEntered.Set();
                    Assert.True(releaseCopy.Wait(TimeSpan.FromSeconds(10)));
                    return Message(source.Data);
                },
                value =>
                {
                    Interlocked.Increment(ref disposed);
                    value.Dispose();
                });
            Assert.True(binding.TryRegister().Succeeded);
            using var borrowed = Message("in-flight");
            Exception callbackFailure = null;
            var callbackThread = new Thread(() =>
            {
                try { backend.Invoke(borrowed); }
                catch (Exception exception) { callbackFailure = exception; }
            });
            callbackThread.Start();
            Assert.True(copyEntered.Wait(TimeSpan.FromSeconds(10)));
            Exception stopFailure = null;
            var stopThread = new Thread(() =>
            {
                try { binding.Stop(); }
                catch (Exception exception) { stopFailure = exception; }
            });
            stopThread.Start();
            Assert.True(removeEntered.Wait(TimeSpan.FromSeconds(10)));
            releaseCopy.Set();
            Assert.True(callbackThread.Join(TimeSpan.FromSeconds(10)));
            Assert.True(stopThread.Join(TimeSpan.FromSeconds(10)));

            Assert.Null(callbackFailure);
            Assert.Null(stopFailure);
            Assert.Equal(0, Volatile.Read(ref applied));
            Assert.Equal(1, Volatile.Read(ref disposed));
            Assert.Equal(1, binding.RejectedAfterStopCount);
            Assert.Equal(FoxRunRos2SubscriptionBindingState.Stopped, binding.State);
        }

        [Fact]
        public void ApplyReentrantStopReleasesLeaseAfterSlotCompletesItsDeferredDrain()
        {
            var backend = new FakeBackend();
            FakeMessage applied = null;
            FoxRunRos2SubscriptionBinding<FakeMessage> binding = null;
            binding = CreateBinding(
                backend,
                129,
                () => 129,
                value =>
                {
                    applied = value;
                    binding.Stop();
                },
                value =>
                {
                    if (!ReferenceEquals(applied, value))
                        return false;
                    applied = null;
                    return true;
                });
            Assert.True(binding.TryRegister().Succeeded);
            using var borrowed = Message("first");
            backend.Invoke(borrowed);

            Assert.True(binding.TryApplyLatest(129));

            Assert.Null(applied);
            Assert.Equal(1, backend.RemoveCount);
            Assert.Equal(1, backend.ReleaseCount);
            binding.Stop();
            Assert.Equal(1, backend.RemoveCount);
            Assert.Equal(1, backend.ReleaseCount);
        }

        [Fact]
        public void ApplyReentrantStopFinalizesInFlightCallbackOnMainThreadAndReleasesLease()
        {
            using var copyEntered = new ManualResetEventSlim();
            using var releaseCopy = new ManualResetEventSlim();
            using var stopRequested = new ManualResetEventSlim();
            var mainThread = Environment.CurrentManagedThreadId;
            var disposeThreads = new ConcurrentDictionary<string, int>();
            var backend = new FakeBackend();
            FakeMessage applied = null;
            FakeMessage firstOwned = null;
            FakeMessage lateOwned = null;
            FoxRunRos2SubscriptionBinding<FakeMessage> binding = null;
            binding = CreateBinding(
                backend,
                130,
                () => 130,
                value =>
                {
                    applied = value;
                    stopRequested.Set();
                    binding.Stop();
                },
                value =>
                {
                    if (!ReferenceEquals(applied, value))
                        return false;
                    applied = null;
                    return true;
                },
                source =>
                {
                    var owned = Message(source.Data);
                    if (source.Data == "first")
                    {
                        firstOwned = owned;
                    }
                    else
                    {
                        lateOwned = owned;
                        copyEntered.Set();
                        Assert.True(releaseCopy.Wait(TimeSpan.FromSeconds(10)));
                    }
                    return owned;
                },
                value =>
                {
                    disposeThreads[value.Data] = Environment.CurrentManagedThreadId;
                    value.Dispose();
                });
            Assert.True(binding.TryRegister().Succeeded);
            using var first = Message("first");
            using var late = Message("late");
            backend.Invoke(first);

            Exception callbackFailure = null;
            var callback = new Thread(() =>
            {
                try { backend.Invoke(late); }
                catch (Exception exception) { callbackFailure = exception; }
            }) { IsBackground = true };
            callback.Start();
            Assert.True(copyEntered.Wait(TimeSpan.FromSeconds(10)));

            var slot = typeof(FoxRunRos2SubscriptionBinding<FakeMessage>)
                .GetField("_slot", System.Reflection.BindingFlags.Instance |
                                   System.Reflection.BindingFlags.NonPublic)
                ?.GetValue(binding);
            Assert.NotNull(slot);
            var stopState = slot.GetType().GetField(
                "_stopState",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var activeAppliers = slot.GetType().GetField(
                "_activeAppliers",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(stopState);
            Assert.NotNull(activeAppliers);
            Exception releaseFailure = null;
            var release = new Thread(() =>
            {
                try
                {
                    Assert.True(stopRequested.Wait(TimeSpan.FromSeconds(10)));
                    var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
                    while (DateTime.UtcNow < deadline)
                    {
                        if ((int)activeAppliers.GetValue(slot) == 0
                            && (int)stopState.GetValue(slot) != 0)
                        {
                            releaseCopy.Set();
                            return;
                        }
                        Thread.Yield();
                    }
                    throw new TimeoutException("The apply operation did not request deferred stop completion.");
                }
                catch (Exception exception)
                {
                    releaseFailure = exception;
                }
            }) { IsBackground = true };
            release.Start();

            try
            {
                Assert.True(binding.TryApplyLatest(130));
                Assert.True(callback.Join(TimeSpan.FromSeconds(10)));
                Assert.True(release.Join(TimeSpan.FromSeconds(10)));

                Assert.Null(callbackFailure);
                Assert.Null(releaseFailure);
                Assert.Null(applied);
                Assert.Equal(1, firstOwned.DisposeCount);
                Assert.Equal(1, lateOwned.DisposeCount);
                Assert.Equal(mainThread, disposeThreads["first"]);
                Assert.NotEqual(mainThread, disposeThreads["late"]);
                Assert.Equal(1, backend.RemoveCount);
                Assert.Equal(1, backend.ReleaseCount);

                binding.Stop();
                Assert.Equal(1, backend.RemoveCount);
                Assert.Equal(1, backend.ReleaseCount);
            }
            finally
            {
                releaseCopy.Set();
                callback.Join(TimeSpan.FromSeconds(10));
                release.Join(TimeSpan.FromSeconds(10));
                binding.Stop();
            }
        }

    }
}
#endif
