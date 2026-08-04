// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Ros2ForUnity
// Purpose: Locks closed-generic custom native publisher backend ownership.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
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
        public void PublisherFailureUsesTheInnermostExceptionClassWithoutLeakingItsMessage()
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

            var registration = publisher.Register<TestEnvelope>(Contract(), FoxRunResolvedQos.Default);

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

        [Fact]
        public async Task CustomTransportLeaseTrackerReservesOwnerBeforeBackendAcquisition()
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

            Assert.Same(
                acquisition,
                await Task.WhenAny(
                    acquisition,
                    Task.Delay(TimeSpan.FromSeconds(5))));
            await acquisition;
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
            public int RemovePublisherCount { get; private set; }
            public int PublishCount { get; private set; }
            public int ReleaseNodeCount { get; private set; }
            public bool PublisherUsable { get; set; } = true;
            public Exception PublisherFailure { get; set; }
            public ROS2.QualityOfServiceProfile LastPublisherQos { get; private set; }

            public object CreateSubscription<T>(string topic, Action<T> callback, ROS2.QualityOfServiceProfile qos)
                where T : ROS2.Message, new()
                => new object();

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
