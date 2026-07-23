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
        public void PublishCreatesDemandWhenItsResolvedTargetsIncludeNative()
        {
            var contract = Contract("Publish", (FoxRunEndpoint)0);

            Assert.False(FoxRunCustomNativeContractDemandPolicy.HasDemand(
                new[] { contract }, FoxRunEndpoint.Foxglove, false, FoxRunEndpoint.Foxglove));
            Assert.True(FoxRunCustomNativeContractDemandPolicy.HasDemand(
                new[] { contract }, FoxRunEndpoint.Ros2Native, false, FoxRunEndpoint.Foxglove));
        }

        [Fact]
        public void SubscribeRequiresEnabledNativeProvider()
        {
            var inherited = Contract("Subscribe", (FoxRunEndpoint)0);
            var explicitNative = Contract("Subscribe", FoxRunEndpoint.Ros2Native);

            Assert.False(FoxRunCustomNativeContractDemandPolicy.HasDemand(
                new[] { inherited }, FoxRunEndpoint.Foxglove, true, FoxRunEndpoint.Foxglove));
            Assert.True(FoxRunCustomNativeContractDemandPolicy.HasDemand(
                new[] { inherited }, FoxRunEndpoint.Foxglove, true, FoxRunEndpoint.Ros2Native));
            Assert.True(FoxRunCustomNativeContractDemandPolicy.HasDemand(
                new[] { explicitNative }, FoxRunEndpoint.Foxglove, true, FoxRunEndpoint.Foxglove));
        }

        [Fact]
        public void PublishAndSubscribeHonorsEitherIndependentDirection()
        {
            var contract = Contract("PublishAndSubscribe", FoxRunEndpoint.Foxglove);

            Assert.False(FoxRunCustomNativeContractDemandPolicy.HasDemand(
                new[] { contract }, FoxRunEndpoint.Foxglove, true, FoxRunEndpoint.Foxglove));
            Assert.True(FoxRunCustomNativeContractDemandPolicy.HasDemand(
                new[] { contract }, FoxRunEndpoint.Ros2Native, false, FoxRunEndpoint.Foxglove));
        }

        [Fact]
        public void MissingEnvelopeNeverCreatesDemand()
        {
            var invalid = new FoxRunSchemaCustomNativeContractInfo(
                "Demo.Source", "Value", "/value", "Publish",
                (FoxRunEndpoint)0, FoxRunRos2QosPreset.Default,
                supportsRos2Native: true, "dto", "payload", string.Empty);

            Assert.False(FoxRunCustomNativeContractDemandPolicy.HasDemand(
                new[] { invalid }, FoxRunEndpoint.Ros2Native, true, FoxRunEndpoint.Ros2Native));
        }

        [Fact]
        public void ExplicitTargetsReplaceThePublishProfile()
        {
            var explicitFoxglove = Contract(
                "Publish",
                (FoxRunEndpoint)0,
                FoxRunEndpoint.Foxglove);
            var explicitNative = Contract(
                "Publish",
                (FoxRunEndpoint)0,
                FoxRunEndpoint.Foxglove | FoxRunEndpoint.Ros2Native);

            Assert.False(FoxRunCustomNativeContractDemandPolicy.HasDemand(
                new[] { explicitFoxglove },
                FoxRunEndpoint.Ros2Native,
                false,
                FoxRunEndpoint.Foxglove));
            Assert.True(FoxRunCustomNativeContractDemandPolicy.HasDemand(
                new[] { explicitNative },
                FoxRunEndpoint.Foxglove,
                false,
                FoxRunEndpoint.Foxglove));
            Assert.True(
                FoxRunCustomNativeContractDemandPolicy.HasExplicitNativePublishContract(
                    new[] { explicitNative }));
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
                "/custom", 10f, "", 0, 0f,
                flow: (int)FoxRunFlow.Publish,
                source: FoxRunGenerationDescriptorConstants.InheritSource,
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
            FoxRunEndpoint provider,
            FoxRunEndpoint targets = 0)
            => new FoxRunSchemaCustomNativeContractInfo(
                "Demo.Source", "Value", "/value", flow, provider,
                FoxRunRos2QosPreset.Reliable, supportsRos2Native: true,
                "dto", "payload", "PayloadEnvelope", targets);
    }
}
