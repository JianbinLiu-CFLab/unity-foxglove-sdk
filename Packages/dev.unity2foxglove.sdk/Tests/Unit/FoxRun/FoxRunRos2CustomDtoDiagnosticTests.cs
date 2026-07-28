// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.SourceGenerators;
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
                mode: (int)FoxRunFlow.PublishAndSubscribe,
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
                mode: (int)FoxRunFlow.PublishAndSubscribe,
                encoding: FoxRunGenerationDescriptorConstants.InheritEncoding));

            Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "FOXRUN401");
            Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "FOXRUN402");
            Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "FOXRUN205");
            Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "FOXRUN206");
        }

        [Fact]
        public void NativeCustomDtoPreservesOfficialQosWithoutSilentDowngrade()
        {
            const string source = @"
using Unity.FoxgloveSDK.Components;

namespace Demo
{
    public sealed class Payload
    {
        public int Count;
    }

    public partial class Host
    {
        [FoxRun(""/custom-valid"", Mode = FoxRunFlow.PublishAndSubscribe,
            Source = FoxRunEndpoint.Ros2Native,
            Targets = FoxRunEndpoint.Ros2Native | FoxRunEndpoint.Ros2Bridge,
            QoS = FoxRunQosProfile.SystemDefault,
            Reliability = FoxRunQosReliability.SystemDefault,
            Durability = FoxRunQosDurability.TransientLocal,
            History = FoxRunQosHistory.KeepLast,
            Depth = 17)]
        private Payload _payload = new Payload();
    }
}";
            var model = FoxRunRoslynGenerationModelLowerer.Lower(
                ExtractRoslynMemberData(source).ToRoslynMembers());
            var member = Assert.Single(Assert.Single(model.Types).Members);

            Assert.Equal(FoxRunRos2ContractKind.CustomDto, member.Ros2ContractKind);
            Assert.NotNull(member.Ros2CustomDtoShape);
            Assert.True(member.Ros2CustomDtoShape.IsSupported);
            Assert.Equal(FoxRunGenerationDescriptorConstants.SystemDefaultQosProfile, member.QosProfile);
            Assert.Equal(FoxRunGenerationDescriptorConstants.SystemDefaultQosPolicy, member.QosReliability);
            Assert.Equal(FoxRunGenerationDescriptorConstants.TransientLocalQosDurability, member.QosDurability);
            Assert.Equal(FoxRunGenerationDescriptorConstants.KeepLastQosHistory, member.QosHistory);
            Assert.Equal(17, member.QosDepth);
            Assert.DoesNotContain(
                FoxRunGenerationModelValidator.Validate(model),
                diagnostic => diagnostic.Id == "FOXRUN613" || diagnostic.Id == "FOXRUN614");
            var result = RunGenerator(source);
            Assert.DoesNotContain(
                result.Diagnostics,
                diagnostic => diagnostic.Id == "FOXRUN613" || diagnostic.Id == "FOXRUN614");

            var binding = Assert.Single(
                FoxRunManifestBuilder.Build(
                    new[] { FoxRunManifestMember.FromGenerationMember(member) },
                    manifestVersion: FoxrunManifestWriter.CurrentManifestVersion)
                .Sections.Subscriptions.Bindings);
            Assert.Equal(member.QosProfile, binding.QosProfile);
            Assert.Equal(member.QosReliability, binding.QosReliability);
            Assert.Equal(member.QosDurability, binding.QosDurability);
            Assert.Equal(member.QosHistory, binding.QosHistory);
            Assert.Equal(17, binding.QosDepth);
        }

        [Fact]
        public void NativeCustomDtoInvalidOfficialQosFailsClosedWithoutFallback()
        {
            const string source = @"
using Unity.FoxgloveSDK.Components;

namespace Demo
{
    public sealed class Payload
    {
        public int Count;
    }

    public partial class Host
    {
        [FoxRun(""/custom-invalid"", Mode = FoxRunFlow.PublishAndSubscribe,
            Source = FoxRunEndpoint.Ros2Native,
            Targets = FoxRunEndpoint.Ros2Native | FoxRunEndpoint.Ros2Bridge,
            QoS = FoxRunQosProfile.SystemDefault,
            History = FoxRunQosHistory.KeepAll,
            Depth = 5)]
        private Payload _payload = new Payload();
    }
}";
            var model = FoxRunRoslynGenerationModelLowerer.Lower(
                ExtractRoslynMemberData(source).ToRoslynMembers());
            var member = Assert.Single(Assert.Single(model.Types).Members);
            Assert.Equal(FoxRunRos2ContractKind.CustomDto, member.Ros2ContractKind);
            Assert.NotNull(member.Ros2CustomDtoShape);
            Assert.True(member.Ros2CustomDtoShape.IsSupported);
            var modelDiagnostic = Assert.Single(
                FoxRunGenerationModelValidator.Validate(model),
                candidate => candidate.Id == "FOXRUN613");
            Assert.Contains("KeepLast", modelDiagnostic.Message, StringComparison.Ordinal);

            var result = RunGenerator(source);
            Assert.Single(result.Diagnostics, candidate => candidate.Id == "FOXRUN613");
            var descriptorJson = GeneratedDescriptorJson(result);
            using var descriptor = JsonDocument.Parse(descriptorJson);
            Assert.Equal(0, descriptor.RootElement.GetProperty("types").GetArrayLength());
            Assert.DoesNotContain(
                result.Results.Single().GeneratedSources,
                generated => generated.SourceText.ToString()
                    .Contains("/custom-invalid", StringComparison.Ordinal));
        }

        [Fact]
        public void PackagedNativePublishAndSubscribeIsLegalWithDirectionalSource()
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
                hz: 10f,
                schemaName: "",
                policy: (int)FoxRunPolicy.FixedRate,
                tolerance: 0f,
                hostKind: "Test",
                rawMemberOrder: 0,
                conditionalSymbols: "",
                mode: (int)FoxRunFlow.PublishAndSubscribe,
                encoding: FoxRunGenerationDescriptorConstants.InheritEncoding,
                source: FoxRunGenerationDescriptorConstants.Ros2NativeSource,
                generatesWebSocketCodec: true,
                generatesRos2NativeRegistration: true,
                ros2MessageShape: packagedShape,
                ros2ContractKind: FoxRunRos2ContractKind.PackagedRos2Message);

            var diagnostics = Validate(member);

            Assert.DoesNotContain(diagnostics, value => value.Id == "FOXRUN205");
            Assert.DoesNotContain(diagnostics, value => value.Id == "FOXRUN612");
        }

        [Fact]
        public void IncompleteCustomNativeBidirectionalContractFailsClosedWithoutLegacyDirectionDiagnostics()
        {
            var shape = FoxRunReflectionRos2CustomDtoShapeBuilder.Build(typeof(InvalidDto));
            var diagnostics = Validate(CreateMember(
                FoxRunRos2ContractKind.CustomDto,
                shape,
                mode: (int)FoxRunFlow.PublishAndSubscribe,
                encoding: FoxRunGenerationDescriptorConstants.JsonEncoding));

            Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "FOXRUN402");
            Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "FOXRUN606");
            Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "FOXRUN205");
            Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "FOXRUN206");
            Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "FOXRUN214");
            Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "FOXRUN006");
        }

        [Fact]
        public void NativeSourceIsRejectedForPublishEvenWhenTheDtoShapeIsValid()
        {
            var diagnostics = Validate(CreateMember(
                FoxRunRos2ContractKind.CustomDto,
                FoxRunReflectionRos2CustomDtoShapeBuilder.Build(typeof(ValidDto)),
                mode: (int)FoxRunFlow.Publish,
                encoding: FoxRunGenerationDescriptorConstants.InheritEncoding));

            Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "FOXRUN612");
            Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "FOXRUN214");
        }

        [Fact]
        public void NativeCustomDtoPublishAndSubscribeRetainsOnlyItsWebSocketOutputContract()
        {
            var shape = FoxRunReflectionRos2CustomDtoShapeBuilder.Build(typeof(ValidDto));
            var member = new FoxRunManifestMember(
                "Example", "Host", "Incoming", "field", typeof(ValidDto).FullName, false, false, string.Empty,
                "/custom", 10f, "example/Custom", (int)FoxRunPolicy.FixedRate, 0f,
                flow: (int)FoxRunFlow.PublishAndSubscribe,
                encoding: (int)FoxRunEncoding.JSON,
                source: FoxRunGenerationDescriptorConstants.Ros2NativeSource,
                generatesWebSocketCodec: true,
                generatesRos2NativeRegistration: false,
                ros2CustomDtoShape: shape,
                ros2ContractKind: FoxRunRos2ContractKind.CustomDto);

            var manifest = FoxRunManifestBuilder.Build(
                new[] { member },
                manifestVersion: FoxrunManifestWriter.CurrentManifestVersion);

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
                "/custom", 10f, "", (int)FoxRunPolicy.FixedRate, 0f,
                flow: (int)FoxRunFlow.PublishAndSubscribe,
                encoding: (int)FoxRunEncoding.JSON,
                source: FoxRunGenerationDescriptorConstants.Ros2NativeSource,
                generatesWebSocketCodec: true,
                generatesRos2NativeRegistration: true,
                ros2MessageShape: misleadingPackagedShape,
                ros2CustomDtoShape: shape,
                ros2ContractKind: FoxRunRos2ContractKind.CustomDto);

            var binding = Assert.Single(FoxRunManifestBuilder.Build(
                    new[] { member },
                    manifestVersion: FoxrunManifestWriter.CurrentManifestVersion)
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
                FoxRunManifestJsonWriter.WriteCanonical(FoxRunManifestBuilder.Build(
                    new[] { member },
                    manifestVersion: FoxrunManifestWriter.CurrentManifestVersion)),
                StringComparison.Ordinal);
        }

        private static CSharpCompilation CreateCompilation(string source)
        {
            var trusted = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
                .Split(System.IO.Path.PathSeparator)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => MetadataReference.CreateFromFile(path));
            var references = trusted
                .Concat(new[] { MetadataReference.CreateFromFile(typeof(FoxRunAttribute).Assembly.Location) })
                .GroupBy(reference => reference.Display, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First());
            return CSharpCompilation.Create(
                "CustomDtoQosProbe",
                new[] { CSharpSyntaxTree.ParseText(source) },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        }

        private static GeneratorDriverRunResult RunGenerator(string source)
        {
            GeneratorDriver driver = CSharpGeneratorDriver.Create(new FoxgloveLogSourceGenerator());
            driver = driver.RunGenerators(CreateCompilation(source));
            return driver.GetRunResult();
        }

        private static string GeneratedDescriptorJson(GeneratorDriverRunResult result)
        {
            var descriptorSource = result.Results
                .Single()
                .GeneratedSources
                .Single(source => source.HintName == "FoxRunGeneratedDescriptorInfo.g.cs")
                .SourceText
                .ToString();
            var descriptorVariable = CSharpSyntaxTree.ParseText(descriptorSource)
                .GetRoot()
                .DescendantNodes()
                .OfType<VariableDeclaratorSyntax>()
                .Single(variable => variable.Identifier.ValueText == "DescriptorJson");
            var literal = Assert.IsType<LiteralExpressionSyntax>(
                descriptorVariable.Initializer?.Value);
            return literal.Token.ValueText;
        }

        private static Unity.FoxgloveSDK.SourceGenerators.MemberData ExtractRoslynMemberData(
            string source)
        {
            var compilation = CreateCompilation(source);
            var field = compilation.SyntaxTrees
                .Single()
                .GetRoot()
                .DescendantNodes()
                .OfType<FieldDeclarationSyntax>()
                .Single(field => field.AttributeLists.Count > 0);
            var constructor = typeof(GeneratorSyntaxContext).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                new[] { typeof(SyntaxNode), typeof(SemanticModel) },
                modifiers: null);
            Assert.NotNull(constructor);
            var context = (GeneratorSyntaxContext)constructor.Invoke(
                new object[] { field, compilation.GetSemanticModel(field.SyntaxTree) });
            var extract = typeof(FoxgloveLogSourceGenerator).GetMethod(
                "ExtractMember",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(extract);
            return Assert.IsType<Unity.FoxgloveSDK.SourceGenerators.MemberData>(
                extract.Invoke(
                    null,
                    new object[] { context, System.Threading.CancellationToken.None }));
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
                hz: 10f,
                schemaName: "",
                policy: (int)FoxRunPolicy.FixedRate,
                tolerance: 0f,
                hostKind: "Test",
                rawMemberOrder: 0,
                conditionalSymbols: "",
                mode: mode,
                encoding: encoding,
                source: FoxRunGenerationDescriptorConstants.Ros2NativeSource,
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
