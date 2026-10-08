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

        [Fact]
        public void DiagnosticsDoNotRelogFailureWhenHealthySiblingSharesContract()
        {
            var diagnostics = new FoxRunRos2SubscriptionDiagnostics();
            var failed = Snapshot(
                "shared-contract",
                FoxRunRos2SubscriptionBindingState.Failed,
                FoxRunRos2RegistrationError.BackendFailure,
                "backend failed");
            var healthy = Snapshot(
                "shared-contract",
                FoxRunRos2SubscriptionBindingState.Ready,
                FoxRunRos2RegistrationError.None,
                string.Empty);

            diagnostics.Update("source:1|shared-contract", failed);
            diagnostics.Update("source:2|shared-contract", healthy);
            Assert.True(diagnostics.ShouldLog("source:1|shared-contract", failed));

            // A normal sibling must not clear a still-active contract/error signature.
            diagnostics.Update("source:2|shared-contract", healthy);
            Assert.False(diagnostics.ShouldLog("source:2|shared-contract", healthy));
            diagnostics.Update("source:1|shared-contract", failed);
            Assert.False(diagnostics.ShouldLog("source:1|shared-contract", failed));

            // Once the last failed sibling recovers, the next recurrence is reportable.
            diagnostics.Update("source:1|shared-contract", healthy);
            Assert.False(diagnostics.ShouldLog("source:1|shared-contract", healthy));
            diagnostics.Update("source:1|shared-contract", failed);
            Assert.True(diagnostics.ShouldLog("source:1|shared-contract", failed));
        }

        [Fact]
        public void DiagnosticsSeparateIdenticalContractsByRuntimeEndpointIdentity()
        {
            var diagnostics = new FoxRunRos2SubscriptionDiagnostics();
            var first = Snapshot("stable-contract", FoxRunRos2SubscriptionBindingState.Ready,
                FoxRunRos2RegistrationError.None, string.Empty, received: 3);
            var second = Snapshot("stable-contract", FoxRunRos2SubscriptionBindingState.Failed,
                FoxRunRos2RegistrationError.BackendFailure, "second failed", received: 9);

            diagnostics.Update("source:101|stable-contract", first);
            diagnostics.Update("source:202|stable-contract", second);

            Assert.Equal(2, diagnostics.Count);
            Assert.True(diagnostics.TryGet("source:101|stable-contract", out var firstResult));
            Assert.True(diagnostics.TryGet("source:202|stable-contract", out var secondResult));
            Assert.Equal("stable-contract", firstResult.ContractId);
            Assert.Equal(3, firstResult.Received);
            Assert.Equal(9, secondResult.Received);

            diagnostics.RemoveExcept(new HashSet<string>(StringComparer.Ordinal)
            {
                "source:202|stable-contract"
            });
            Assert.False(diagnostics.TryGet("source:101|stable-contract", out _));
            Assert.True(diagnostics.TryGet("source:202|stable-contract", out secondResult));
            Assert.Equal(9, secondResult.Received);
        }

        [Fact]
        public void RuntimeDiagnosticsExposeSortedBoundedContractAndTransportSnapshots()
        {
            var diagnostics = new FoxRunRos2SubscriptionDiagnostics();
            var zeta = new FoxRunRos2GeneratedContract(
                "zeta", "/zeta", "Demo.Zeta", "_incoming", "std_msgs/msg/String",
                Unity.FoxgloveSDK.Components.FoxRunFlow.Subscribe,
                FoxRunRos2RouteEndpoint.R2fu,
                FoxRunQosProfile.Default,
                hasExplicitQosProfile: true,
                qosReliability: default,
                hasExplicitQosReliability: false,
                qosDurability: default,
                hasExplicitQosDurability: false,
                qosHistory: default,
                hasExplicitQosHistory: false,
                qosDepth: 0,
                hasExplicitQosDepth: false,
                supportsRos2Native: true);
            var alpha = new FoxRunRos2GeneratedContract(
                "alpha", "/alpha", "Demo.Alpha", "_incoming", "geometry_msgs/msg/Twist",
                Unity.FoxgloveSDK.Components.FoxRunFlow.Subscribe,
                FoxRunRos2RouteEndpoint.R2fu,
                FoxRunQosProfile.SensorData,
                hasExplicitQosProfile: true,
                qosReliability: default,
                hasExplicitQosReliability: false,
                qosDurability: default,
                hasExplicitQosDurability: false,
                qosHistory: default,
                hasExplicitQosHistory: false,
                qosDepth: 0,
                hasExplicitQosDepth: false,
                supportsRos2Native: true);

            diagnostics.Update(
                "source:2|zeta",
                new FoxRunRos2SubscriptionBindingSnapshot(
                    zeta,
                    FoxRunResolvedQos.Default,
                    9,
                    FoxRunRos2SubscriptionBindingState.Failed,
                    FoxRunRos2RegistrationError.BackendFailure,
                    new string('z', FoxRunRos2RegistrationResult.MaximumDiagnosticLength + 9),
                    7, 3, 2, 1, 4, 5, 6, 101, 102),
                new FoxRunRos2RuntimeDiagnosticContext("lyrical", "rmw_zenoh_cpp"));
            diagnostics.Update(
                "source:1|alpha",
                new FoxRunRos2SubscriptionBindingSnapshot(
                    alpha,
                    FoxRunResolvedQos.SensorData,
                    8,
                    FoxRunRos2SubscriptionBindingState.Receiving,
                    FoxRunRos2RegistrationError.None,
                    string.Empty,
                    11, 1, 10, 0, 0, 0, 0, 201, 202),
                new FoxRunRos2RuntimeDiagnosticContext("jazzy", "rmw_fastrtps_cpp"));

            var snapshots = diagnostics.GetSnapshots();

            Assert.Equal(2, snapshots.Length);
            Assert.Equal("alpha", snapshots[0].ContractId);
            Assert.Equal("/alpha", snapshots[0].Topic);
            Assert.Equal("Demo.Alpha", snapshots[0].DeclaringType);
            Assert.Equal("geometry_msgs/msg/Twist", snapshots[0].CanonicalRosType);
            Assert.Equal("jazzy", snapshots[0].RosDistro);
            Assert.Equal("rmw_fastrtps_cpp", snapshots[0].RmwImplementation);
            Assert.Equal("fastdds", snapshots[0].CommunicationMode);
            Assert.Equal("ROS2 Native / FastDDS (DDS)", snapshots[0].TransportLabel);
            AssertResolvedQos(
                FoxRunResolvedQos.SensorData,
                snapshots[0].Qos);
            Assert.Equal(201, snapshots[0].LastReceiveStopwatchTimestamp);
            Assert.Equal(202, snapshots[0].LastApplyStopwatchTimestamp);

            Assert.Equal("zeta", snapshots[1].ContractId);
            Assert.Equal("zenoh", snapshots[1].CommunicationMode);
            Assert.Equal("ROS2 Native / Zenoh", snapshots[1].TransportLabel);
            AssertResolvedQos(
                FoxRunResolvedQos.Default,
                snapshots[1].Qos);
            Assert.Equal("BackendFailure", snapshots[1].LastErrorCode);
            Assert.Equal("The native ROS2 backend failed while operating the subscription.", snapshots[1].LastErrorMessage);
            Assert.Equal(7, snapshots[1].Received);
            Assert.Equal(3, snapshots[1].Replaced);
            Assert.Equal(2, snapshots[1].Applied);
            Assert.Equal(1, snapshots[1].Pending);
            Assert.Equal(4, snapshots[1].RejectedAfterStop);
            Assert.Equal(5, snapshots[1].CopyFailed);
            Assert.Equal(6, snapshots[1].StaleCallbacks);

            var unknownRmw = new FoxRunRos2RuntimeDiagnosticContext(
                "future", "rmw_custom_cpp");
            Assert.Equal("unknown", unknownRmw.CommunicationMode);
            Assert.Equal("ROS2 Native / rmw_custom_cpp", unknownRmw.TransportLabel);

            var getSnapshots = typeof(FoxRunRos2SubscriptionRuntimeDiagnostics).GetMethod("GetSnapshots");
            Assert.NotNull(getSnapshots);
            Assert.Equal(typeof(FoxRunRos2SubscriptionDiagnosticSnapshot[]), getSnapshots.ReturnType);
        }
    }
}
#endif
