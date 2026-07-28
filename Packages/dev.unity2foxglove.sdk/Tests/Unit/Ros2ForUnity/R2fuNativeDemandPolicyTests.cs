// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Ros2ForUnity
// Purpose: Pins unified R2FU runtime demand without changing output policy.

using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.UnitTests.Harness;
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
                (FoxRunQosProfile)0,
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
                FoxRunQosProfile.Default,
                supportsWebSocket: false,
                supportsRos2Native: true,
                nativeType: "std_msgs.msg.String",
                canonicalRosType: "std_msgs/msg/String",
                copyShapeIdentity: "fixture",
                qosReliability: FoxRunQosReliability.Reliable,
                qosDurability: FoxRunQosDurability.Volatile,
                qosHistory: FoxRunQosHistory.KeepLast,
                qosDepth: 10);

            Assert.False(FoxRunNativeDemandPolicy.HasExplicitNativeContract(
                new[] { inheritedButCapable }));
            Assert.True(FoxRunNativeDemandPolicy.HasExplicitNativeContract(
                new[] { inheritedButCapable, explicitNative }));
        }

        [Fact]
        public void LoadedSceneProbeUsesOnlyInstantiatedFoxRunSources()
        {
            var foxgloveOnly = new FoxRunLoadedSceneContractDescriptor(
                "Demo.FoxgloveOnly",
                hasExplicitNativePublishContract: false,
                hasExplicitNativeSubscriptionContract: false,
                hasExplicitFoxgloveSubscriptionContract: true);

            var snapshot = FoxRunLoadedSceneContractProbe.InspectContracts(
                new[] { foxgloveOnly });

            Assert.False(snapshot.HasExplicitNativePublishContract);
            Assert.False(snapshot.HasExplicitNativeSubscriptionContract);
            Assert.True(snapshot.HasExplicitFoxgloveSubscriptionContract);
            Assert.True(snapshot.ContainsDeclaringType("Demo.FoxgloveOnly"));
            Assert.False(snapshot.ContainsDeclaringType("Generated.NativeSourceElsewhereInProject"));
        }

        [Fact]
        public void LoadedSceneProbeDetectsExplicitNativePublishAndSubscribeContracts()
        {
            var native = new FoxRunLoadedSceneContractDescriptor(
                "Demo.Native",
                hasExplicitNativePublishContract: true,
                hasExplicitNativeSubscriptionContract: true,
                hasExplicitFoxgloveSubscriptionContract: false);

            var snapshot = FoxRunLoadedSceneContractProbe.InspectContracts(
                new[] { native });

            Assert.True(snapshot.HasExplicitNativePublishContract);
            Assert.True(snapshot.HasExplicitNativeSubscriptionContract);
            Assert.False(snapshot.HasExplicitFoxgloveSubscriptionContract);
        }

        [Fact]
        public void InspectorAndPlayModeGuardUseLoadedSceneEvidence()
        {
            var inspector = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.R2fuRuntime.cs");
            var subscribeInspector = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.SubscribeData.cs");
            var guard = TestSources.Text(
                "Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityRuntimePlayModeGuard.cs");

            Assert.Contains(
                "FoxRunLoadedSceneContractProbe.CaptureLoadedScenes()",
                inspector,
                System.StringComparison.Ordinal);
            Assert.Contains(
                "HasLoadedSceneExplicitSource",
                subscribeInspector,
                System.StringComparison.Ordinal);
            Assert.DoesNotContain(
                "GetGeneratedSubscriptionBindings",
                inspector,
                System.StringComparison.Ordinal);
            Assert.Contains(
                "FoxRunLoadedSceneContractProbe.CaptureLoadedScenes()",
                guard,
                System.StringComparison.Ordinal);
            Assert.DoesNotContain(
                "HasGeneratedExplicitNative",
                guard,
                System.StringComparison.Ordinal);
        }
    }
}
