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
    public sealed partial class FoxRunRos2CustomPublisherBackendTests
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
    }
}
#endif
