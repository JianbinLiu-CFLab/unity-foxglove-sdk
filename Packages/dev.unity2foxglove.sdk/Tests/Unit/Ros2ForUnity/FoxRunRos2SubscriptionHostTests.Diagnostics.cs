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

        [Fact]
        public void RuntimeDiagnosticOrderingUsesEndpointAsTheFinalDeterministicTieBreaker()
        {
            var diagnostics = new FoxRunRos2SubscriptionDiagnostics();
            var snapshot = Snapshot(
                "same-contract",
                FoxRunRos2SubscriptionBindingState.Receiving,
                FoxRunRos2RegistrationError.None,
                string.Empty,
                received: 9);
            diagnostics.Update("source:9|same-contract", snapshot);
            diagnostics.Update(
                "source:1|same-contract",
                Snapshot(
                    "same-contract",
                    FoxRunRos2SubscriptionBindingState.Receiving,
                    FoxRunRos2RegistrationError.None,
                    string.Empty,
                    received: 1));

            var snapshots = diagnostics.GetSnapshots();

            Assert.Equal(2, snapshots.Length);
            Assert.Equal(1, snapshots[0].Received);
            Assert.Equal(9, snapshots[1].Received);
        }

        [Fact]
        public void OneRegistrationFailureDoesNotPreventTheNextContract()
        {
            var failures = 0;
            var laterRegistrations = 0;

            Assert.False(FoxRunRos2RegistrationIsolation.TryRun(
                () => throw new InvalidOperationException("first failed"),
                _ => failures++));
            Assert.True(FoxRunRos2RegistrationIsolation.TryRun(
                () => laterRegistrations++,
                _ => failures++));

            Assert.Equal(1, failures);
            Assert.Equal(1, laterRegistrations);
        }

        [Fact]
        [Trait("Phase", "184-F")]
        public void NullStreamRegistrationStillMarksTheEndpointSeenForStableDiagnostics()
        {
            var hub = new FoxRunRos2SubscriptionHub();
            var source = new NativeOnlySource();
            var sourceCandidateType = typeof(FoxRunRos2SubscriptionHub).GetNestedType(
                "SourceCandidate",
                BindingFlags.NonPublic);
            Assert.NotNull(sourceCandidateType);
            var sourceCandidate = Activator.CreateInstance(
                sourceCandidateType,
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                args: new object[] { source, source, null },
                culture: null);
            var registrarType = typeof(FoxRunRos2SubscriptionHub).GetNestedType(
                "CollectingRegistrar",
                BindingFlags.NonPublic);
            Assert.NotNull(registrarType);
            var registrar = Assert.IsAssignableFrom<IFoxRunRos2SubscriptionRegistrar>(
                Activator.CreateInstance(
                    registrarType,
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    binder: null,
                    args: new[] { (object)hub, sourceCandidate },
                    culture: null));

            var contract = Contract("ros2-native", "default");
            registrar.RegisterStream<FakeHostMessage, FakeHostMessage>(
                contract,
                tryAdmitInput: null,
                materializeOwned: (message, _) => message,
                transferOwned: _ => { },
                clearOwned: () => { });

            var seenField = typeof(FoxRunRos2SubscriptionHub).GetField(
                "_seenEndpoints",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(seenField);
            var seen = Assert.IsType<HashSet<string>>(seenField.GetValue(hub));
            var identity = source.GetInstanceID() + "|" + contract.Id;
            Assert.Contains(identity, seen);

            var diagnosticsField = typeof(FoxRunRos2SubscriptionHub).GetField(
                "_diagnostics",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(diagnosticsField);
            var diagnostics = Assert.IsType<FoxRunRos2SubscriptionDiagnostics>(
                diagnosticsField.GetValue(hub));
            Assert.True(diagnostics.TryGet(identity, out var beforeReconcile));
            Assert.Equal(FoxRunRos2SubscriptionBindingState.Failed, beforeReconcile.State);
            Assert.Equal(FoxRunRos2RegistrationError.BackendFailure, beforeReconcile.Error);

            diagnostics.RemoveExcept(seen);

            Assert.True(diagnostics.TryGet(identity, out var afterReconcile));
            Assert.Equal(beforeReconcile, afterReconcile);
        }

    }
}
#endif
