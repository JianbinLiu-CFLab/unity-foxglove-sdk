// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Ros2ForUnity
// Purpose: Locks closed-generic custom native publisher backend ownership.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Unity.FoxgloveSDK.Components;
using Unity2Foxglove.Ros2ForUnity.Native;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Ros2ForUnity
{


public sealed partial class FoxRunRos2CustomPublisherBackendTests
    {
        [Fact]
        public void ContextlessCallbackThreadReleaseCompletesThroughOwnerThreadPump()
        {
            var ownerThreadId = Thread.CurrentThread.ManagedThreadId;
            var driver = new FakeNodeDriver();
            var owner = new Ros2ForUnityFoxRunNodeOwner(
                driver,
                () => true,
                new ManagedQosFactory(),
                ownerThreadId,
                ownerContext: null);
            var publisher = owner.AcquirePublisherBackend();

            Assert.True(owner.ReleaseHostOwnership());
            Assert.Equal(0, driver.ReleaseNodeCount);

            Assert.True(
                RunOnCallbackThread(
                    publisher.ReleaseNodeOwnership,
                    "The contextless callback-thread release did not complete."));
            Assert.Equal(0, driver.ReleaseNodeCount);

            Assert.True(owner.RetryPendingNodeReleaseOnCurrentThread());
            Assert.Equal(1, driver.ReleaseNodeCount);
            Assert.Equal(ownerThreadId, driver.ReleaseThreadId);
        }

        [Fact]
        public void ContextlessTrackerReleaseRetriesTheOwnerThreadHandoff()
        {
            var ownerThreadId = Thread.CurrentThread.ManagedThreadId;
            var driver = new FakeNodeDriver();
            Ros2ForUnityFoxRunNodeOwner owner = null;
            var tracker = new FoxRunRos2CustomNativeTransportLeaseTracker(
                () => owner = new Ros2ForUnityFoxRunNodeOwner(
                    driver,
                    () => true,
                    new ManagedQosFactory(),
                    ownerThreadId,
                    ownerContext: null));

            Assert.True(tracker.TryAcquirePublisherBackend(out var publisher));
            Assert.True(owner.ReleaseHostOwnership());

            Assert.True(
                RunOnCallbackThread(
                    publisher.ReleaseNodeOwnership,
                    "The contextless tracker release did not complete."));
            Assert.Equal(0, driver.ReleaseNodeCount);

            Assert.True(tracker.RetryPendingRelease());
            Assert.Equal(1, driver.ReleaseNodeCount);
            Assert.Equal(ownerThreadId, driver.ReleaseThreadId);
        }

        [Fact]
        public void CallbackThreadLeaseReleaseRemainsRetryableOnTheOwnerThread()
        {
            var ownerThreadId = Thread.CurrentThread.ManagedThreadId;
            var context = new QueuedSynchronizationContext();
            var driver = new FakeNodeDriver();
            Ros2ForUnityFoxRunNodeOwner owner = null;
            var tracker = new FoxRunRos2CustomNativeTransportLeaseTracker(
                () => owner = new Ros2ForUnityFoxRunNodeOwner(
                    driver,
                    () => true,
                    new ManagedQosFactory(),
                    ownerThreadId,
                    context));

            Assert.True(tracker.TryAcquirePublisherBackend(out var publisher));
            Assert.True(tracker.TryAcquireSubscriptionBackend(out var subscription));

            Assert.True(
                RunOnCallbackThread(
                    () =>
                    {
                        Assert.True(publisher.ReleaseNodeOwnership());
                        return subscription.ReleaseNodeOwnership();
                    },
                    "The callback-thread lease release did not complete within the timeout."));
            Assert.Equal(0, driver.ReleaseNodeCount);
            Assert.True(context.PendingCount > 0);

            context.RunPending();

            Assert.True(tracker.RetryPendingRelease());
            Assert.Equal(1, driver.ReleaseNodeCount);
            Assert.Equal(ownerThreadId, driver.ReleaseThreadId);
            Assert.NotNull(owner);
        }

        [Fact]
        public void OwnerContextRetriesQueuedNodeReleaseAcrossPublisherAndSubscriptionLeases()
        {
            var ownerThreadId = Thread.CurrentThread.ManagedThreadId;
            var context = new QueuedSynchronizationContext();
            var driver = new FakeNodeDriver { ReleaseFailuresRemaining = 1 };
            var owner = new Ros2ForUnityFoxRunNodeOwner(
                driver,
                () => true,
                new ManagedQosFactory(),
                ownerThreadId,
                context);
            var publisher = owner.AcquirePublisherBackend();
            var subscription = owner.AcquireBackend();

            Assert.False(
                RunOnCallbackThread(
                    () =>
                    {
                        Assert.True(publisher.ReleaseNodeOwnership());
                        Assert.True(subscription.ReleaseNodeOwnership());
                        return owner.ReleaseHostOwnership();
                    },
                    "The callback-thread owner release did not complete within the timeout."));
            Assert.Equal(0, driver.ReleaseNodeCount);
            Assert.True(context.PendingCount > 0);

            context.RunPending();

            Assert.Equal(2, driver.ReleaseNodeCount);
            Assert.Equal(ownerThreadId, driver.ReleaseThreadId);
        }

        [Fact]
        public void OwnerContextRetriesQueuedNodeReleaseAfterDriverException()
        {
            var ownerThreadId = Thread.CurrentThread.ManagedThreadId;
            var context = new QueuedSynchronizationContext();
            var driver = new FakeNodeDriver
            {
                ReleaseExceptionsRemaining = 1,
            };
            var owner = new Ros2ForUnityFoxRunNodeOwner(
                driver,
                () => true,
                new ManagedQosFactory(),
                ownerThreadId,
                context);

            Assert.False(
                RunOnCallbackThread(
                    owner.ReleaseHostOwnership,
                    "The callback-thread owner release did not complete within the timeout."));
            Assert.Equal(0, driver.ReleaseNodeCount);

            context.RunPending();

            Assert.Equal(2, driver.ReleaseNodeCount);
            Assert.Equal(ownerThreadId, driver.ReleaseThreadId);
        }

        [Fact]
        public void BindingRetainsOwnershipWhenOwnerReleaseFailsOnTheOwnerThread()
        {
            var driver = new FakeNodeDriver { ReleaseFailuresRemaining = 1 };
            var owner = new Ros2ForUnityFoxRunNodeOwner(driver, () => true, new ManagedQosFactory());
            var publisher = owner.AcquirePublisherBackend();

            Assert.True(owner.ReleaseHostOwnership());
            Assert.False(publisher.ReleaseNodeOwnership());
            Assert.Equal(1, driver.ReleaseNodeCount);

            Assert.True(publisher.ReleaseNodeOwnership());
            Assert.Equal(2, driver.ReleaseNodeCount);
        }

        [Fact]
        public void CustomTransportLeaseTrackerRejectsAcquireWhileHostReleaseIsInFlight()
        {
            var driver = new FakeNodeDriver
            {
                BlockRelease = true,
            };
            using var ownerReady = new ManualResetEventSlim(false);
            using var releaseRequested = new ManualResetEventSlim(false);
            using var releaseCompleted = new ManualResetEventSlim(false);
            Exception ownerThreadFailure = null;
            var ownerThreadId = 0;
            IFoxRunRos2NativePublisherBackend publisher = null;
            var ownerThread = new Thread(
                () =>
                {
                    ownerThreadId = Thread.CurrentThread.ManagedThreadId;
                    ownerReady.Set();
                    releaseRequested.Wait(TimeSpan.FromSeconds(5));
                    try
                    {
                        Assert.True(publisher.ReleaseNodeOwnership());
                    }
                    catch (Exception exception)
                    {
                        ownerThreadFailure = exception;
                    }
                    finally
                    {
                        releaseCompleted.Set();
                    }
                });
            ownerThread.Start();
            Assert.True(ownerReady.Wait(TimeSpan.FromSeconds(5)));
            var tracker = new FoxRunRos2CustomNativeTransportLeaseTracker(
                () => new Ros2ForUnityFoxRunNodeOwner(
                    driver,
                    () => true,
                    ownerThreadId: ownerThreadId));

            Assert.True(tracker.TryAcquirePublisherBackend(out publisher));
            releaseRequested.Set();

            Assert.True(
                driver.ReleaseStarted.Wait(TimeSpan.FromSeconds(5)),
                "The host release must be observable while it is in flight.");
            Assert.False(
                tracker.TryAcquirePublisherBackend(out _),
                "A new lease must not reuse an owner while its host release is in flight.");

            driver.ContinueRelease.Set();
            Assert.True(
                releaseCompleted.Wait(TimeSpan.FromSeconds(5)),
                "The blocked host release did not complete.");
            ownerThread.Join(TimeSpan.FromSeconds(5));
            Assert.Null(ownerThreadFailure);

            ownerThreadId = Thread.CurrentThread.ManagedThreadId;
            Assert.True(tracker.TryAcquirePublisherBackend(out var nextPublisher));
            Assert.True(nextPublisher.ReleaseNodeOwnership());
        }

        [Fact]
        public void CustomTransportLeaseTrackerReservesOwnerBeforeBackendAcquisition()
        {
            var driver = new FakeNodeDriver();
            var owner = new Ros2ForUnityFoxRunNodeOwner(driver);
            var createdOwners = 0;
            var tracker = new FoxRunRos2CustomNativeTransportLeaseTracker(
                () =>
                {
                    Interlocked.Increment(ref createdOwners);
                    return owner;
                });

            Assert.True(tracker.TryAcquireSubscriptionBackend(out var first));
            var ownerSync = PrivateField<object>(owner, "_sync");
            var trackerSync = PrivateField<object>(tracker, "_sync");
            using var acquisitionStarted = new ManualResetEventSlim(false);
            IFoxRunRos2NativePublisherBackend second = null;
            var acquired = false;
            Task acquisition = null;

            Monitor.Enter(ownerSync);
            try
            {
                acquisition = Task.Run(
                    () =>
                    {
                        acquisitionStarted.Set();
                        acquired = tracker.TryAcquirePublisherBackend(out second);
                    });
                Assert.True(acquisitionStarted.Wait(TimeSpan.FromSeconds(5)));
                Assert.True(
                    SpinWait.SpinUntil(
                        () =>
                        {
                            lock (trackerSync)
                                return PrivateField<int>(tracker, "_leaseCount") == 2;
                        },
                        TimeSpan.FromSeconds(5)),
                    "Selecting the shared owner must reserve its tracker lease before backend acquisition can block.");
                Assert.Equal(1, Volatile.Read(ref createdOwners));
            }
            finally
            {
                Monitor.Exit(ownerSync);
            }

            Assert.True(
                acquisition.Wait(TimeSpan.FromSeconds(5)),
                "Publisher backend acquisition did not complete after the owner lock was released.");
            Assert.True(acquired);
            Assert.NotNull(second);
            first.ReleaseNodeOwnership();
            Assert.Equal(0, driver.ReleaseNodeCount);
            second.ReleaseNodeOwnership();
            Assert.Equal(1, driver.ReleaseNodeCount);
            Assert.Equal(1, Volatile.Read(ref createdOwners));
        }

        private static T PrivateField<T>(object instance, string name)
            => (T)(instance.GetType().GetField(
                       name,
                       BindingFlags.Instance | BindingFlags.NonPublic)
                   ?? throw new InvalidOperationException(
                       $"Private field '{name}' was not found on {instance.GetType().FullName}."))
                .GetValue(instance);

        private static T RunOnCallbackThread<T>(Func<T> action, string timeoutMessage)
        {
            T result = default;
            Exception failure = null;
            var completed = new ManualResetEventSlim(false);
            var thread = new Thread(
                () =>
                {
                    try
                    {
                        result = action();
                    }
                    catch (Exception exception)
                    {
                        failure = exception;
                    }
                    finally
                    {
                        completed.Set();
                    }
                })
            {
                IsBackground = true,
            };

            thread.Start();
            var signaled = completed.Wait(TimeSpan.FromSeconds(2));
            var joined = thread.Join(TimeSpan.FromSeconds(1));
            Assert.True(signaled, timeoutMessage);
            Assert.True(joined, timeoutMessage + " The callback thread did not exit.");
            Assert.Null(failure);
            return result;
        }
    }
}
#endif
