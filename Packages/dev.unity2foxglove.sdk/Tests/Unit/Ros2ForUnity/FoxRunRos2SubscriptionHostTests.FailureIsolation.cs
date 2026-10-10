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

    }
}
#endif
