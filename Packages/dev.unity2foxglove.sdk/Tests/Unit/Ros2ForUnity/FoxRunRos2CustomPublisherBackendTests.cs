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
    [Trait("Phase", "181-D")]
    [Trait("Domain", "CustomNativePublisher")]
    public sealed class FoxRunRos2CustomPublisherBackendTests
    {
        [Fact]
        public void PublisherTokenPublishesThenReleasesTheSharedNodeLease()
        {
            var driver = new FakeNodeDriver();
            var qosFactory = new ManagedQosFactory();
            var owner = new Ros2ForUnityFoxRunNodeOwner(driver, () => true, qosFactory);
            var publisher = owner.AcquirePublisherBackend();
            var subscriber = owner.AcquireBackend();

            var registration = publisher.Register<TestEnvelope>(Contract(), FoxRunResolvedQos.Default);

            Assert.True(registration.Succeeded);
            Assert.True(publisher.TryPublish(registration.Token, new TestEnvelope()));
            Assert.Equal(1, driver.CreatePublisherCount);
            Assert.Equal(1, driver.PublishCount);
            var mapped = Assert.Single(qosFactory.Created);
            Assert.Equal(ROS2.HistoryPolicy.QOS_POLICY_HISTORY_KEEP_LAST, mapped.History);
            Assert.Equal(10, mapped.Depth);
            Assert.Equal(ROS2.ReliabilityPolicy.QOS_POLICY_RELIABILITY_RELIABLE, mapped.Reliability);
            Assert.Equal(ROS2.DurabilityPolicy.QOS_POLICY_DURABILITY_VOLATILE, mapped.Durability);
            Assert.True(mapped.IsDisposed);

            publisher.RemovePublisher(registration.Token);
            publisher.ReleaseNodeOwnership();
            owner.ReleaseHostOwnership();
            Assert.Equal(0, driver.ReleaseNodeCount);

            subscriber.ReleaseNodeOwnership();
            Assert.Equal(1, driver.ReleaseNodeCount);
        }

        [Fact]
        public void PublisherReportsMiddlewareAcceptanceWhenNoSubscriberIsPresent()
        {
            var driver = new FakeNodeDriver();
            var owner = new Ros2ForUnityFoxRunNodeOwner(driver, () => true, new ManagedQosFactory());
            var publisher = owner.AcquirePublisherBackend();
            var registration = publisher.Register<TestEnvelope>(Contract(), FoxRunResolvedQos.Default);

            Assert.True(registration.Succeeded);
            Assert.True(publisher.TryPublish(registration.Token, new TestEnvelope()));
            Assert.Equal(1, driver.PublishCount);
            Assert.Equal(0, driver.CreateSubscriptionCount);

            publisher.RemovePublisher(registration.Token);
            Assert.True(publisher.ReleaseNodeOwnership());
            Assert.True(owner.ReleaseHostOwnership());
            Assert.Equal(1, driver.ReleaseNodeCount);
        }

        [Fact]
        public void PublisherBackendRefusesLatePublishWhenNativeRuntimeCloses()
        {
            var driver = new FakeNodeDriver();
            var nativeRuntimeAvailable = true;
            var owner = new Ros2ForUnityFoxRunNodeOwner(
                driver,
                () => nativeRuntimeAvailable,
                new ManagedQosFactory());
            var publisher = owner.AcquirePublisherBackend();
            var registration = publisher.Register<TestEnvelope>(Contract(), FoxRunResolvedQos.Default);

            nativeRuntimeAvailable = false;

            Assert.False(publisher.TryPublish(registration.Token, new TestEnvelope()));
            Assert.Equal(0, driver.PublishCount);

            publisher.RemovePublisher(registration.Token);
            publisher.ReleaseNodeOwnership();
            owner.ReleaseHostOwnership();
            Assert.Equal(1, driver.ReleaseNodeCount);
        }

        [Fact]
        public void InvalidPublisherTokenRollsBackTheEndpointAndFailsClosed()
        {
            var driver = new FakeNodeDriver { PublisherUsable = false };
            var owner = new Ros2ForUnityFoxRunNodeOwner(
                driver,
                () => true,
                new ManagedQosFactory());
            var publisher = owner.AcquirePublisherBackend();

            var registration = publisher.Register<TestEnvelope>(Contract(), FoxRunResolvedQos.Default);

            Assert.False(registration.Succeeded);
            Assert.Equal(FoxRunRos2RegistrationError.InvalidPublisherToken, registration.Error);
            Assert.Equal(1, driver.RemovePublisherCount);
            publisher.ReleaseNodeOwnership();
            owner.ReleaseHostOwnership();
            Assert.Equal(1, driver.ReleaseNodeCount);
        }

        [Fact]
        public void PublisherTokenRetainsEndpointWhenRemovalThrows()
        {
            var driver = new FakeNodeDriver { PublisherRemovalExceptionsRemaining = 1 };
            var owner = new Ros2ForUnityFoxRunNodeOwner(
                driver,
                () => true,
                new ManagedQosFactory());
            var publisher = owner.AcquirePublisherBackend();

            var registration = publisher.Register<TestEnvelope>(
                Contract(),
                FoxRunResolvedQos.Default);

            Assert.True(registration.Succeeded);
            var removalFailure = Assert.Throws<InvalidOperationException>(
                () => publisher.RemovePublisher(registration.Token));
            Assert.Equal("publisher removal pending", removalFailure.Message);
            Assert.Equal(1, driver.RemovePublisherCount);

            publisher.RemovePublisher(registration.Token);

            Assert.Equal(2, driver.RemovePublisherCount);
            Assert.True(publisher.ReleaseNodeOwnership());
            Assert.True(owner.ReleaseHostOwnership());
            Assert.Equal(1, driver.ReleaseNodeCount);
        }

        [Fact]
        public void PublisherTokenRetainsEndpointWhenRemovalReturnsFalse()
        {
            var driver = new FakeNodeDriver { PublisherRemovalReturnsFalse = true };
            var owner = new Ros2ForUnityFoxRunNodeOwner(driver, () => true, new ManagedQosFactory());
            var publisher = owner.AcquirePublisherBackend();
            var registration = publisher.Register<TestEnvelope>(Contract(), FoxRunResolvedQos.Default);

            Assert.True(registration.Succeeded);
            var removalFailure = Assert.Throws<InvalidOperationException>(
                () => publisher.RemovePublisher(registration.Token));
            Assert.Equal("R2FU publisher was not found during removal.", removalFailure.Message);
            Assert.Equal(1, driver.RemovePublisherCount);

            driver.PublisherRemovalReturnsFalse = false;
            publisher.RemovePublisher(registration.Token);

            Assert.Equal(2, driver.RemovePublisherCount);
            Assert.True(publisher.ReleaseNodeOwnership());
            Assert.True(owner.ReleaseHostOwnership());
            Assert.Equal(1, driver.ReleaseNodeCount);
        }

        [Fact]
        public void QosDisposeFailureRollsBackTheCreatedPublisherExactlyOnce()
        {
            var driver = new FakeNodeDriver();
            var qosFactory = new ManagedQosFactory
            {
                DisposeFailure = new InvalidOperationException("qos dispose failed"),
            };
            var owner = new Ros2ForUnityFoxRunNodeOwner(driver, () => true, qosFactory);
            var publisher = owner.AcquirePublisherBackend();

            var registration = publisher.Register<TestEnvelope>(
                Contract(),
                FoxRunResolvedQos.Default);

            Assert.False(registration.Succeeded);
            Assert.Equal(
                FoxRunRos2RegistrationError.PublisherBackendFailure,
                registration.Error);
            Assert.Equal(1, driver.CreatePublisherCount);
            Assert.Equal(1, driver.RemovePublisherCount);
            Assert.True(Assert.Single(qosFactory.Created).IsDisposed);
            publisher.ReleaseNodeOwnership();
            owner.ReleaseHostOwnership();
            Assert.Equal(1, driver.ReleaseNodeCount);
        }

        [Fact]
        public void NativeBinaryLoadFailureIsolatedToThePublisherContract()
        {
            var driver = new FakeNodeDriver
            {
                PublisherFailure = new TargetInvocationException(
                    new DllNotFoundException("ros2-native-path=phase181-secret"))
            };
            var owner = new Ros2ForUnityFoxRunNodeOwner(
                driver,
                () => true,
                new ManagedQosFactory());
            var publisher = owner.AcquirePublisherBackend();

            var registration = publisher.Register<TestEnvelope>(
                Contract(),
                FoxRunResolvedQos.Default);

            Assert.False(registration.Succeeded);
            Assert.Equal(
                FoxRunRos2RegistrationError.NativeRuntimeSurfaceUnavailable,
                registration.Error);
            Assert.Equal("DllNotFoundException", registration.FailureKind);
            Assert.Equal(0, driver.RemovePublisherCount);
            publisher.ReleaseNodeOwnership();
            owner.ReleaseHostOwnership();
        }

        [Fact]
        public void FatalPublisherInspectionPreservesPrimaryFailureAndRollsBackTheEndpoint()
        {
            var driver = new FakeNodeDriver
            {
                PublisherUsabilityFailure = new OutOfMemoryException("publisher-inspection")
            };
            var owner = new Ros2ForUnityFoxRunNodeOwner(
                driver,
                () => true,
                new ManagedQosFactory());
            var publisher = owner.AcquirePublisherBackend();

            var thrown = Assert.Throws<OutOfMemoryException>(
                () => publisher.Register<TestEnvelope>(Contract(), FoxRunResolvedQos.Default));

            Assert.Equal("publisher-inspection", thrown.Message);
            Assert.Equal(1, driver.CreatePublisherCount);
            Assert.Equal(1, driver.RemovePublisherCount);
            publisher.ReleaseNodeOwnership();
            owner.ReleaseHostOwnership();
        }

        [Fact]
        public void CustomTransportLeaseTrackerSharesOneNodeAcrossInputAndOutputUntilTheLastLeaseStops()
        {
            var driver = new FakeNodeDriver();
            var createdOwners = 0;
            var tracker = new FoxRunRos2CustomNativeTransportLeaseTracker(
                () =>
                {
                    createdOwners++;
                    return new Ros2ForUnityFoxRunNodeOwner(driver);
                });

            Assert.Equal(0, createdOwners);
            Assert.Equal(0, driver.ReleaseNodeCount);

            Assert.True(tracker.TryAcquireSubscriptionBackend(out var subscription));
            Assert.True(tracker.TryAcquirePublisherBackend(out var publisher));
            Assert.Equal(1, createdOwners);

            subscription.ReleaseNodeOwnership();
            Assert.Equal(0, driver.ReleaseNodeCount);

            publisher.ReleaseNodeOwnership();
            Assert.Equal(1, driver.ReleaseNodeCount);

            Assert.True(tracker.TryAcquirePublisherBackend(out var nextPublisher));
            Assert.Equal(2, createdOwners);
            nextPublisher.ReleaseNodeOwnership();
            Assert.Equal(2, driver.ReleaseNodeCount);
        }

        [Fact]
        public void CustomTransportLeaseRetriesHostReleaseAfterRecoverableFailure()
        {
            var driver = new FakeNodeDriver { ReleaseFailuresRemaining = 1 };
            var tracker = new FoxRunRos2CustomNativeTransportLeaseTracker(
                () => new Ros2ForUnityFoxRunNodeOwner(driver));

            Assert.True(tracker.TryAcquirePublisherBackend(out var publisher));
            Assert.True(publisher.ReleaseNodeOwnership());
            Assert.Equal(1, driver.ReleaseNodeCount);

            Assert.True(tracker.RetryPendingRelease());
            Assert.Equal(2, driver.ReleaseNodeCount);
        }

        [Fact]
        public void CallbackThreadHostReleaseIsDispatchedToTheOwnerThread()
        {
            var ownerThreadId = Thread.CurrentThread.ManagedThreadId;
            var context = new QueuedSynchronizationContext();
            var driver = new FakeNodeDriver();
            var owner = new Ros2ForUnityFoxRunNodeOwner(
                driver,
                () => true,
                new ManagedQosFactory(),
                ownerThreadId,
                context);
            var publisher = owner.AcquirePublisherBackend();

            var releaseResult = RunOnCallbackThread(
                () =>
                {
                    Assert.True(publisher.ReleaseNodeOwnership());
                    return owner.ReleaseHostOwnership();
                },
                "The callback-thread host release did not complete within the timeout.");
            Assert.False(releaseResult);
            Assert.Equal(0, driver.ReleaseNodeCount);
            Assert.Equal(1, context.PendingCount);

            context.RunPending();

            Assert.Equal(1, driver.ReleaseNodeCount);
            Assert.Equal(ownerThreadId, driver.ReleaseThreadId);
            Assert.True(owner.ReleaseHostOwnership());
        }

        [Fact]
        public void CallbackThreadBindingReleaseDoesNotLeakAfterHostRelease()
        {
            var ownerThreadId = Thread.CurrentThread.ManagedThreadId;
            var context = new QueuedSynchronizationContext();
            var driver = new FakeNodeDriver();
            var owner = new Ros2ForUnityFoxRunNodeOwner(
                driver,
                () => true,
                new ManagedQosFactory(),
                ownerThreadId,
                context);
            var publisher = owner.AcquirePublisherBackend();

            Assert.True(owner.ReleaseHostOwnership());
            Assert.True(
                RunOnCallbackThread(
                    () => publisher.ReleaseNodeOwnership(),
                    "The callback-thread binding release did not complete within the timeout."));
            Assert.Equal(0, driver.ReleaseNodeCount);
            Assert.Equal(1, context.PendingCount);

            context.RunPending();

            Assert.Equal(1, driver.ReleaseNodeCount);
            Assert.Equal(ownerThreadId, driver.ReleaseThreadId);
        }

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

        private static FoxRunRos2CustomPublisherContract Contract()
            => new FoxRunRos2CustomPublisherContract(
                "publisher-contract",
                "/phase181/outbound",
                "Phase181.Source",
                "State",
                "unity2foxglove_foxrun_interfaces_v1/msg/Phase181State48D288ED82F1",
                "unity2foxglove_foxrun_interfaces_v1/msg/Phase181State48D288ED82F1Envelope",
                "dev.unity2foxglove.foxrun.ros2.interfaces",
                "unity2foxglove_foxrun_interfaces_v1",
                1,
                "120864853239fae290b5199cd02dbf02f107299bccd8972b06d8cf59fc7594fd",
                "dev.unity2foxglove.ros2forunity.runtime.jazzy.win64",
                FoxRunFlow.Publish,
                FoxRunQosProfile.Default,
                hasExplicitQosProfile: true,
                qosReliability: default,
                hasExplicitQosReliability: false,
                qosDurability: default,
                hasExplicitQosDurability: false,
                qosHistory: default,
                hasExplicitQosHistory: false,
                qosDepth: 0,
                hasExplicitQosDepth: false);

        private sealed class TestEnvelope : ROS2.Message, IDisposable
        {
            public bool IsDisposed { get; private set; }

            public void Dispose() => IsDisposed = true;
        }

        private sealed class FakeNodeDriver : IFoxRunRos2R2fuNodeDriver
        {
            public int CreatePublisherCount { get; private set; }
            public int CreateSubscriptionCount { get; private set; }
            public int RemovePublisherCount { get; private set; }
            public int PublishCount { get; private set; }
            public int ReleaseNodeCount { get; private set; }
            public int ReleaseThreadId { get; private set; }
            public bool PublisherUsable { get; set; } = true;
            public int PublisherRemovalExceptionsRemaining { get; set; }
            public bool PublisherRemovalReturnsFalse { get; set; }
            public int ReleaseFailuresRemaining { get; set; }
            public int ReleaseExceptionsRemaining { get; set; }
            public bool BlockRelease { get; set; }
            public ManualResetEventSlim ReleaseStarted { get; } = new ManualResetEventSlim(false);
            public ManualResetEventSlim ContinueRelease { get; } = new ManualResetEventSlim(false);
            public Exception PublisherFailure { get; set; }
            public Exception PublisherUsabilityFailure { get; set; }
            public ROS2.QualityOfServiceProfile LastPublisherQos { get; private set; }

            public object CreateSubscription<T>(string topic, Action<T> callback, ROS2.QualityOfServiceProfile qos)
                where T : ROS2.Message, new()
            {
                CreateSubscriptionCount++;
                return new object();
            }

            public bool IsSubscriptionUsable(object subscription) => subscription != null;
            public bool RemoveSubscription(object subscription) => true;

            public object CreatePublisher<T>(string topic, ROS2.QualityOfServiceProfile qos)
                where T : ROS2.Message, new()
            {
                CreatePublisherCount++;
                LastPublisherQos = qos;
                if (PublisherFailure != null)
                    throw PublisherFailure;
                return new object();
            }

            public bool IsPublisherUsable<T>(object publisher)
                where T : ROS2.Message, new()
            {
                if (PublisherUsabilityFailure != null)
                    throw PublisherUsabilityFailure;
                return PublisherUsable && publisher != null;
            }

            public bool Publish<T>(object publisher, T message)
                where T : ROS2.Message, new()
            {
                if (publisher == null || message == null)
                    return false;
                PublishCount++;
                return true;
            }

            public bool RemovePublisher<T>(object publisher)
                where T : ROS2.Message, new()
            {
                if (publisher == null)
                    return false;
                RemovePublisherCount++;
                if (PublisherRemovalExceptionsRemaining > 0)
                {
                    PublisherRemovalExceptionsRemaining--;
                    throw new InvalidOperationException("publisher removal pending");
                }
                return !PublisherRemovalReturnsFalse;
            }

            public bool ReleaseNode()
            {
                ReleaseNodeCount++;
                ReleaseThreadId = Thread.CurrentThread.ManagedThreadId;
                if (BlockRelease)
                {
                    ReleaseStarted.Set();
                    if (!ContinueRelease.Wait(TimeSpan.FromSeconds(5)))
                        return false;
                    BlockRelease = false;
                }
                if (ReleaseFailuresRemaining > 0)
                {
                    ReleaseFailuresRemaining--;
                    return false;
                }
                if (ReleaseExceptionsRemaining > 0)
                {
                    ReleaseExceptionsRemaining--;
                    throw new InvalidOperationException("node release pending");
                }
                return true;
            }
        }

        private sealed class QueuedSynchronizationContext : SynchronizationContext
        {
            private readonly ConcurrentQueue<(SendOrPostCallback Callback, object State)> _pending =
                new ConcurrentQueue<(SendOrPostCallback Callback, object State)>();

            public int PendingCount => _pending.Count;

            public override void Post(SendOrPostCallback callback, object state)
                => _pending.Enqueue((callback, state));

            public void RunPending()
            {
                while (_pending.TryDequeue(out var work))
                    work.Callback(work.State);
            }
        }

        private sealed class ManagedQosFactory : IFoxRunRos2NativeQosProfileFactory
        {
            public List<ManagedQosProfile> Created { get; } = new List<ManagedQosProfile>();
            public Exception DisposeFailure { get; set; }

            public IFoxRunRos2NativeQosProfile Create(ROS2.QosPresetProfile preset)
            {
                var profile = new ManagedQosProfile(DisposeFailure);
                Created.Add(profile);
                return profile;
            }
        }

        private sealed class ManagedQosProfile : IFoxRunRos2NativeQosProfile
        {
            private readonly Exception _disposeFailure;

            internal ManagedQosProfile(Exception disposeFailure = null)
            {
                _disposeFailure = disposeFailure;
            }

            public ROS2.QualityOfServiceProfile NativeProfile => null;
            public ROS2.HistoryPolicy History { get; private set; }
            public int Depth { get; private set; }
            public ROS2.ReliabilityPolicy Reliability { get; private set; }
            public ROS2.DurabilityPolicy Durability { get; private set; }
            public bool IsDisposed { get; private set; }

            public void SetHistory(ROS2.HistoryPolicy history, int depth)
            {
                History = history;
                Depth = depth;
            }

            public void SetPolicies(
                ROS2.HistoryPolicy history,
                int depth,
                ROS2.ReliabilityPolicy reliability,
                ROS2.DurabilityPolicy durability)
            {
                History = history;
                Depth = depth;
                Reliability = reliability;
                Durability = durability;
            }

            public void Dispose()
            {
                IsDisposed = true;
                if (_disposeFailure != null)
                    throw _disposeFailure;
            }
        }
    }
}
#endif
