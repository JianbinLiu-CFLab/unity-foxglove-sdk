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
                subscriptionsEnabled: false,
                defaultSubscriptionProvider: FoxRunSubscriptionProvider.FoxgloveWebSocket,
                hasExplicitNativeContract: false));
        }

        [Fact]
        public void DefaultNativeSubscriptionRequiresPreflightEvenWithZeroContracts()
        {
            var zeroContracts = System.Array.Empty<FoxRunSchemaSubscriptionBindingInfo>();
            Assert.False(FoxRunNativeDemandPolicy.HasExplicitNativeContract(zeroContracts));
            Assert.True(FoxRunNativeDemandPolicy.HasNativeRuntimeDemand(
                nativeOutputEnabled: false,
                subscriptionsEnabled: true,
                defaultSubscriptionProvider: FoxRunSubscriptionProvider.Ros2Native,
                hasExplicitNativeContract: FoxRunNativeDemandPolicy.HasExplicitNativeContract(zeroContracts)));
        }

        [Fact]
        public void ExplicitNativeContractRequiresPreflightUnderWebSocketDefault()
        {
            Assert.True(FoxRunNativeDemandPolicy.HasNativeRuntimeDemand(
                nativeOutputEnabled: false,
                subscriptionsEnabled: true,
                defaultSubscriptionProvider: FoxRunSubscriptionProvider.FoxgloveWebSocket,
                hasExplicitNativeContract: true));
        }

        [Fact]
        public void DisablingSubscriptionsRemovesOnlyInboundDemand()
        {
            Assert.False(FoxRunNativeDemandPolicy.HasNativeRuntimeDemand(
                nativeOutputEnabled: false,
                subscriptionsEnabled: false,
                defaultSubscriptionProvider: FoxRunSubscriptionProvider.Ros2Native,
                hasExplicitNativeContract: true));
            Assert.True(FoxRunNativeDemandPolicy.HasNativeRuntimeDemand(
                nativeOutputEnabled: true,
                subscriptionsEnabled: false,
                defaultSubscriptionProvider: FoxRunSubscriptionProvider.Ros2Native,
                hasExplicitNativeContract: true));
        }

        [Theory]
        [InlineData(FoxRunSubscriptionProvider.FoxgloveWebSocket)]
        [InlineData(FoxRunSubscriptionProvider.Inherit)]
        [InlineData((FoxRunSubscriptionProvider)99)]
        public void NonNativeManagerDefaultDoesNotInventInboundDemand(
            FoxRunSubscriptionProvider defaultSubscriptionProvider)
        {
            Assert.False(FoxRunNativeDemandPolicy.HasNativeRuntimeDemand(
                nativeOutputEnabled: false,
                subscriptionsEnabled: true,
                defaultSubscriptionProvider: defaultSubscriptionProvider,
                hasExplicitNativeContract: false));
        }

        [Fact]
        public void OnlyExplicitNativeProviderMetadataCountsAsAnExplicitNativeContract()
        {
            var inheritedButCapable = new FoxRunSchemaSubscriptionBindingInfo(
                "Demo.Source",
                "Incoming",
                "/incoming",
                "SubscribeOnly",
                FoxRunSubscriptionProvider.Inherit,
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
                "SubscribeOnly",
                FoxRunSubscriptionProvider.Ros2Native,
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
