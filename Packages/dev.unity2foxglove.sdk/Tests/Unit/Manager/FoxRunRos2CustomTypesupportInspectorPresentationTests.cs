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
        [InlineData(
            "Publish",
            FoxRunQosProfile.Default,
            (FoxRunQosReliability)0,
            (FoxRunQosDurability)0,
            (FoxRunQosHistory)0,
            0,
            "Outbound / Default")]
        [InlineData(
            "Subscribe",
            FoxRunQosProfile.Default,
            FoxRunQosReliability.Reliable,
            FoxRunQosDurability.Volatile,
            FoxRunQosHistory.KeepLast,
            10,
            "Inbound / Default / Reliable / Volatile / Keep Last / Depth 10")]
        [InlineData(
            "PublishAndSubscribe",
            FoxRunQosProfile.SensorData,
            FoxRunQosReliability.BestEffort,
            FoxRunQosDurability.Volatile,
            FoxRunQosHistory.KeepLast,
            5,
            "Inbound and outbound / Sensor Data / Best Effort / Volatile / Keep Last / Depth 5")]
        [InlineData(
            "Subscribe",
            FoxRunQosProfile.SystemDefault,
            FoxRunQosReliability.SystemDefault,
            FoxRunQosDurability.SystemDefault,
            FoxRunQosHistory.SystemDefault,
            0,
            "Inbound / System Default / System Default Reliability / System Default Durability / System Default History")]
        public void GeneratedContractDirectionLabelsDescribeEachNativeTransportDirection(
            string flow,
            FoxRunQosProfile profile,
            FoxRunQosReliability reliability,
            FoxRunQosDurability durability,
            FoxRunQosHistory history,
            int depth,
            string expected)
        {
            Assert.Equal(
                expected,
                Ros2ForUnityCustomTypesupportInspectorPresentation.DirectionalContractPolicyLabel(
                    flow,
                    profile,
                    reliability,
                    durability,
                    history,
                    depth));
        }
    }
}
