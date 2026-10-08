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

        [Fact]
        public void FatalRegistrationFailurePassesThroughTheIsolationBoundary()
        {
            var failures = 0;

            Assert.Throws<OutOfMemoryException>(() =>
                FoxRunRos2RegistrationIsolation.TryRun(
                    () => throw new OutOfMemoryException("fatal registration"),
                    _ => failures++));

            Assert.Equal(0, failures);
        }

        [Fact]
        public void OneApplyFailureIsTerminalAndDoesNotPreventTheNextContract()
        {
            var first = new FakeHostBinding(
                "first",
                () => throw new InvalidOperationException("first setter failed"));
            var secondApplies = 0;
            var second = new FakeHostBinding("second", () =>
            {
                secondApplies++;
                return true;
            });

            Assert.False(FoxRunRos2ApplyIsolation.TryRun(first, 1, out var firstFailure));
            Assert.IsType<InvalidOperationException>(firstFailure);
            Assert.True(FoxRunRos2ApplyIsolation.TryRun(second, 1, out var secondFailure));
            Assert.Null(secondFailure);
            Assert.Equal(1, secondApplies);
            Assert.Equal(FoxRunRos2SubscriptionBindingState.Failed, first.State);
            Assert.True(first.TryGetSnapshot(1, out var snapshot));
            Assert.Equal(FoxRunRos2RegistrationError.ApplyFailure, snapshot.Error);
            Assert.Equal("The native ROS2 subscription could not apply the copied message.", snapshot.Diagnostic);
        }

        [Fact]
        public void FatalApplyFailurePassesThroughWithoutBeingRecordedAsAContractFailure()
        {
            var binding = new FakeHostBinding(
                "fatal-apply",
                () => throw new OutOfMemoryException("fatal apply"));

            Assert.Throws<OutOfMemoryException>(() =>
                FoxRunRos2ApplyIsolation.TryRun(binding, 1, out _));

            Assert.Equal(FoxRunRos2SubscriptionBindingState.Ready, binding.State);
        }

        private static FoxRunRos2GeneratedContract Contract(
            string provider,
            string qos,
            Unity.FoxgloveSDK.Components.FoxRunFlow mode =
                Unity.FoxgloveSDK.Components.FoxRunFlow.Subscribe,
            bool supportsNative = true)
        {
            var hasExplicitQosProfile = TryParseQosProfile(qos, out var qosProfile);
            return new FoxRunRos2GeneratedContract(
                "host-contract-" + provider + "-" + qos,
                "/native/string",
                "Demo.HostReceiver",
                "_incoming",
                "std_msgs/msg/String",
                mode,
                ParseProvider(provider),
                qosProfile,
                hasExplicitQosProfile,
                qosReliability: default,
                hasExplicitQosReliability: false,
                qosDurability: default,
                hasExplicitQosDurability: false,
                qosHistory: default,
                hasExplicitQosHistory: false,
                qosDepth: 0,
                hasExplicitQosDepth: false,
                supportsRos2Native: supportsNative);
        }

        private static FoxRunRos2GeneratedContract CustomContract(
            FoxRunRos2RouteEndpoint provider,
            Unity.FoxgloveSDK.Components.FoxRunEncoding encoding)
            => new FoxRunRos2GeneratedContract(
                "custom-contract",
                "/native/custom",
                "Demo.CustomReceiver",
                "_incoming",
                "unity2foxglove_foxrun_interfaces_v1/msg/CustomEnvelope",
                Unity.FoxgloveSDK.Components.FoxRunFlow.PublishAndSubscribe,
                provider,
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
                supportsRos2Native: true,
                declaredSubscriptionEncoding: encoding,
                contractKind: FoxRunRos2GeneratedContractKind.CustomInterface,
                staticInterfacePackageId: "dev.unity2foxglove.foxrun.ros2.interfaces",
                rosPackageName: "unity2foxglove_foxrun_interfaces_v1",
                interfaceRevision: 1,
                interfaceDigest: "120864853239fae290b5199cd02dbf02f107299bccd8972b06d8cf59fc7594fd",
                baseRuntimePackageId: "dev.unity2foxglove.ros2forunity.runtime.jazzy.win64",
                canonicalPayloadType: "unity2foxglove_foxrun_interfaces_v1/msg/Custom");

        private static FoxRunRos2RouteEndpoint ParseProvider(string provider)
            => provider == "ros2-native"
                ? FoxRunRos2RouteEndpoint.R2fu
                : provider == "foxglove" || provider == "foxglove-stream"
                    ? FoxRunRos2RouteEndpoint.WebSocket
                    : provider == "invalid"
                        ? (FoxRunRos2RouteEndpoint)99
                        : (FoxRunRos2RouteEndpoint)0;

        private static bool TryParseQosProfile(
            string qos,
            out FoxRunQosProfile profile)
        {
            switch (qos)
            {
                case "reliable":
                    profile = FoxRunQosProfile.Default;
                    return true;
                case "sensor-data":
                    profile = FoxRunQosProfile.SensorData;
                    return true;
                case "default":
                    profile = FoxRunQosProfile.Default;
                    return true;
                case "inherit":
                    profile = default;
                    return false;
                default:
                    throw new ArgumentOutOfRangeException(nameof(qos), qos, "Unknown test QoS.");
            }
        }

        private static void AssertResolvedQos(
            FoxRunResolvedQos expected,
            FoxRunResolvedQos actual)
        {
            Assert.Equal(expected, actual);
            Assert.Equal(expected.Profile, actual.Profile);
            Assert.Equal(expected.Reliability, actual.Reliability);
            Assert.Equal(expected.Durability, actual.Durability);
            Assert.Equal(expected.History, actual.History);
            Assert.Equal(expected.Depth, actual.Depth);
        }

        private static FoxRunRos2SubscriptionBindingSnapshot Snapshot(
            string id,
            FoxRunRos2SubscriptionBindingState state,
            FoxRunRos2RegistrationError error,
            string diagnostic,
            long received = 0)
            => new FoxRunRos2SubscriptionBindingSnapshot(
                id,
                1,
                state,
                error,
                diagnostic,
                received,
                0,
                0,
                0,
                0,
                0,
                0);

        private sealed class FakeHostMessage : ROS2.Message, IDisposable
        {
            public bool IsDisposed { get; private set; }

            public void Dispose() => IsDisposed = true;
        }

        private sealed class FakeHostBinding : IFoxRunRos2HostBinding
        {
            private readonly Func<bool> _apply;
            private FoxRunRos2RegistrationResult _result = FoxRunRos2RegistrationResult.Success();

            internal FakeHostBinding(string id, Func<bool> apply)
            {
                Contract = new FoxRunRos2GeneratedContract(
                    id, "/" + id, "Demo.Host", "_message", "std_msgs/msg/String",
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
                _apply = apply;
                State = FoxRunRos2SubscriptionBindingState.Ready;
            }

            public FoxRunRos2GeneratedContract Contract { get; }
            public string ContractId => Contract.Id;
            public long SessionGeneration => 1;
            public FoxRunRos2SubscriptionBindingState State { get; private set; }
            public bool CanRetryRegistration => false;
            public FoxRunRos2RegistrationResult TryRegister() => _result;
            public bool TryApplyLatest(long activeSessionGeneration) => _apply();
            public void RecordApplyFailure(Exception exception)
            {
                State = FoxRunRos2SubscriptionBindingState.Failed;
                _result = FoxRunRos2RegistrationResult.Failure(
                    FoxRunRos2RegistrationError.ApplyFailure,
                    exception.GetType().Name + ": " + exception.Message);
            }
            public bool TryGetSnapshot(long activeSessionGeneration, out FoxRunRos2SubscriptionBindingSnapshot snapshot)
            {
                snapshot = Snapshot(ContractId, State, _result.Error, _result.Diagnostic);
                return activeSessionGeneration == SessionGeneration;
            }
            public FoxRunRos2AcceptanceArmStatus ArmAcceptanceAttempt(
                out FoxRunRos2AcceptanceAttemptSnapshot snapshot)
            {
                snapshot = default;
                return FoxRunRos2AcceptanceArmStatus.EndpointUnavailable;
            }
            public bool TryGetAcceptanceAttempt(out FoxRunRos2AcceptanceAttemptSnapshot snapshot)
            {
                snapshot = default;
                return false;
            }
            public bool EndAcceptanceAttempt(long epoch) => false;
            public bool TryCompleteAcceptanceAttempt(
                long epoch,
                out FoxRunRos2AcceptanceAttemptSnapshot snapshot)
            {
                snapshot = default;
                return false;
            }
            public void Stop() => State = FoxRunRos2SubscriptionBindingState.Stopped;
        }

        private sealed class NativeOnlySource : UnityEngine.MonoBehaviour, IFoxRunRos2SubscriptionSource
        {
            public int FoxRunRos2SubscriptionCount => 1;

            public void FoxRunRos2RegisterSubscriptions(IFoxRunRos2SubscriptionRegistrar registrar)
            {
            }
        }

        private sealed class CustomNativeOnlySource : UnityEngine.MonoBehaviour, IFoxRunRos2CustomSubscriptionSource
        {
            public int FoxRunRos2CustomSubscriptionCount => 1;

            public void FoxRunRos2RegisterCustomSubscriptions(IFoxRunRos2SubscriptionRegistrar registrar)
            {
            }
        }

        private sealed class HostManagedQosProfile : IFoxRunRos2NativeQosProfile
        {
            public ROS2.QualityOfServiceProfile NativeProfile { get; } = null;
            public void SetHistory(ROS2.HistoryPolicy history, int depth) { }
            public void SetPolicies(ROS2.HistoryPolicy history, int depth, ROS2.ReliabilityPolicy reliability, ROS2.DurabilityPolicy durability) { }
            public void Dispose() { }
        }

        private sealed class FakeR2fuNodeDriver : IFoxRunRos2R2fuNodeDriver
        {
            public int CreateCount { get; private set; }
            public int RemoveCount { get; private set; }
            public int ReleaseNodeCount { get; private set; }
            public int ReleaseFailuresRemaining { get; set; }
            public List<ROS2.QualityOfServiceProfile> SeenQos { get; } = new List<ROS2.QualityOfServiceProfile>();
            public bool RemoveReturns { get; set; } = true;

            public object CreateSubscription<T>(string topic, Action<T> callback, ROS2.QualityOfServiceProfile qos)
                where T : ROS2.Message, new()
            {
                CreateCount++;
                SeenQos.Add(qos);
                return new object();
            }

            public bool IsSubscriptionUsable(object subscription) => subscription != null;

            public bool RemoveSubscription(object subscription)
            {
                Assert.NotNull(subscription);
                RemoveCount++;
                return RemoveReturns;
            }

            public object CreatePublisher<T>(string topic, ROS2.QualityOfServiceProfile qos)
                where T : ROS2.Message, new()
                => new object();

            public bool IsPublisherUsable<T>(object publisher)
                where T : ROS2.Message, new()
                => publisher != null;

            public bool Publish<T>(object publisher, T message)
                where T : ROS2.Message, new()
                => publisher != null && message != null;

            public bool RemovePublisher<T>(object publisher)
                where T : ROS2.Message, new()
                => publisher != null;

            public bool ReleaseNode()
            {
                ReleaseNodeCount++;
                if (ReleaseFailuresRemaining > 0)
                {
                    ReleaseFailuresRemaining--;
                    return false;
                }
                return true;
            }
        }
    }
}
#endif
