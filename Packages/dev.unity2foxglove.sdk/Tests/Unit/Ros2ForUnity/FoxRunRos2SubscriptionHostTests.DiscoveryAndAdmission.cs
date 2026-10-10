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
    public sealed partial class FoxRunRos2SubscriptionHostTests
    {
        [Fact]
        public void ApplyRateGateDrainsAtMostOneValuePerCapturedPeriod()
        {
            var gate = new FoxRunRos2ApplyRateGate(10);

            Assert.True(gate.TryAcquire(100.0));
            Assert.False(gate.TryAcquire(100.01));
            Assert.False(gate.TryAcquire(100.099));
            Assert.True(gate.TryAcquire(100.1));
            Assert.False(gate.TryAcquire(100.1));

            var emptyGate = new FoxRunRos2ApplyRateGate(10);
            Assert.False(emptyGate.TryExecute(200.0, () => false));
            Assert.True(emptyGate.TryExecute(200.0, () => true));
            Assert.False(emptyGate.TryExecute(200.01, () => true));
        }

        [Fact]
        public void DiscoveryKeysSortByTypeInstanceTopicAndMember()
        {
            var keys = new List<FoxRunRos2DiscoveryKey>
            {
                new FoxRunRos2DiscoveryKey("B.Type", 1, "/a", "a"),
                new FoxRunRos2DiscoveryKey("A.Type", 2, "/a", "a"),
                new FoxRunRos2DiscoveryKey("A.Type", 1, "/b", "a"),
                new FoxRunRos2DiscoveryKey("A.Type", 1, "/a", "b"),
                new FoxRunRos2DiscoveryKey("A.Type", 1, "/a", "a")
            };

            keys.Sort();

            Assert.Equal(
                new[] { "A.Type|1|/a|a", "A.Type|1|/a|b", "A.Type|1|/b|a", "A.Type|2|/a|a", "B.Type|1|/a|a" },
                keys.ConvertAll(key => key.ToString()));
        }

        [Fact]
        public void NativeOnlyGeneratedSourceIsDiscoverableWithoutWebSocketInputInterface()
        {
            var source = new NativeOnlySource { isActiveAndEnabled = true };

            Assert.True(FoxRunRos2SourceDiscovery.TryGet(source, out var discovered));
            Assert.Same(source, discovered);
            Assert.False((object)source is Unity.FoxgloveSDK.Components.IFoxgloveInputSource);
        }

        [Fact]
        public void CustomNativeOnlyGeneratedSourceUsesTheExistingSubscriptionRegistrarDiscovery()
        {
            var source = new CustomNativeOnlySource { isActiveAndEnabled = true };

            Assert.True(FoxRunRos2SourceDiscovery.TryGetCustom(source, out var discovered));
            Assert.Same(source, discovered);
            Assert.False((object)source is IFoxRunRos2SubscriptionSource);
        }

        [Fact]
        public void ProductionBackendBorrowsOneQosAndSharedNodeReleaseIsUnique()
        {
            var driver = new FakeR2fuNodeDriver();
            var owner = new Ros2ForUnityFoxRunNodeOwner(driver);
            var first = owner.AcquireBackend();
            var second = owner.AcquireBackend();
            var qos = new HostManagedQosProfile();

            var firstResult = first.Register<FakeHostMessage>(
                Contract("ros2-native", "default"),
                qos,
                _ => { });
            var secondResult = second.Register<FakeHostMessage>(
                Contract("ros2-native", "default"),
                qos,
                _ => { });

            Assert.True(firstResult.Succeeded);
            Assert.True(secondResult.Succeeded);
            Assert.Equal(2, driver.CreateCount);
            Assert.All(driver.SeenQos, seen => Assert.Same(qos.NativeProfile, seen));
            first.RemoveSubscription(firstResult.Token);
            Assert.Equal(1, driver.RemoveCount);

            first.ReleaseNodeOwnership();
            first.ReleaseNodeOwnership();
            owner.ReleaseHostOwnership();
            Assert.Equal(0, driver.ReleaseNodeCount);
            second.ReleaseNodeOwnership();
            second.ReleaseNodeOwnership();
            Assert.Equal(1, driver.ReleaseNodeCount);
        }

        [Fact]
        public void MigratedDefaultFixtureKeepsThePortableDefaultProfile()
        {
            var contract = Contract("ros2-native", "default");

            Assert.True(contract.HasExplicitQosProfile);
            Assert.Equal(
                FoxRunQosProfile.Default,
                contract.QosProfile);
        }

        [Fact]
        public void SubscriptionAdmissionChoosesTheSameCanonicalTopKForEveryDiscoveryPermutation()
        {
            var first = new List<string>
            {
                "source-d",
                "source-a",
                "source-c",
                "source-b",
            };
            var second = new List<string>
            {
                "source-b",
                "source-c",
                "source-a",
                "source-d",
            };

            Assert.Equal(
                1,
                FoxRunRos2SubscriptionAdmission.RetainDeterministicPrefix(
                    first,
                    3,
                    StringComparer.Ordinal.Compare));
            Assert.Equal(
                1,
                FoxRunRos2SubscriptionAdmission.RetainDeterministicPrefix(
                    second,
                    3,
                    StringComparer.Ordinal.Compare));
            Assert.Equal(
                new[] { "source-a", "source-b", "source-c" },
                first);
            Assert.Equal(first, second);
        }

        [Fact]
        public void ProductionBackendRechecksLifecycleAdmissionImmediatelyBeforeSubscriptionCreation()
        {
            var driver = new FakeR2fuNodeDriver();
            var lifecycleReady = false;
            var owner = new Ros2ForUnityFoxRunNodeOwner(driver, () => lifecycleReady);
            var backend = owner.AcquireBackend();
            var qos = new HostManagedQosProfile();

            var denied = backend.Register<FakeHostMessage>(
                Contract("ros2-native", "default"),
                qos,
                _ => { });

            Assert.False(denied.Succeeded);
            Assert.Equal(FoxRunRos2RegistrationError.RuntimeUnavailable, denied.Error);
            Assert.Equal(0, driver.CreateCount);

            lifecycleReady = true;
            var accepted = backend.Register<FakeHostMessage>(
                Contract("ros2-native", "default"),
                qos,
                _ => { });
            Assert.True(accepted.Succeeded);
            Assert.Equal(1, driver.CreateCount);

            backend.RemoveSubscription(accepted.Token);
            backend.ReleaseNodeOwnership();
            owner.ReleaseHostOwnership();
        }

        [Fact]
        public void ProductionBackendRetainsSubscriptionWhenRemovalReturnsFalse()
        {
            var driver = new FakeR2fuNodeDriver { RemoveReturns = false };
            var owner = new Ros2ForUnityFoxRunNodeOwner(driver);
            var backend = owner.AcquireBackend();
            var registration = backend.Register<FakeHostMessage>(
                Contract("ros2-native", "default"),
                new HostManagedQosProfile(),
                _ => { });
            Assert.True(registration.Succeeded);

            var removalFailure = Assert.Throws<InvalidOperationException>(
                () => backend.RemoveSubscription(registration.Token));
            Assert.Equal("R2FU subscription was not found during removal.", removalFailure.Message);
            Assert.Equal(1, driver.RemoveCount);

            driver.RemoveReturns = true;
            backend.RemoveSubscription(registration.Token);

            Assert.Equal(2, driver.RemoveCount);
            Assert.True(backend.ReleaseNodeOwnership());
            Assert.True(owner.ReleaseHostOwnership());
            Assert.Equal(1, driver.ReleaseNodeCount);
        }

        [Fact]
        public void ProductionNodeOwnerRetriesSharedNodeReleaseAfterRecoverableFailure()
        {
            var driver = new FakeR2fuNodeDriver { ReleaseFailuresRemaining = 1 };
            var owner = new Ros2ForUnityFoxRunNodeOwner(driver);

            Assert.False(owner.ReleaseHostOwnership());
            Assert.Equal(1, driver.ReleaseNodeCount);

            Assert.True(owner.ReleaseHostOwnership());
            Assert.Equal(2, driver.ReleaseNodeCount);
        }

        [Fact]
        public void DiagnosticsKeepContractsIsolatedAndDebounceIdenticalFailures()
        {
            var diagnostics = new FoxRunRos2SubscriptionDiagnostics();
            var ready = Snapshot("ready", FoxRunRos2SubscriptionBindingState.Ready, FoxRunRos2RegistrationError.None, string.Empty);
            var failed = Snapshot("failed", FoxRunRos2SubscriptionBindingState.Failed, FoxRunRos2RegistrationError.BackendFailure, "boom");

            diagnostics.Update("source:11|ready", ready);
            diagnostics.Update("source:12|failed", failed);

            Assert.Equal(2, diagnostics.Count);
            Assert.True(diagnostics.TryGet("source:11|ready", out var readyResult));
            Assert.Equal(FoxRunRos2SubscriptionBindingState.Ready, readyResult.State);
            Assert.True(diagnostics.ShouldLog("source:12|failed", failed));
            Assert.False(diagnostics.ShouldLog("source:12|failed", failed));
            Assert.False(diagnostics.ShouldLog("source:99|failed", failed));
            var sameCodeDifferentMessage = Snapshot(
                "failed",
                FoxRunRos2SubscriptionBindingState.Failed,
                FoxRunRos2RegistrationError.BackendFailure,
                "backend was retried");
            Assert.False(diagnostics.ShouldLog("source:12|failed", sameCodeDifferentMessage));

            var healthy = Snapshot(
                "failed",
                FoxRunRos2SubscriptionBindingState.Ready,
                FoxRunRos2RegistrationError.None,
                string.Empty);
            diagnostics.Update("source:12|failed", healthy);
            Assert.False(diagnostics.ShouldLog("source:12|failed", healthy));
            diagnostics.Update("source:12|failed", failed);
            Assert.True(diagnostics.ShouldLog("source:12|failed", failed));
        }

    }
}
#endif
