// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Ros2ForUnity
// Purpose: Pins unified R2FU runtime demand without changing output policy.

using Unity.FoxgloveSDK.Components;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Ros2ForUnity
{
    [Trait("Phase", "179-D")]
    [Trait("Domain", "R2fuNativeDemand")]
    public sealed class R2fuNativeDemandPolicyTests
    {
        [Fact]
        public void NativeOutputAlwaysRequiresTheNativeRuntime()
        {
            Assert.True(FoxRunNativeDemandPolicy.HasNativeRuntimeDemand(
                nativeOutputEnabled: true,
                defaultPublishTargets: FoxRunEndpoint.Foxglove,
                hasExplicitNativePublishContract: false,
                subscriptionsEnabled: false,
                defaultSubscriptionSource: FoxRunEndpoint.Foxglove,
                hasExplicitNativeContract: false));
        }

        [Fact]
        public void DefaultNativeSubscriptionRequiresPreflightEvenWithZeroContracts()
        {
            var zeroContracts = System.Array.Empty<FoxRunSchemaSubscriptionBindingInfo>();
            Assert.False(FoxRunNativeDemandPolicy.HasExplicitNativeContract(zeroContracts));
            Assert.True(FoxRunNativeDemandPolicy.HasNativeRuntimeDemand(
                nativeOutputEnabled: false,
                defaultPublishTargets: FoxRunEndpoint.Foxglove,
                hasExplicitNativePublishContract: false,
                subscriptionsEnabled: true,
                defaultSubscriptionSource: FoxRunEndpoint.Ros2Native,
                hasExplicitNativeContract: FoxRunNativeDemandPolicy.HasExplicitNativeContract(zeroContracts)));
        }

        [Fact]
        public void ExplicitNativeContractRequiresPreflightUnderWebSocketDefault()
        {
            Assert.True(FoxRunNativeDemandPolicy.HasNativeRuntimeDemand(
                nativeOutputEnabled: false,
                defaultPublishTargets: FoxRunEndpoint.Foxglove,
                hasExplicitNativePublishContract: false,
                subscriptionsEnabled: true,
                defaultSubscriptionSource: FoxRunEndpoint.Foxglove,
                hasExplicitNativeContract: true));
        }

        [Fact]
        public void DisablingSubscriptionsRemovesOnlyInboundDemand()
        {
            Assert.False(FoxRunNativeDemandPolicy.HasNativeRuntimeDemand(
                nativeOutputEnabled: false,
                defaultPublishTargets: FoxRunEndpoint.Foxglove,
                hasExplicitNativePublishContract: false,
                subscriptionsEnabled: false,
                defaultSubscriptionSource: FoxRunEndpoint.Ros2Native,
                hasExplicitNativeContract: true));
            Assert.True(FoxRunNativeDemandPolicy.HasNativeRuntimeDemand(
                nativeOutputEnabled: true,
                defaultPublishTargets: FoxRunEndpoint.Foxglove,
                hasExplicitNativePublishContract: false,
                subscriptionsEnabled: false,
                defaultSubscriptionSource: FoxRunEndpoint.Ros2Native,
                hasExplicitNativeContract: true));
        }

        [Theory]
        [InlineData(FoxRunEndpoint.Foxglove)]
        [InlineData((FoxRunEndpoint)0)]
        [InlineData((FoxRunEndpoint)99)]
        public void NonNativeManagerDefaultDoesNotInventInboundDemand(
            FoxRunEndpoint defaultSubscriptionSource)
        {
            Assert.False(FoxRunNativeDemandPolicy.HasNativeRuntimeDemand(
                nativeOutputEnabled: false,
                defaultPublishTargets: FoxRunEndpoint.Foxglove,
                hasExplicitNativePublishContract: false,
                subscriptionsEnabled: true,
                defaultSubscriptionSource: defaultSubscriptionSource,
                hasExplicitNativeContract: false));
        }

        [Fact]
        public void NativePublishProfileOrExplicitContractRequiresTheRuntime()
        {
            Assert.True(FoxRunNativeDemandPolicy.HasNativeRuntimeDemand(
                nativeOutputEnabled: false,
                defaultPublishTargets: FoxRunEndpoint.Ros2Native,
                hasExplicitNativePublishContract: false,
                subscriptionsEnabled: false,
                defaultSubscriptionSource: FoxRunEndpoint.Foxglove,
                hasExplicitNativeContract: false));
            Assert.True(FoxRunNativeDemandPolicy.HasNativeRuntimeDemand(
                nativeOutputEnabled: false,
                defaultPublishTargets: FoxRunEndpoint.Foxglove,
                hasExplicitNativePublishContract: true,
                subscriptionsEnabled: false,
                defaultSubscriptionSource: FoxRunEndpoint.Foxglove,
                hasExplicitNativeContract: false));
        }

        [Fact]
        public void OnlyExplicitNativeProviderMetadataCountsAsAnExplicitNativeContract()
        {
            var inheritedButCapable = new FoxRunSchemaSubscriptionBindingInfo(
                "Demo.Source",
                "Incoming",
                "/incoming",
                "Subscribe",
                (FoxRunEndpoint)0,
                FoxRunRos2QosPreset.Inherit,
                supportsWebSocket: true,
                supportsRos2Native: true,
                nativeType: "std_msgs.msg.String",
                canonicalRosType: "std_msgs/msg/String",
                copyShapeIdentity: "fixture");
            var explicitNative = new FoxRunSchemaSubscriptionBindingInfo(
                "Demo.Source",
                "NativeIncoming",
                "/native",
                "Subscribe",
                FoxRunEndpoint.Ros2Native,
                FoxRunRos2QosPreset.Default,
                supportsWebSocket: false,
                supportsRos2Native: true,
                nativeType: "std_msgs.msg.String",
                canonicalRosType: "std_msgs/msg/String",
                copyShapeIdentity: "fixture");

            Assert.False(FoxRunNativeDemandPolicy.HasExplicitNativeContract(
                new[] { inheritedButCapable }));
            Assert.True(FoxRunNativeDemandPolicy.HasExplicitNativeContract(
                new[] { inheritedButCapable, explicitNative }));
        }
    }
}
