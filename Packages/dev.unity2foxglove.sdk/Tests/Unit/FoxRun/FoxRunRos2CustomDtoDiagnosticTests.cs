// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Linq;
using Unity.FoxgloveSDK.Editor;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.FoxRun
{
    public sealed class FoxRunRos2CustomDtoDiagnosticTests
    {
        [Fact]
        public void NativeCustomDtoPublishAndSubscribeUsesItsOwnStableContractDiagnosticPath()
        {
            var shape = FoxRunReflectionRos2CustomDtoShapeBuilder.Build(typeof(ValidDto));
            var diagnostics = Validate(CreateMember(
                FoxRunRos2ContractKind.CustomDto,
                shape,
                mode: 2,
                encoding: FoxRunGenerationDescriptorConstants.JsonEncoding));

            Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "FOXRUN205");
            Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "FOXRUN206");
            Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "FOXRUN402");
            Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "FOXRUN006");
        }

        [Fact]
        public void NativeCustomDtoPublishAndSubscribeMayInheritItsWebSocketOutputEncoding()
        {
            var shape = FoxRunReflectionRos2CustomDtoShapeBuilder.Build(typeof(ValidDto));
            var diagnostics = Validate(CreateMember(
                FoxRunRos2ContractKind.CustomDto,
                shape,
                mode: 2,
                encoding: FoxRunGenerationDescriptorConstants.InheritEncoding));

            Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "FOXRUN401");
            Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "FOXRUN402");
            Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "FOXRUN205");
            Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "FOXRUN206");
        }

        [Fact]
        public void PackagedNativePublishAndSubscribeKeepsThePhase179Foxrun205TextAndMeaning()
        {
            var packagedShape = new FoxRunRos2MessageShape(
                "global::Example.Packaged",
                "example_msgs/msg/Packaged",
                hasPublicParameterlessConstructor: true,
                implementsRos2Message: true,
                copyShapeIdentity: "packaged",
                members: Array.Empty<FoxRunRos2MessageMemberShape>(),
                diagnostics: Array.Empty<string>());
            var member = new FoxRunGenerationMember(
                ns: "Example",
                className: "Host",
                memberName: "Incoming",
                memberKind: "field",
                rawObservedTypeName: "Example.Packaged",
                emissionTypeName: "Example.Packaged",
                isValueType: false,
                isArray: false,
                elementTypeName: "",
                topic: "/custom",
                rateHz: 10f,
                schemaName: "",
                publishMode: 0,
                changeEpsilon: 0f,
                forceIntervalSeconds: 0f,
                hostKind: "Test",
                rawMemberOrder: 0,
                conditionalSymbols: "",
                mode: 2,
                encoding: FoxRunGenerationDescriptorConstants.InheritEncoding,
                subscriptionProvider: FoxRunGenerationDescriptorConstants.Ros2NativeSubscriptionProvider,
                generatesWebSocketCodec: true,
                generatesRos2NativeRegistration: true,
                ros2MessageShape: packagedShape,
                ros2ContractKind: FoxRunRos2ContractKind.PackagedRos2Message);

            var diagnostic = Assert.Single(Validate(member), value => value.Id == "FOXRUN205");
            Assert.Equal("Ros2Native subscriptions are supported only for SubscribeOnly members.", diagnostic.Message);
        }

        [Fact]
        public void IncompleteCustomNativeBidirectionalContractFailsClosedWithoutFoxrun205()
        {
            var shape = FoxRunReflectionRos2CustomDtoShapeBuilder.Build(typeof(InvalidDto));
            var diagnostics = Validate(CreateMember(
                FoxRunRos2ContractKind.CustomDto,
                shape,
                mode: 2,
                encoding: FoxRunGenerationDescriptorConstants.JsonEncoding));

            Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "FOXRUN402");
            Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "FOXRUN606");
            Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "FOXRUN205");
            Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "FOXRUN006");
        }

        [Fact]
        public void NativeSubscriptionProviderIsRejectedForPublishOnlyEvenWhenTheDtoShapeIsValid()
        {
            var diagnostics = Validate(CreateMember(
                FoxRunRos2ContractKind.CustomDto,
                FoxRunReflectionRos2CustomDtoShapeBuilder.Build(typeof(ValidDto)),
                mode: 0,
                encoding: FoxRunGenerationDescriptorConstants.InheritEncoding));

            Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "FOXRUN214");
        }

        [Fact]
        public void NativeCustomDtoPublishAndSubscribeRetainsOnlyItsWebSocketOutputContract()
        {
            var shape = FoxRunReflectionRos2CustomDtoShapeBuilder.Build(typeof(ValidDto));
            var member = new FoxRunManifestMember(
                "Example", "Host", "Incoming", "field", typeof(ValidDto).FullName, false, false, string.Empty,
                "/custom", 10f, "example/Custom", 0, 0f, 0f,
                flowMode: 2,
                encoding: (int)Unity.FoxgloveSDK.Components.FoxRunWireEncoding.Json,
                subscriptionProvider: FoxRunGenerationDescriptorConstants.Ros2NativeSubscriptionProvider,
                generatesWebSocketCodec: true,
                generatesRos2NativeRegistration: false,
                ros2CustomDtoShape: shape,
                ros2ContractKind: FoxRunRos2ContractKind.CustomDto);

            var manifest = FoxRunManifestBuilder.Build(new[] { member }, manifestVersion: 2);

            Assert.Single(Assert.Single(manifest.Sections.FoxRun.Types).Contracts);
            var binding = Assert.Single(manifest.Sections.Subscriptions.Bindings);
            Assert.True(binding.SupportsWebSocket);
            Assert.False(binding.SupportsRos2Native);
            Assert.Equal(FoxRunRos2ContractKind.CustomDto, binding.Ros2ContractKind);
            Assert.Equal(shape.CanonicalIdentity, binding.CustomDtoIdentity);
            Assert.Equal(shape.PayloadIdentity, binding.CustomPayloadIdentity);
            Assert.Equal(string.Empty, binding.CustomEnvelopeIdentity);
        }

        [Fact]
        public void NativeCustomDtoManifestKeepsPackagedShapeFieldsEmpty()
        {
            var shape = FoxRunReflectionRos2CustomDtoShapeBuilder.Build(typeof(ValidDto));
            var misleadingPackagedShape = new FoxRunRos2MessageShape(
                "global::Example.Legacy",
                "example_msgs/msg/Legacy",
                hasPublicParameterlessConstructor: true,
                implementsRos2Message: true,
                copyShapeIdentity: "legacy-copy-shape",
                members: Array.Empty<FoxRunRos2MessageMemberShape>(),
                diagnostics: Array.Empty<string>());
            var member = new FoxRunManifestMember(
                "Example", "Host", "Incoming", "field", typeof(ValidDto).FullName, false, false, string.Empty,
                "/custom", 10f, "", 0, 0f, 0f,
                flowMode: 2,
                encoding: (int)Unity.FoxgloveSDK.Components.FoxRunWireEncoding.Json,
                subscriptionProvider: FoxRunGenerationDescriptorConstants.Ros2NativeSubscriptionProvider,
                generatesWebSocketCodec: true,
                generatesRos2NativeRegistration: true,
                ros2MessageShape: misleadingPackagedShape,
                ros2CustomDtoShape: shape,
                ros2ContractKind: FoxRunRos2ContractKind.CustomDto);

            var binding = Assert.Single(FoxRunManifestBuilder.Build(new[] { member }, manifestVersion: 2)
                .Sections.Subscriptions.Bindings);

            Assert.True(binding.SupportsRos2Native);
            Assert.Equal(shape.FullyQualifiedTypeName, binding.NativeType);
            Assert.Equal(string.Empty, binding.CanonicalRosType);
            Assert.Equal(string.Empty, binding.CopyShapeIdentity);
            Assert.Equal(shape.CanonicalIdentity, binding.CustomDtoIdentity);
            Assert.Equal(shape.PayloadIdentity, binding.CustomPayloadIdentity);
            Assert.Equal(
                Unity.FoxgloveSDK.Components.FoxRunRos2InterfaceIdentity.BuildEnvelopeMessageName(shape.PayloadIdentity),
                binding.CustomEnvelopeIdentity);
            Assert.Contains(
                "\"customEnvelopeIdentity\":\"" + binding.CustomEnvelopeIdentity + "\"",
                FoxRunManifestJsonWriter.WriteCanonical(FoxRunManifestBuilder.Build(new[] { member }, manifestVersion: 2)),
                StringComparison.Ordinal);
        }

        private static FoxRunGenerationMember CreateMember(
            FoxRunRos2ContractKind contractKind,
            FoxRunRos2CustomDtoShape shape,
            int mode,
            string encoding)
        {
            return new FoxRunGenerationMember(
                ns: "Example",
                className: "Host",
                memberName: "Incoming",
                memberKind: "field",
                rawObservedTypeName: typeof(ValidDto).FullName,
                emissionTypeName: typeof(ValidDto).FullName,
                isValueType: false,
                isArray: false,
                elementTypeName: "",
                topic: "/custom",
                rateHz: 10f,
                schemaName: "",
                publishMode: 0,
                changeEpsilon: 0f,
                forceIntervalSeconds: 0f,
                hostKind: "Test",
                rawMemberOrder: 0,
                conditionalSymbols: "",
                mode: mode,
                encoding: encoding,
                subscriptionProvider: FoxRunGenerationDescriptorConstants.Ros2NativeSubscriptionProvider,
                generatesWebSocketCodec: true,
                generatesRos2NativeRegistration: false,
                ros2CustomDtoShape: shape,
                ros2ContractKind: contractKind);
        }

        private static FoxRunGenerationDiagnostic[] Validate(FoxRunGenerationMember member)
            => FoxRunGenerationModelValidator.Validate(
                    FoxRunGenerationModel.FromMembers(new[] { member }))
                .ToArray();

        public sealed class ValidDto
        {
            public int Count;
        }

        public sealed class InvalidDto
        {
            public decimal Lossy;
        }
    }
}
