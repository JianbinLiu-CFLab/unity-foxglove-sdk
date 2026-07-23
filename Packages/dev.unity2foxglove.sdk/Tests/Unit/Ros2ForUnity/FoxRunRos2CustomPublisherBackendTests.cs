// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Ros2ForUnity
// Purpose: Locks closed-generic custom native publisher backend ownership.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using System.Collections.Generic;
using System.Reflection;
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
            var owner = new Ros2ForUnityFoxRunNodeOwner(driver);
            var publisher = owner.AcquirePublisherBackend();
            var subscriber = owner.AcquireBackend();

            var registration = publisher.Register<TestEnvelope>(Contract());

            Assert.True(registration.Succeeded);
            Assert.True(publisher.TryPublish(registration.Token, new TestEnvelope()));
            Assert.Equal(1, driver.CreatePublisherCount);
            Assert.Equal(1, driver.PublishCount);

            publisher.RemovePublisher(registration.Token);
            publisher.ReleaseNodeOwnership();
            owner.ReleaseHostOwnership();
            Assert.Equal(0, driver.ReleaseNodeCount);

            subscriber.ReleaseNodeOwnership();
            Assert.Equal(1, driver.ReleaseNodeCount);
        }

        [Fact]
        public void PublisherBackendRefusesLatePublishWhenNativeRuntimeCloses()
        {
            var driver = new FakeNodeDriver();
            var nativeRuntimeAvailable = true;
            var owner = new Ros2ForUnityFoxRunNodeOwner(
                driver,
                () => nativeRuntimeAvailable);
            var publisher = owner.AcquirePublisherBackend();
            var registration = publisher.Register<TestEnvelope>(Contract());

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
            var owner = new Ros2ForUnityFoxRunNodeOwner(driver);
            var publisher = owner.AcquirePublisherBackend();

            var registration = publisher.Register<TestEnvelope>(Contract());

            Assert.False(registration.Succeeded);
            Assert.Equal(FoxRunRos2RegistrationError.InvalidPublisherToken, registration.Error);
            Assert.Equal(1, driver.RemovePublisherCount);
            publisher.ReleaseNodeOwnership();
            owner.ReleaseHostOwnership();
            Assert.Equal(1, driver.ReleaseNodeCount);
        }

        [Fact]
        public void PublisherFailureUsesTheInnermostExceptionClassWithoutLeakingItsMessage()
        {
            var driver = new FakeNodeDriver
            {
                PublisherFailure = new TargetInvocationException(
                    new DllNotFoundException("ros2-native-path=phase181-secret"))
            };
            var owner = new Ros2ForUnityFoxRunNodeOwner(driver);
            var publisher = owner.AcquirePublisherBackend();

            var registration = publisher.Register<TestEnvelope>(Contract());

            Assert.False(registration.Succeeded);
            Assert.Equal(FoxRunRos2RegistrationError.PublisherBackendFailure, registration.Error);
            Assert.Equal("DllNotFoundException", registration.FailureKind);
            Assert.DoesNotContain("phase181-secret", registration.Diagnostic, StringComparison.Ordinal);
            Assert.DoesNotContain("phase181-secret", registration.FailureKind, StringComparison.Ordinal);
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
                FoxRunFlow.Publish);

        private sealed class TestEnvelope : ROS2.Message, IDisposable
        {
            public bool IsDisposed { get; private set; }

            public void Dispose() => IsDisposed = true;
        }

        private sealed class FakeNodeDriver : IFoxRunRos2R2fuNodeDriver
        {
            public int CreatePublisherCount { get; private set; }
            public int RemovePublisherCount { get; private set; }
            public int PublishCount { get; private set; }
            public int ReleaseNodeCount { get; private set; }
            public bool PublisherUsable { get; set; } = true;
            public Exception PublisherFailure { get; set; }

            public object CreateSubscription<T>(string topic, Action<T> callback, ROS2.QualityOfServiceProfile qos)
                where T : ROS2.Message, new()
                => new object();

            public bool IsSubscriptionUsable(object subscription) => subscription != null;
            public bool RemoveSubscription(object subscription) => true;

            public object CreatePublisher<T>(string topic)
                where T : ROS2.Message, new()
            {
                CreatePublisherCount++;
                if (PublisherFailure != null)
                    throw PublisherFailure;
                return new object();
            }

            public bool IsPublisherUsable<T>(object publisher)
                where T : ROS2.Message, new()
                => PublisherUsable && publisher != null;

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
                return true;
            }

            public void ReleaseNode() => ReleaseNodeCount++;
        }
    }
}
#endif
