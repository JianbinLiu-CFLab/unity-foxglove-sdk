// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Manager
// Purpose: Keep custom ROS2 typesupport Inspector wording bounded and actionable.

using System;
using System.Linq;
using Unity.FoxgloveSDK.Components;
using Unity2Foxglove.Ros2ForUnity.Editor;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Manager
{
    [Trait("Phase", "181-E")]
    [Trait("Domain", "InspectorPresentation")]
    public sealed class FoxRunRos2CustomTypesupportInspectorPresentationTests
    {
        [Fact]
        public void EveryPreflightStateHasBoundedStatusAndActionPresentation()
        {
            foreach (var code in Enum.GetValues(typeof(Ros2ForUnityCustomTypesupportPreflightCode))
                         .Cast<Ros2ForUnityCustomTypesupportPreflightCode>())
            {
                Assert.False(string.IsNullOrWhiteSpace(
                    Ros2ForUnityCustomTypesupportInspectorPresentation.StatusLabel(code)));
                Assert.False(string.IsNullOrWhiteSpace(
                    Ros2ForUnityCustomTypesupportInspectorPresentation.ActionLabel(code)));
            }
        }

        [Fact]
        public void DigestAndContractPresentationRemainCompactAndDirectional()
        {
            Assert.Equal(
                "0123456789ab",
                Ros2ForUnityCustomTypesupportInspectorPresentation.CompactDigest(
                    "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"));
            Assert.Equal(
                "Inbound / Sensor Data",
                Ros2ForUnityCustomTypesupportInspectorPresentation.ContractPolicyLabel(
                    "Inbound / Sensor Data"));
        }

        [Theory]
        [InlineData("PublishOnly", FoxRunRos2QosPreset.Default, "Outbound / publisher-default QoS")]
        [InlineData("SubscribeOnly", FoxRunRos2QosPreset.Reliable, "Inbound / Reliable")]
        [InlineData("PublishAndSubscribe", FoxRunRos2QosPreset.SensorData, "Inbound / Sensor Data; outbound / publisher-default QoS")]
        public void GeneratedContractDirectionLabelsDescribeEachNativeTransportDirection(
            string flowMode,
            FoxRunRos2QosPreset qos,
            string expected)
        {
            Assert.Equal(
                expected,
                Ros2ForUnityCustomTypesupportInspectorPresentation.DirectionalContractPolicyLabel(flowMode, qos));
        }
    }
}
