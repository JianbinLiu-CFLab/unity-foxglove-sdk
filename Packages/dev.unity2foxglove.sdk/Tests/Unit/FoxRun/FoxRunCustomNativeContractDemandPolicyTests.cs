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
        public void PublishOnlyCreatesDemandOnlyWhenNativeOutputIsEnabled()
        {
            var contract = Contract("PublishOnly", FoxRunSubscriptionProvider.Inherit);

            Assert.False(FoxRunCustomNativeContractDemandPolicy.HasDemand(
                new[] { contract }, false, false, FoxRunSubscriptionProvider.FoxgloveWebSocket));
            Assert.True(FoxRunCustomNativeContractDemandPolicy.HasDemand(
                new[] { contract }, true, false, FoxRunSubscriptionProvider.FoxgloveWebSocket));
        }

        [Fact]
        public void SubscribeOnlyRequiresEnabledNativeProvider()
        {
            var inherited = Contract("SubscribeOnly", FoxRunSubscriptionProvider.Inherit);
            var explicitNative = Contract("SubscribeOnly", FoxRunSubscriptionProvider.Ros2Native);

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
                "Demo.Source", "Value", "/value", "PublishOnly",
                FoxRunSubscriptionProvider.Inherit, FoxRunRos2QosPreset.Default,
                supportsRos2Native: true, "dto", "payload", string.Empty);

            Assert.False(FoxRunCustomNativeContractDemandPolicy.HasDemand(
                new[] { invalid }, true, true, FoxRunSubscriptionProvider.Ros2Native));
        }

        [Fact]
        public void GeneratedMetadataRetainsPublishOnlyCustomNativeContract()
        {
            var customShape = new FoxRunRos2CustomDtoShape(
                "Demo.Payload", "dto", "payload", hasPublicParameterlessConstructor: true,
                isSupported: true, members: System.Array.Empty<FoxRunRos2CustomDtoMemberShape>(),
                diagnostics: System.Array.Empty<string>());
            var member = new FoxRunManifestMember(
                "Demo", "Publisher", "Payload", "field", "Demo.Payload", false, false, string.Empty,
                "/custom", 10f, "", 0, 0f, 0f,
                flowMode: (int)FoxRunMode.PublishOnly,
                subscriptionProvider: FoxRunGenerationDescriptorConstants.InheritSubscriptionProvider,
                ros2Qos: FoxRunGenerationDescriptorConstants.ReliableRos2Qos,
                generatesWebSocketCodec: false,
                generatesRos2NativeRegistration: true,
                ros2CustomDtoShape: customShape,
                ros2ContractKind: FoxRunRos2ContractKind.CustomDto);

            var manifest = FoxRunManifestBuilder.Build(new[] { member }, manifestVersion: 2);
            var contract = Assert.Single(manifest.CustomNativeContracts);
            var generated = FoxRunSchemaInfoWriter.GenerateSource(manifest);

            Assert.Equal("PublishOnly", contract.FlowMode);
            Assert.Equal("payloadEnvelope", contract.CustomEnvelopeIdentity);
            Assert.Contains("CustomNativeContractCount = 1", generated, System.StringComparison.Ordinal);
            Assert.Contains("new FoxRunSchemaCustomNativeContractInfo", generated, System.StringComparison.Ordinal);
            Assert.True(FoxRunSchemaInfoWriter.VerifyGeneratedInfo(manifest, generated).IsValid);
        }

        private static FoxRunSchemaCustomNativeContractInfo Contract(
            string flowMode,
            FoxRunSubscriptionProvider provider)
            => new FoxRunSchemaCustomNativeContractInfo(
                "Demo.Source", "Value", "/value", flowMode, provider,
                FoxRunRos2QosPreset.Reliable, supportsRos2Native: true,
                "dto", "payload", "PayloadEnvelope");
    }
}
