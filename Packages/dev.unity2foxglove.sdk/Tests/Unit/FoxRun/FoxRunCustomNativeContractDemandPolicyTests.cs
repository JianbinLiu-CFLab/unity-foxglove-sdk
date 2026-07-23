// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.FoxRun
{
    [Trait("Phase", "181-E")]
    [Trait("Domain", "NativeDemand")]
    public sealed class FoxRunCustomNativeContractDemandPolicyTests
    {
        [Fact]
        public void PublishCreatesDemandOnlyWhenNativeOutputIsEnabled()
        {
            var contract = Contract("Publish", FoxRunSubscriptionProvider.Inherit);

            Assert.False(FoxRunCustomNativeContractDemandPolicy.HasDemand(
                new[] { contract }, false, false, FoxRunSubscriptionProvider.FoxgloveWebSocket));
            Assert.True(FoxRunCustomNativeContractDemandPolicy.HasDemand(
                new[] { contract }, true, false, FoxRunSubscriptionProvider.FoxgloveWebSocket));
        }

        [Fact]
        public void SubscribeRequiresEnabledNativeProvider()
        {
            var inherited = Contract("Subscribe", FoxRunSubscriptionProvider.Inherit);
            var explicitNative = Contract("Subscribe", FoxRunSubscriptionProvider.Ros2Native);

            Assert.False(FoxRunCustomNativeContractDemandPolicy.HasDemand(
                new[] { inherited }, false, true, FoxRunSubscriptionProvider.FoxgloveWebSocket));
            Assert.True(FoxRunCustomNativeContractDemandPolicy.HasDemand(
                new[] { inherited }, false, true, FoxRunSubscriptionProvider.Ros2Native));
            Assert.True(FoxRunCustomNativeContractDemandPolicy.HasDemand(
                new[] { explicitNative }, false, true, FoxRunSubscriptionProvider.FoxgloveWebSocket));
        }

        [Fact]
        public void PublishAndSubscribeHonorsEitherIndependentDirection()
        {
            var contract = Contract("PublishAndSubscribe", FoxRunSubscriptionProvider.FoxgloveWebSocket);

            Assert.False(FoxRunCustomNativeContractDemandPolicy.HasDemand(
                new[] { contract }, false, true, FoxRunSubscriptionProvider.FoxgloveWebSocket));
            Assert.True(FoxRunCustomNativeContractDemandPolicy.HasDemand(
                new[] { contract }, true, false, FoxRunSubscriptionProvider.FoxgloveWebSocket));
        }

        [Fact]
        public void MissingEnvelopeNeverCreatesDemand()
        {
            var invalid = new FoxRunSchemaCustomNativeContractInfo(
                "Demo.Source", "Value", "/value", "Publish",
                FoxRunSubscriptionProvider.Inherit, FoxRunRos2QosPreset.Default,
                supportsRos2Native: true, "dto", "payload", string.Empty);

            Assert.False(FoxRunCustomNativeContractDemandPolicy.HasDemand(
                new[] { invalid }, true, true, FoxRunSubscriptionProvider.Ros2Native));
        }

        [Fact]
        public void GeneratedMetadataRetainsPublishCustomNativeContract()
        {
            var customShape = new FoxRunRos2CustomDtoShape(
                "Demo.Payload", "dto", "payload", hasPublicParameterlessConstructor: true,
                isSupported: true, members: System.Array.Empty<FoxRunRos2CustomDtoMemberShape>(),
                diagnostics: System.Array.Empty<string>());
            var member = new FoxRunManifestMember(
                "Demo", "Publisher", "Payload", "field", "Demo.Payload", false, false, string.Empty,
                "/custom", 10f, "", 0, 0f, 0f,
                flow: (int)FoxRunFlow.Publish,
                subscriptionProvider: FoxRunGenerationDescriptorConstants.InheritSubscriptionProvider,
                ros2Qos: FoxRunGenerationDescriptorConstants.ReliableRos2Qos,
                generatesWebSocketCodec: false,
                generatesRos2NativeRegistration: true,
                ros2CustomDtoShape: customShape,
                ros2ContractKind: FoxRunRos2ContractKind.CustomDto);

            var manifest = FoxRunManifestBuilder.Build(new[] { member }, manifestVersion: 2);
            var contract = Assert.Single(manifest.CustomNativeContracts);
            var generated = FoxRunSchemaInfoWriter.GenerateSource(manifest);

            Assert.Equal("Publish", contract.Flow);
            Assert.Equal("payloadEnvelope", contract.CustomEnvelopeIdentity);
            Assert.Contains("CustomNativeContractCount = 1", generated, System.StringComparison.Ordinal);
            Assert.Contains("new FoxRunSchemaCustomNativeContractInfo", generated, System.StringComparison.Ordinal);
            Assert.True(FoxRunSchemaInfoWriter.VerifyGeneratedInfo(manifest, generated).IsValid);
        }

        private static FoxRunSchemaCustomNativeContractInfo Contract(
            string flow,
            FoxRunSubscriptionProvider provider)
            => new FoxRunSchemaCustomNativeContractInfo(
                "Demo.Source", "Value", "/value", flow, provider,
                FoxRunRos2QosPreset.Reliable, supportsRos2Native: true,
                "dto", "payload", "PayloadEnvelope");
    }
}
