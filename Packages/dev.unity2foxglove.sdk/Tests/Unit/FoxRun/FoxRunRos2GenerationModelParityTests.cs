// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/FoxRun
// Purpose: Pins provider, QoS, capability, and native message-shape parity across generation hosts.

using System;
using System.Linq;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.SourceGenerators;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.FoxRun
{
    [Trait("Phase", "179-B")]
    [Trait("Domain", "FoxRun")]
    public sealed class FoxRunRos2GenerationModelParityTests
    {
        [Theory]
        [InlineData(1, 2, "foxglove-websocket", "reliable", true, true)]
        [InlineData(2, 3, "ros2-native", "sensor-data", true, true)]
        [InlineData(0, 0, "inherit", "inherit", true, true)]
        public void RoslynAndReflectionLowerersPreserveNormalizedProviderCapabilitiesAndNativeShape(
            int provider,
            int qos,
            string expectedProvider,
            string expectedQos,
            bool generatesWebSocketCodec,
            bool generatesRos2NativeRegistration)
        {
            var shape = new FoxRunRos2MessageShape(
                "global::std_msgs.msg.String",
                "std_msgs/msg/String",
                hasPublicParameterlessConstructor: true,
                implementsRos2Message: true,
                copyShapeIdentity: "std_msgs/msg/String|Data:string",
                members: new[]
                {
                    new FoxRunRos2MessageMemberShape(
                        "Data",
                        FoxRunRos2MessageMemberKind.String,
                        "string",
                        sequenceElementTypeName: "",
                        nestedShapeIdentity: "")
                },
                diagnostics: Array.Empty<string>());

            var roslyn = FoxRunRoslynGenerationModelLowerer.Lower(new[]
            {
                new FoxRunRoslynGenerationMember(
                    "Demo", "Receiver", "_incoming", "field",
                    "std_msgs.msg.String", "global::std_msgs.msg.String",
                    false, false, "", "/demo/input", "std_msgs/msg/String",
                    10f, (int)FoxRunPolicy.FixedRate, 0f, 0, "",
                    mode: (int)FoxRunFlow.Subscribe, encoding: provider == 1 ? 2 : 0,
                    protobufTypeShape: BuildStringProtobufShape(),
                    source: provider, ros2Qos: qos,
                    generatesWebSocketCodec: generatesWebSocketCodec,
                    generatesRos2NativeRegistration: generatesRos2NativeRegistration,
                    ros2MessageShape: shape)
            });
            var reflection = FoxRunReflectionGenerationModelLowerer.Lower(new[]
            {
                new FoxRunReflectionGenerationMember(
                    "Demo", "Receiver", "_incoming", "field",
                    "std_msgs.msg.String", "global::std_msgs.msg.String",
                    false, false, "", "/demo/input", "std_msgs/msg/String",
                    10f, (int)FoxRunPolicy.FixedRate, 0f, 0, "",
                    mode: (int)FoxRunFlow.Subscribe, encoding: provider == 1 ? 2 : 0,
                    protobufTypeShape: BuildStringProtobufShape(),
                    source: provider, ros2Qos: qos,
                    generatesWebSocketCodec: generatesWebSocketCodec,
                    generatesRos2NativeRegistration: generatesRos2NativeRegistration,
                    ros2MessageShape: shape)
            });

            var comparison = FoxRunGenerationDescriptorComparer.Compare(roslyn, reflection);
            Assert.True(comparison.IsSemanticEqual, string.Join(Environment.NewLine, comparison.SemanticDifferences));

            var member = Assert.Single(roslyn.Types[0].Members);
            Assert.Equal(expectedProvider, member.Source);
            Assert.Equal(expectedQos, member.Ros2Qos);
            Assert.Equal(generatesWebSocketCodec, member.GeneratesWebSocketCodec);
            Assert.Equal(generatesRos2NativeRegistration, member.GeneratesRos2NativeRegistration);
            Assert.Equal("std_msgs.msg.String", member.EmissionTypeName);
            Assert.Equal("std_msgs/msg/String", member.Ros2MessageShape.CanonicalRosType);
            Assert.Equal("std_msgs/msg/String|Data:string", member.Ros2MessageShape.CopyShapeIdentity);
        }

        [Fact]
        public void DescriptorPersistsProviderCapabilitiesAndCopyShapeIdentity()
        {
            var model = BuildModel(2, 3, true, true, BuildShape("std_msgs/msg/String|Data:string"));
            using var document = JsonDocument.Parse(FoxRunGenerationDescriptorJsonWriter.Write(model));
            var root = document.RootElement;
            var member = root.GetProperty("types")[0].GetProperty("members")[0];

            Assert.Equal(3, root.GetProperty("descriptorVersion").GetInt32());
            Assert.Equal("3.0.0", root.GetProperty("generatorVersion").GetString());
            Assert.Equal("ros2-native", member.GetProperty("source").GetString());
            Assert.Equal("sensor-data", member.GetProperty("ros2Qos").GetString());
            Assert.True(member.GetProperty("generatesWebSocketCodec").GetBoolean());
            Assert.True(member.GetProperty("generatesRos2NativeRegistration").GetBoolean());
            Assert.Equal(
                "std_msgs/msg/String|Data:string",
                member.GetProperty("ros2MessageShape").GetProperty("copyShapeIdentity").GetString());
        }

        [Fact]
        public void RoslynAndReflectionLowerersPreserveCustomDtoContractKindAndSchema()
        {
            var customShape = new FoxRunRos2CustomDtoShape(
                "global::Demo.CustomPayload",
                "Demo.CustomPayload|Count:int32",
                "CustomPayload_12ab34cd56ef",
                hasPublicParameterlessConstructor: true,
                isSupported: true,
                members: new[]
                {
                    new FoxRunRos2CustomDtoMemberShape(
                        "Count", "count", FoxRunRos2CustomDtoMemberKind.Scalar,
                        "int", "int32", "", "", hasPresence: false,
                        canRead: true, canWrite: true)
                },
                diagnostics: Array.Empty<string>());
            var roslyn = FoxRunRoslynGenerationModelLowerer.Lower(new[]
            {
                new FoxRunRoslynGenerationMember(
                    "Demo", "Host", "Payload", "field", "Demo.CustomPayload", "global::Demo.CustomPayload",
                    false, false, "", "/custom", "", 10f, (int)FoxRunPolicy.FixedRate, 0f, 0, "",
                    mode: (int)FoxRunFlow.PublishAndSubscribe,
                    encoding: 2, source: 2, ros2Qos: 0,
                    generatesWebSocketCodec: true, generatesRos2NativeRegistration: false,
                    ros2CustomDtoShape: customShape,
                    ros2ContractKind: FoxRunRos2ContractKind.CustomDto)
            });
            var reflection = FoxRunReflectionGenerationModelLowerer.Lower(new[]
            {
                new FoxRunReflectionGenerationMember(
                    "Demo", "Host", "Payload", "field", "Demo.CustomPayload", "global::Demo.CustomPayload",
                    false, false, "", "/custom", "", 10f, (int)FoxRunPolicy.FixedRate, 0f, 0, "",
                    mode: (int)FoxRunFlow.PublishAndSubscribe,
                    encoding: 2, source: 2, ros2Qos: 0,
                    generatesWebSocketCodec: true, generatesRos2NativeRegistration: false,
                    ros2CustomDtoShape: customShape,
                    ros2ContractKind: FoxRunRos2ContractKind.CustomDto)
            });

            Assert.True(FoxRunGenerationDescriptorComparer.Compare(roslyn, reflection).IsSemanticEqual);
            using var document = JsonDocument.Parse(FoxRunGenerationDescriptorJsonWriter.Write(roslyn));
            var member = document.RootElement.GetProperty("types")[0].GetProperty("members")[0];
            Assert.Equal("CustomDto", member.GetProperty("ros2ContractKind").GetString());
            Assert.Equal(
                "CustomPayload_12ab34cd56ef",
                member.GetProperty("ros2CustomDtoShape").GetProperty("payloadIdentity").GetString());
        }

        [Fact]
        public void ReadOnlyFixedSequenceShapeRoundTripsAcrossHostsAndDescriptor()
        {
            var shape = BuildImuShape(canWrite: false);
            var roslyn = FoxRunRoslynGenerationModelLowerer.Lower(new[]
            {
                new FoxRunRoslynGenerationMember(
                    "Demo", "Receiver", "_incoming", "field",
                    "sensor_msgs.msg.Imu", "global::sensor_msgs.msg.Imu",
                    false, false, "", "/imu", "sensor_msgs/msg/Imu",
                    10f, (int)FoxRunPolicy.FixedRate, 0f, 0, "",
                    mode: (int)FoxRunFlow.Subscribe, encoding: 0,
                    source: 2, ros2Qos: 3,
                    generatesWebSocketCodec: false,
                    generatesRos2NativeRegistration: true,
                    ros2MessageShape: shape)
            });
            var reflection = FoxRunReflectionGenerationModelLowerer.Lower(new[]
            {
                new FoxRunReflectionGenerationMember(
                    "Demo", "Receiver", "_incoming", "field",
                    "sensor_msgs.msg.Imu", "global::sensor_msgs.msg.Imu",
                    false, false, "", "/imu", "sensor_msgs/msg/Imu",
                    10f, (int)FoxRunPolicy.FixedRate, 0f, 0, "",
                    mode: (int)FoxRunFlow.Subscribe, encoding: 0,
                    source: 2, ros2Qos: 3,
                    generatesWebSocketCodec: false,
                    generatesRos2NativeRegistration: true,
                    ros2MessageShape: shape)
            });

            Assert.True(FoxRunGenerationDescriptorComparer.Compare(roslyn, reflection).IsSemanticEqual);
            using var document = JsonDocument.Parse(FoxRunGenerationDescriptorJsonWriter.Write(roslyn));
            var covariance = document.RootElement.GetProperty("types")[0].GetProperty("members")[0]
                .GetProperty("ros2MessageShape").GetProperty("members")[0];
            Assert.True(covariance.GetProperty("canRead").GetBoolean());
            Assert.False(covariance.GetProperty("canWrite").GetBoolean());
            Assert.Equal("FixedArray", covariance.GetProperty("sequenceRepresentation").GetString());
            Assert.Equal(9, covariance.GetProperty("fixedSize").GetInt32());

            var writable = FoxRunRoslynGenerationModelLowerer.Lower(new[]
            {
                new FoxRunRoslynGenerationMember(
                    "Demo", "Receiver", "_incoming", "field",
                    "sensor_msgs.msg.Imu", "global::sensor_msgs.msg.Imu",
                    false, false, "", "/imu", "sensor_msgs/msg/Imu",
                    10f, (int)FoxRunPolicy.FixedRate, 0f, 0, "",
                    mode: (int)FoxRunFlow.Subscribe, encoding: 0,
                    source: 2, ros2Qos: 3,
                    generatesWebSocketCodec: false,
                    generatesRos2NativeRegistration: true,
                    ros2MessageShape: BuildImuShape(canWrite: true))
            });
            Assert.False(FoxRunGenerationDescriptorComparer.Compare(roslyn, writable).IsSemanticEqual);
        }

        [Theory]
        [InlineData("provider")]
        [InlineData("qos")]
        [InlineData("websocket-capability")]
        [InlineData("native-capability")]
        [InlineData("copy-shape")]
        public void DescriptorComparerTreatsProviderCapabilityAndCopyShapeChangesAsSemantic(string changedField)
        {
            var left = BuildModel(0, 0, true, true, BuildShape("shape-a"));
            var right = changedField switch
            {
                "provider" => BuildModel(1, 0, true, true, BuildShape("shape-a")),
                "qos" => BuildModel(0, 2, true, true, BuildShape("shape-a")),
                "websocket-capability" => BuildModel(0, 0, false, true, BuildShape("shape-a")),
                "native-capability" => BuildModel(0, 0, true, false, BuildShape("shape-a")),
                _ => BuildModel(0, 0, true, true, BuildShape("shape-b"))
            };

            Assert.False(FoxRunGenerationDescriptorComparer.Compare(left, right).IsSemanticEqual);
        }

        [Theory]
        [InlineData("ros2-native")]
        [InlineData("inherit")]
        public void NativeProviderOrInheritedValidNativeCapabilityDoesNotRequireAWebSocketShape(
            string source)
        {
            var nativeOnly = new FoxRunGenerationMember(
                "Demo", "Receiver", "_incoming", "field",
                "vendor_msgs.msg.NativeOnly", "global::vendor_msgs.msg.NativeOnly", "vendor_msgs.msg.NativeOnly",
                false, false, "", "/demo/input", 10f, "vendor_msgs/msg/NativeOnly",
                (int)FoxRunPolicy.FixedRate, 0f,
                "Roslyn", 1, "", mode: (int)FoxRunFlow.Subscribe,
                encoding: FoxRunGenerationDescriptorConstants.InheritEncoding,
                source: source,
                ros2Qos: FoxRunGenerationDescriptorConstants.SensorDataRos2Qos,
                generatesWebSocketCodec: false,
                generatesRos2NativeRegistration: true,
                ros2MessageShape: BuildShape(
                    "native-only",
                    "global::vendor_msgs.msg.NativeOnly",
                    "vendor_msgs/msg/NativeOnly"));
            var model = FoxRunGenerationModel.FromMembers(new[] { nativeOnly });

            Assert.DoesNotContain(
                FoxRunGenerationModelValidator.Validate(model),
                diagnostic => diagnostic.Severity == "Error");
        }

        [Fact]
        public void InheritedOrdinaryDtoCanRemainWebSocketOnlyAndFailClosedForNative()
        {
            var protobufShape = FoxRunProtobufTypeShape.Object(
                "Demo.CommandDto",
                Array.Empty<FoxRunProtobufTypeField>());
            var member = new FoxRunGenerationMember(
                "Demo", "Receiver", "_incoming", "field",
                "Demo.CommandDto", "global::Demo.CommandDto", "Demo.CommandDto",
                false, false, "", "/demo/input", 10f, "demo/CommandDto",
                (int)FoxRunPolicy.FixedRate, 0f,
                "Roslyn", 1, "", mode: (int)FoxRunFlow.Subscribe,
                encoding: FoxRunGenerationDescriptorConstants.ProtobufEncoding,
                protobufTypeShape: protobufShape,
                source: FoxRunGenerationDescriptorConstants.InheritSource,
                ros2Qos: FoxRunGenerationDescriptorConstants.InheritRos2Qos,
                generatesWebSocketCodec: true,
                generatesRos2NativeRegistration: false);
            var model = FoxRunGenerationModel.FromMembers(new[] { member });

            Assert.DoesNotContain(
                FoxRunGenerationModelValidator.Validate(model),
                diagnostic => diagnostic.Severity == "Error");
            Assert.True(member.GeneratesWebSocketCodec);
            Assert.False(member.GeneratesRos2NativeRegistration);
            Assert.Null(member.Ros2MessageShape);
        }

        [Theory]
        [InlineData("Unity.Collections.NativeArray<float>", "Unity native container", 0)]
        [InlineData("Demo.UnknownDto", "is not a canonical built-in contract type", 0)]
        [InlineData("Unity.Collections.NativeArray<float>", "Unity native container", 1)]
        public void UnknownMemberDataThatRequiresWebSocketShapeKeepsDetailedDiagnostic(
            string typeName,
            string expectedDiagnosticText,
            int source)
        {
            var topic = new TopicEntry(
                "/demo/input", 10f, "demo/Unknown", (int)FoxRunPolicy.FixedRate, 0f,
                mode: (int)FoxRunFlow.Subscribe, encoding: 0,
                source: source, ros2Qos: 0);
            var sourceData = new Unity.FoxgloveSDK.SourceGenerators.MemberData(
                "Demo", "Receiver", true, "_incoming", "field",
                typeName, "global::" + typeName,
                false, false, "", 1, Location.None, new[] { topic });
            var reflectionData = new FoxrunCodeGenerator.MemberData(
                "_incoming", typeName, "/demo/input", 10f, "demo/Unknown",
                mode: (int)FoxRunFlow.Subscribe, encoding: 0,
                source: source, ros2Qos: 0);
            var models = new[]
            {
                FoxRunRoslynGenerationModelLowerer.Lower(sourceData.ToRoslynMembers()),
                FoxRunReflectionGenerationModelLowerer.Lower(new[] { reflectionData.ToReflectionMember() })
            };

            foreach (var model in models)
            {
                var member = Assert.Single(model.Types.Single().Members);
                Assert.False(member.GeneratesWebSocketCodec);
                Assert.False(member.GeneratesRos2NativeRegistration);
                Assert.Contains(
                    FoxRunGenerationModelValidator.Validate(model),
                    diagnostic => diagnostic.Id == "FOXRUN006"
                        && diagnostic.Message.Contains(expectedDiagnosticText, StringComparison.Ordinal));
            }
        }

        [Fact]
        public void SourceAndReflectionMemberDataCarryProviderQosAndShapeIntoLowerers()
        {
            var shape = BuildShape("source-chain");
            var fixtureTypeName = typeof(ReflectionRos2StringFixture).FullName;
            var topic = new TopicEntry(
                "/demo/input", 10f, "std_msgs/msg/String", (int)FoxRunPolicy.FixedRate, 0f,
                mode: (int)FoxRunFlow.Subscribe, encoding: 0, source: 2, ros2Qos: 3);
            var reflectedData = new FoxrunCodeGenerator.MemberData(
                "_incoming", typeof(ReflectionRos2StringFixture), "field", "Demo", "Receiver",
                "/demo/input", 10f, "std_msgs/msg/String", mode: (int)FoxRunFlow.Subscribe, encoding: 0,
                source: 2, ros2Qos: 3, ros2MessageShape: shape);
            var sourceData = new Unity.FoxgloveSDK.SourceGenerators.MemberData(
                "Demo", "Receiver", true, "_incoming", "field",
                fixtureTypeName, "global::" + fixtureTypeName,
                false, false, "", 1, Location.None, new[] { topic },
                protobufTypeShape: reflectedData.ProtobufTypeShape,
                ros2MessageShape: shape);

            var roslyn = FoxRunRoslynGenerationModelLowerer.Lower(sourceData.ToRoslynMembers());
            var reflection = FoxRunReflectionGenerationModelLowerer.Lower(new[] { reflectedData.ToReflectionMember() });
            var comparison = FoxRunGenerationDescriptorComparer.Compare(roslyn, reflection);

            Assert.True(comparison.IsSemanticEqual, string.Join(Environment.NewLine, comparison.SemanticDifferences));
            var member = Assert.Single(roslyn.Types.Single().Members);
            Assert.Equal("ros2-native", member.Source);
            Assert.Equal("sensor-data", member.Ros2Qos);
            Assert.Equal("source-chain", member.Ros2MessageShape.CopyShapeIdentity);
        }

        [Fact]
        public void ExplicitNativeDualCapabilityFromBothHostsNeverProducesWireManifest()
        {
            var shape = BuildShape("explicit-native-dual");
            var fixtureTypeName = typeof(ReflectionRos2StringFixture).FullName;
            var topic = new TopicEntry(
                "/demo/native", 10f, "std_msgs/msg/String", (int)FoxRunPolicy.FixedRate, 0f,
                mode: (int)FoxRunFlow.Subscribe, encoding: 0, source: 2, ros2Qos: 3);
            var reflectionData = new FoxrunCodeGenerator.MemberData(
                "_incoming", typeof(ReflectionRos2StringFixture), "field", "Demo", "Receiver",
                "/demo/native", 10f, "std_msgs/msg/String", mode: (int)FoxRunFlow.Subscribe, encoding: 0,
                source: 2, ros2Qos: 3, ros2MessageShape: shape);
            var sourceData = new Unity.FoxgloveSDK.SourceGenerators.MemberData(
                "Demo", "Receiver", true, "_incoming", "field",
                fixtureTypeName, "global::" + fixtureTypeName,
                false, false, "", 1, Location.None, new[] { topic },
                protobufTypeShape: reflectionData.ProtobufTypeShape,
                ros2MessageShape: shape);
            var roslynMember = Assert.Single(
                Assert.Single(FoxRunRoslynGenerationModelLowerer.Lower(sourceData.ToRoslynMembers()).Types).Members);
            var reflectionMember = reflectionData.ToManifestMember();

            Assert.True(roslynMember.GeneratesWebSocketCodec);
            Assert.True(roslynMember.GeneratesRos2NativeRegistration);
            Assert.True(reflectionMember.GeneratesWebSocketCodec);
            Assert.True(reflectionMember.GeneratesRos2NativeRegistration);

            var roslynManifest = FoxRunManifestBuilder.Build(
                new[] { FoxRunManifestMember.FromGenerationMember(roslynMember) },
                manifestVersion: 2);
            var reflectionManifest = FoxRunManifestBuilder.Build(
                new[] { reflectionMember },
                manifestVersion: 2);

            Assert.Empty(roslynManifest.Sections.FoxRun.Types);
            Assert.Empty(reflectionManifest.Sections.FoxRun.Types);
            Assert.Equal(
                FoxRunManifestJsonWriter.WriteCanonical(roslynManifest),
                FoxRunManifestJsonWriter.WriteCanonical(reflectionManifest));
            var binding = Assert.Single(roslynManifest.Sections.Subscriptions.Bindings);
            Assert.Equal(FoxRunGenerationDescriptorConstants.Ros2NativeSource, binding.DeclaredSource);
            Assert.True(binding.SupportsWebSocket);
            Assert.True(binding.SupportsRos2Native);
        }

        [Fact]
        public void RoslynAndReflectionManifestProjectionProduceIdenticalCanonicalV2Evidence()
        {
            var shape = BuildShape("manifest-parity");
            var fixtureTypeName = typeof(ReflectionRos2StringFixture).FullName;
            var topic = new TopicEntry(
                "/demo/input", 10f, "std_msgs/msg/String", (int)FoxRunPolicy.FixedRate, 0f,
                mode: (int)FoxRunFlow.Subscribe, encoding: 0, source: 0, ros2Qos: 3);
            var reflectionData = new FoxrunCodeGenerator.MemberData(
                "_incoming", typeof(ReflectionRos2StringFixture), "field", "Demo", "Receiver",
                "/demo/input", 10f, "std_msgs/msg/String", mode: (int)FoxRunFlow.Subscribe, encoding: 0,
                source: 0, ros2Qos: 3, ros2MessageShape: shape);
            var sourceData = new Unity.FoxgloveSDK.SourceGenerators.MemberData(
                "Demo", "Receiver", true, "_incoming", "field",
                fixtureTypeName, "global::" + fixtureTypeName,
                false, false, "", 1, Location.None, new[] { topic },
                protobufTypeShape: reflectionData.ProtobufTypeShape,
                ros2MessageShape: shape);

            var roslynModel = FoxRunRoslynGenerationModelLowerer.Lower(sourceData.ToRoslynMembers());
            var roslynMember = Assert.Single(Assert.Single(roslynModel.Types).Members);
            var roslynManifest = FoxRunManifestBuilder.Build(
                new[] { FoxRunManifestMember.FromGenerationMember(roslynMember) },
                manifestVersion: 2);
            var reflectionManifest = FoxRunManifestBuilder.Build(
                new[] { reflectionData.ToManifestMember() },
                manifestVersion: 2);

            Assert.Equal(
                FoxRunManifestJsonWriter.WriteCanonical(roslynManifest),
                FoxRunManifestJsonWriter.WriteCanonical(reflectionManifest));
            Assert.Equal(
                roslynManifest.Sections.Subscriptions.ManifestHash,
                reflectionManifest.Sections.Subscriptions.ManifestHash);
            Assert.Equal(roslynManifest.GlobalManifestHash, reflectionManifest.GlobalManifestHash);
            Assert.Single(roslynManifest.Sections.Subscriptions.Bindings);
        }

        [Fact]
        public void SourceGeneratorExtractsDeclaredSourceAndQosIntoNormalizedDescriptorModel()
        {
            var compilation = CSharpCompilation.Create(
                "Phase179ProviderExtraction",
                new[]
                {
                    CSharpSyntaxTree.ParseText(@"
using Unity.FoxgloveSDK.Components;
namespace Demo
{
    public partial class Receiver
    {
        [FoxRun(""/demo/input"", Mode = FoxRunFlow.Subscribe, Encoding = FoxRunEncoding.JSON,
            Source = FoxRunEndpoint.Foxglove,
            Ros2Qos = FoxRunRos2QosPreset.Reliable)]
        private string _incoming;
    }
}")
                },
                GeneratorReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(new FoxgloveLogSourceGenerator());
            var result = driver.RunGenerators(compilation).GetRunResult();
            var descriptor = result.Results.Single().GeneratedSources
                .Single(source => source.HintName == "FoxRunGeneratedDescriptorInfo.g.cs")
                .SourceText
                .ToString();

            Assert.Contains("\\\"source\\\":\\\"foxglove-websocket\\\"", descriptor, StringComparison.Ordinal);
            Assert.Contains("\\\"ros2Qos\\\":\\\"reliable\\\"", descriptor, StringComparison.Ordinal);
            Assert.Contains("\\\"generatesWebSocketCodec\\\":true", descriptor, StringComparison.Ordinal);
            Assert.Contains("\\\"generatesRos2NativeRegistration\\\":false", descriptor, StringComparison.Ordinal);
        }

        [Fact]
        public void DuplicateTopicDeclarationIsNotSeparatedByProvider()
        {
            var first = Assert.Single(BuildModel(1, 0, true, false, BuildShape("same")).Types[0].Members);
            var secondModel = BuildModel(2, 0, false, true, BuildShape("same"));
            var second = Assert.Single(secondModel.Types[0].Members);
            var duplicate = FoxRunGenerationModel.FromMembers(new[] { first, second });

            var comparison = FoxRunGenerationDescriptorComparer.Compare(duplicate, duplicate);

            Assert.False(comparison.IsSemanticEqual);
            Assert.Contains(comparison.SemanticDifferences, difference => difference.Contains("Duplicate", StringComparison.Ordinal));
        }

        private static FoxRunGenerationModel BuildModel(
            int provider,
            int qos,
            bool generatesWebSocketCodec,
            bool generatesRos2NativeRegistration,
            FoxRunRos2MessageShape shape)
            => FoxRunRoslynGenerationModelLowerer.Lower(new[]
            {
                new FoxRunRoslynGenerationMember(
                    "Demo", "Receiver", "_incoming", "field",
                    "std_msgs.msg.String", "global::std_msgs.msg.String",
                    false, false, "", "/demo/input", "std_msgs/msg/String",
                    10f, (int)FoxRunPolicy.FixedRate, 0f, 0, "",
                    mode: (int)FoxRunFlow.Subscribe, encoding: provider == 1 ? 2 : 0,
                    protobufTypeShape: BuildStringProtobufShape(),
                    source: provider, ros2Qos: qos,
                    generatesWebSocketCodec: generatesWebSocketCodec,
                    generatesRos2NativeRegistration: generatesRos2NativeRegistration,
                    ros2MessageShape: shape)
            });

        private static FoxRunRos2MessageShape BuildShape(
            string copyShapeIdentity,
            string fullyQualifiedTypeName = "global::std_msgs.msg.String",
            string canonicalRosType = "std_msgs/msg/String")
            => new(
                fullyQualifiedTypeName,
                canonicalRosType,
                hasPublicParameterlessConstructor: true,
                implementsRos2Message: true,
                copyShapeIdentity: copyShapeIdentity,
                members: new[]
                {
                    new FoxRunRos2MessageMemberShape(
                        "Data",
                        FoxRunRos2MessageMemberKind.String,
                        "string",
                        sequenceElementTypeName: "",
                        nestedShapeIdentity: "")
                },
                diagnostics: Array.Empty<string>());

        private static FoxRunProtobufTypeShape BuildStringProtobufShape()
            => FoxRunProtobufTypeShape.Object(
                "std_msgs.msg.String",
                new[]
                {
                    new FoxRunProtobufTypeField(
                        "data",
                        "Data",
                        FoxRunProtobufTypeShape.Canonical("string"))
                });

        private static FoxRunRos2MessageShape BuildImuShape(bool canWrite)
            => new(
                "global::sensor_msgs.msg.Imu",
                "sensor_msgs/msg/Imu",
                hasPublicParameterlessConstructor: true,
                implementsRos2Message: true,
                copyShapeIdentity: "sensor_msgs/msg/Imu|Orientation_covariance:fixed-array<double,9>:" + (canWrite ? "settable" : "get-only"),
                members: new[]
                {
                    new FoxRunRos2MessageMemberShape(
                        "Orientation_covariance",
                        FoxRunRos2MessageMemberKind.Sequence,
                        "double[]",
                        sequenceElementTypeName: "double",
                        nestedShapeIdentity: "",
                        canRead: true,
                        canWrite: canWrite,
                        sequenceRepresentation: FoxRunRos2SequenceRepresentation.FixedArray,
                        fixedSize: 9)
                },
                diagnostics: Array.Empty<string>());

        private static MetadataReference[] GeneratorReferences()
        {
            var trusted = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
                .Split(System.IO.Path.PathSeparator)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => MetadataReference.CreateFromFile(path));
            return trusted
                .Concat(new[] { MetadataReference.CreateFromFile(typeof(FoxRunAttribute).Assembly.Location) })
                .GroupBy(reference => reference.Display, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray();
        }

        private sealed class ReflectionRos2StringFixture
        {
            public string Data { get; set; }
        }
    }
}
