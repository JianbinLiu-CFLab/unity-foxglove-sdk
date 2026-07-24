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
        [InlineData(2, 1, "ros2-native", "default", true, true)]
        [InlineData(2, 2, "ros2-native", "sensor-data", true, true)]
        [InlineData(2, 3, "ros2-native", "system-default", true, true)]
        [InlineData(0, 0, "inherit", "inherit", true, true)]
        public void RoslynAndReflectionLowerersPreserveNormalizedProviderCapabilitiesAndNativeShape(
            int provider,
            int qosProfile,
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
                    source: provider, qosProfile: qosProfile,
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
                    source: provider, qosProfile: qosProfile,
                    generatesWebSocketCodec: generatesWebSocketCodec,
                    generatesRos2NativeRegistration: generatesRos2NativeRegistration,
                    ros2MessageShape: shape)
            });

            var comparison = FoxRunGenerationDescriptorComparer.Compare(roslyn, reflection);
            Assert.True(comparison.IsSemanticEqual, string.Join(Environment.NewLine, comparison.SemanticDifferences));

            var member = Assert.Single(roslyn.Types[0].Members);
            Assert.Equal(expectedProvider, member.Source);
            Assert.Equal(expectedQos, member.QosProfile);
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

            Assert.Equal(FoxRunGenerationDescriptorConstants.DescriptorVersion, root.GetProperty("descriptorVersion").GetInt32());
            Assert.Equal(FoxRunGenerationDescriptorConstants.GeneratorVersion, root.GetProperty("generatorVersion").GetString());
            Assert.Equal("ros2-native", member.GetProperty("source").GetString());
            Assert.Equal("system-default", member.GetProperty("qosProfile").GetString());
            Assert.True(member.GetProperty("generatesWebSocketCodec").GetBoolean());
            Assert.True(member.GetProperty("generatesRos2NativeRegistration").GetBoolean());
            Assert.Equal(
                "std_msgs/msg/String|Data:string",
                member.GetProperty("ros2MessageShape").GetProperty("copyShapeIdentity").GetString());
        }

        [Fact]
        public void FullDuplexQosFlowsIdenticallyToEveryRos2DirectionAcrossGenerationHosts()
        {
            const FoxRunNamedArgumentPresence presence =
                FoxRunNamedArgumentPresence.Mode
                | FoxRunNamedArgumentPresence.Source
                | FoxRunNamedArgumentPresence.Targets
                | FoxRunNamedArgumentPresence.QoS
                | FoxRunNamedArgumentPresence.Reliability
                | FoxRunNamedArgumentPresence.Durability
                | FoxRunNamedArgumentPresence.History
                | FoxRunNamedArgumentPresence.Depth;
            var shape = BuildShape("full-duplex-qos");
            var roslyn = FoxRunRoslynGenerationModelLowerer.Lower(new[]
            {
                new FoxRunRoslynGenerationMember(
                    "Demo", "Duplex", "Value", "field",
                    "std_msgs.msg.String", "global::std_msgs.msg.String",
                    false, false, "", "/duplex", "std_msgs/msg/String",
                    10f, (int)FoxRunPolicy.FixedRate, 0f, 0, "",
                    mode: (int)FoxRunFlow.PublishAndSubscribe,
                    source: (int)FoxRunEndpoint.Ros2Native,
                    targets: (int)(FoxRunEndpoint.Ros2Native | FoxRunEndpoint.Ros2Bridge),
                    qosProfile: (int)FoxRunQosProfile.SensorData,
                    qosReliability: (int)FoxRunQosReliability.Reliable,
                    qosDurability: (int)FoxRunQosDurability.TransientLocal,
                    qosHistory: (int)FoxRunQosHistory.KeepLast,
                    qosDepth: 37,
                    namedArgumentPresence: presence,
                    generatesWebSocketCodec: true,
                    generatesRos2NativeRegistration: true,
                    ros2MessageShape: shape)
            });
            var reflection = FoxRunReflectionGenerationModelLowerer.Lower(new[]
            {
                new FoxRunReflectionGenerationMember(
                    "Demo", "Duplex", "Value", "field",
                    "std_msgs.msg.String", "global::std_msgs.msg.String",
                    false, false, "", "/duplex", "std_msgs/msg/String",
                    10f, (int)FoxRunPolicy.FixedRate, 0f, 0, "",
                    mode: (int)FoxRunFlow.PublishAndSubscribe,
                    source: (int)FoxRunEndpoint.Ros2Native,
                    targets: (int)(FoxRunEndpoint.Ros2Native | FoxRunEndpoint.Ros2Bridge),
                    qosProfile: (int)FoxRunQosProfile.SensorData,
                    qosReliability: (int)FoxRunQosReliability.Reliable,
                    qosDurability: (int)FoxRunQosDurability.TransientLocal,
                    qosHistory: (int)FoxRunQosHistory.KeepLast,
                    qosDepth: 37,
                    namedArgumentPresence: presence,
                    generatesWebSocketCodec: true,
                    generatesRos2NativeRegistration: true,
                    ros2MessageShape: shape)
            });

            var comparison = FoxRunGenerationDescriptorComparer.Compare(roslyn, reflection);
            Assert.True(comparison.IsSemanticEqual, string.Join(Environment.NewLine, comparison.SemanticDifferences));
            var member = Assert.Single(Assert.Single(roslyn.Types).Members);
            Assert.Equal("ros2-native", member.Source);
            Assert.Equal("ros2-native,ros2-bridge", member.Targets);
            Assert.Equal("sensor-data", member.QosProfile);
            Assert.Equal("reliable", member.QosReliability);
            Assert.Equal("transient-local", member.QosDurability);
            Assert.Equal("keep-last", member.QosHistory);
            Assert.Equal(37, member.QosDepth);
            Assert.Equal(presence, member.NamedArgumentPresence & presence);
            Assert.DoesNotContain(
                FoxRunGenerationModelValidator.Validate(roslyn),
                diagnostic => diagnostic.Severity == "Error");

            var binding = Assert.Single(
                FoxRunManifestBuilder.Build(
                    new[] { FoxRunManifestMember.FromGenerationMember(member) },
                    manifestVersion: FoxrunManifestWriter.CurrentManifestVersion)
                .Sections.Subscriptions.Bindings);
            Assert.Equal(member.Source, binding.DeclaredSource);
            Assert.Equal(member.Targets, binding.DeclaredTargets);
            Assert.Equal(member.QosProfile, binding.QosProfile);
            Assert.Equal(member.QosReliability, binding.QosReliability);
            Assert.Equal(member.QosDurability, binding.QosDurability);
            Assert.Equal(member.QosHistory, binding.QosHistory);
            Assert.Equal(member.QosDepth, binding.QosDepth);
        }

        [Theory]
        [InlineData(0, 0, 0, 0, 0, "inherit", "inherit", "inherit", "inherit")]
        [InlineData(99, 98, 97, 96, -4, "", "", "", "")]
        public void QosPresenceSurvivesZeroAndInvalidValuesAcrossBothLowerers(
            int profile,
            int reliability,
            int durability,
            int history,
            int depth,
            string expectedProfile,
            string expectedReliability,
            string expectedDurability,
            string expectedHistory)
        {
            const FoxRunNamedArgumentPresence qosPresence =
                FoxRunNamedArgumentPresence.QoS
                | FoxRunNamedArgumentPresence.Reliability
                | FoxRunNamedArgumentPresence.Durability
                | FoxRunNamedArgumentPresence.History
                | FoxRunNamedArgumentPresence.Depth;
            var roslyn = FoxRunRoslynGenerationModelLowerer.Lower(new[]
            {
                new FoxRunRoslynGenerationMember(
                    "Demo", "Presence", "Value", "field", "System.Single", "float",
                    true, false, "", "/qos-presence", "", 10f,
                    (int)FoxRunPolicy.FixedRate, 0f, 0, "",
                    qosProfile: profile,
                    qosReliability: reliability,
                    qosDurability: durability,
                    qosHistory: history,
                    qosDepth: depth,
                    namedArgumentPresence: qosPresence)
            });
            var reflection = FoxRunReflectionGenerationModelLowerer.Lower(new[]
            {
                new FoxRunReflectionGenerationMember(
                    "Demo", "Presence", "Value", "field", "System.Single", "float",
                    true, false, "", "/qos-presence", "", 10f,
                    (int)FoxRunPolicy.FixedRate, 0f, 0, "",
                    qosProfile: profile,
                    qosReliability: reliability,
                    qosDurability: durability,
                    qosHistory: history,
                    qosDepth: depth,
                    namedArgumentPresence: qosPresence)
            });

            var comparison = FoxRunGenerationDescriptorComparer.Compare(roslyn, reflection);
            Assert.True(comparison.IsSemanticEqual, string.Join(Environment.NewLine, comparison.SemanticDifferences));
            var member = Assert.Single(Assert.Single(roslyn.Types).Members);
            Assert.Equal(qosPresence, member.NamedArgumentPresence & qosPresence);
            Assert.Equal(expectedProfile, member.QosProfile);
            Assert.Equal(expectedReliability, member.QosReliability);
            Assert.Equal(expectedDurability, member.QosDurability);
            Assert.Equal(expectedHistory, member.QosHistory);
            Assert.Equal(depth, member.QosDepth);
            Assert.Single(
                FoxRunGenerationModelValidator.Validate(roslyn),
                diagnostic => diagnostic.Id == "FOXRUN613");
        }

        [Fact]
        public void OmittedQosKeepsEveryAxisIndependentlyInheritable()
        {
            var model = BuildModel(0, 0, true, true, BuildShape("omitted-qos"));
            var member = Assert.Single(Assert.Single(model.Types).Members);

            Assert.Equal(FoxRunGenerationDescriptorConstants.InheritQosProfile, member.QosProfile);
            Assert.Equal(FoxRunGenerationDescriptorConstants.InheritQosPolicy, member.QosReliability);
            Assert.Equal(FoxRunGenerationDescriptorConstants.InheritQosPolicy, member.QosDurability);
            Assert.Equal(FoxRunGenerationDescriptorConstants.InheritQosPolicy, member.QosHistory);
            Assert.Equal(0, member.QosDepth);
            Assert.False(member.HasNamedArgument(FoxRunNamedArgumentPresence.QoS));
            Assert.False(member.HasNamedArgument(FoxRunNamedArgumentPresence.Reliability));
            Assert.False(member.HasNamedArgument(FoxRunNamedArgumentPresence.Durability));
            Assert.False(member.HasNamedArgument(FoxRunNamedArgumentPresence.History));
            Assert.False(member.HasNamedArgument(FoxRunNamedArgumentPresence.Depth));

            using var document = JsonDocument.Parse(FoxRunGenerationDescriptorJsonWriter.Write(model));
            var descriptorMember = document.RootElement.GetProperty("types")[0].GetProperty("members")[0];
            Assert.Equal("inherit", descriptorMember.GetProperty("qosProfile").GetString());
            Assert.Equal("inherit", descriptorMember.GetProperty("qosReliability").GetString());
            Assert.Equal("inherit", descriptorMember.GetProperty("qosDurability").GetString());
            Assert.Equal("inherit", descriptorMember.GetProperty("qosHistory").GetString());
            Assert.Equal(0, descriptorMember.GetProperty("qosDepth").GetInt32());
            Assert.Equal(string.Empty, descriptorMember.GetProperty("explicitArguments").GetString());
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
                    encoding: 2, source: 2, qosProfile: 0,
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
                    encoding: 2, source: 2, qosProfile: 0,
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
                    source: 2, qosProfile: 3,
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
                    source: 2, qosProfile: 3,
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
                    source: 2, qosProfile: 3,
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
                qosProfile: FoxRunGenerationDescriptorConstants.SensorDataQosProfile,
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
                qosProfile: FoxRunGenerationDescriptorConstants.InheritQosProfile,
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
                source: source, qosProfile: 0);
            var sourceData = new Unity.FoxgloveSDK.SourceGenerators.MemberData(
                "Demo", "Receiver", true, "_incoming", "field",
                typeName, "global::" + typeName,
                false, false, "", 1, Location.None, new[] { topic });
            var reflectionData = new FoxrunCodeGenerator.MemberData(
                "_incoming", typeName, "/demo/input", 10f, "demo/Unknown",
                mode: (int)FoxRunFlow.Subscribe, encoding: 0,
                source: source, qosProfile: 0);
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
                mode: (int)FoxRunFlow.Subscribe, encoding: 0, source: 2, qosProfile: 3);
            var reflectedData = new FoxrunCodeGenerator.MemberData(
                "_incoming", typeof(ReflectionRos2StringFixture), "field", "Demo", "Receiver",
                "/demo/input", 10f, "std_msgs/msg/String", mode: (int)FoxRunFlow.Subscribe, encoding: 0,
                source: 2, qosProfile: 3, ros2MessageShape: shape);
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
            Assert.Equal("system-default", member.QosProfile);
            Assert.Equal("source-chain", member.Ros2MessageShape.CopyShapeIdentity);
        }

        [Fact]
        public void ExplicitNativeDualCapabilityFromBothHostsNeverProducesWireManifest()
        {
            var shape = BuildShape("explicit-native-dual");
            var fixtureTypeName = typeof(ReflectionRos2StringFixture).FullName;
            var topic = new TopicEntry(
                "/demo/native", 10f, "std_msgs/msg/String", (int)FoxRunPolicy.FixedRate, 0f,
                mode: (int)FoxRunFlow.Subscribe, encoding: 0, source: 2, qosProfile: 3);
            var reflectionData = new FoxrunCodeGenerator.MemberData(
                "_incoming", typeof(ReflectionRos2StringFixture), "field", "Demo", "Receiver",
                "/demo/native", 10f, "std_msgs/msg/String", mode: (int)FoxRunFlow.Subscribe, encoding: 0,
                source: 2, qosProfile: 3, ros2MessageShape: shape);
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
                manifestVersion: FoxrunManifestWriter.CurrentManifestVersion);
            var reflectionManifest = FoxRunManifestBuilder.Build(
                new[] { reflectionMember },
                manifestVersion: FoxrunManifestWriter.CurrentManifestVersion);

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
        public void RoslynAndReflectionManifestProjectionProduceIdenticalCanonicalV3Evidence()
        {
            var shape = BuildShape("manifest-parity");
            var fixtureTypeName = typeof(ReflectionRos2StringFixture).FullName;
            var topic = new TopicEntry(
                "/demo/input", 10f, "std_msgs/msg/String", (int)FoxRunPolicy.FixedRate, 0f,
                mode: (int)FoxRunFlow.Subscribe, encoding: 0, source: 0, qosProfile: 3);
            var reflectionData = new FoxrunCodeGenerator.MemberData(
                "_incoming", typeof(ReflectionRos2StringFixture), "field", "Demo", "Receiver",
                "/demo/input", 10f, "std_msgs/msg/String", mode: (int)FoxRunFlow.Subscribe, encoding: 0,
                source: 0, qosProfile: 3, ros2MessageShape: shape);
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
                manifestVersion: FoxrunManifestWriter.CurrentManifestVersion);
            var reflectionManifest = FoxRunManifestBuilder.Build(
                new[] { reflectionData.ToManifestMember() },
                manifestVersion: FoxrunManifestWriter.CurrentManifestVersion);

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
    public sealed class NativePayload
    {
        public float Value { get; set; }
    }

    public partial class Receiver
    {
        [FoxRun(""/demo/input"", Mode = FoxRunFlow.Subscribe,
            Source = FoxRunEndpoint.Ros2Native,
            QoS = FoxRunQosProfile.SystemDefault)]
        private NativePayload _incoming;
    }
}")
                },
                GeneratorReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(new FoxgloveLogSourceGenerator());
            var result = driver.RunGenerators(compilation).GetRunResult();
            Assert.DoesNotContain(
                result.Diagnostics,
                diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            var descriptor = result.Results.Single().GeneratedSources
                .Single(source => source.HintName == "FoxRunGeneratedDescriptorInfo.g.cs")
                .SourceText
                .ToString();

            Assert.Contains("\\\"source\\\":\\\"ros2-native\\\"", descriptor, StringComparison.Ordinal);
            Assert.Contains("\\\"qosProfile\\\":\\\"system-default\\\"", descriptor, StringComparison.Ordinal);
            Assert.Contains("\\\"generatesWebSocketCodec\\\":true", descriptor, StringComparison.Ordinal);
            Assert.Contains("\\\"generatesRos2NativeRegistration\\\":true", descriptor, StringComparison.Ordinal);
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

        [Theory]
        [InlineData("flow")]
        [InlineData("source")]
        [InlineData("targets")]
        [InlineData("profile")]
        [InlineData("reliability")]
        [InlineData("durability")]
        [InlineData("history")]
        [InlineData("depth")]
        [InlineData("presence")]
        public void SharedTopicRejectsMixedDirectionalQosContract(string mismatch)
        {
            const FoxRunNamedArgumentPresence presence =
                FoxRunNamedArgumentPresence.Mode
                | FoxRunNamedArgumentPresence.Source
                | FoxRunNamedArgumentPresence.Targets
                | FoxRunNamedArgumentPresence.QoS
                | FoxRunNamedArgumentPresence.Reliability
                | FoxRunNamedArgumentPresence.Durability
                | FoxRunNamedArgumentPresence.History
                | FoxRunNamedArgumentPresence.Depth;
            var first = BuildGroupedQosMember("_first", presence);
            var second = BuildGroupedQosMember(
                "_second",
                mismatch == "presence"
                    ? presence & ~FoxRunNamedArgumentPresence.Depth
                    : presence,
                mode: mismatch == "flow"
                    ? (int)FoxRunFlow.Publish
                    : (int)FoxRunFlow.PublishAndSubscribe,
                source: mismatch == "source"
                    ? FoxRunGenerationDescriptorConstants.FoxgloveWebSocketSource
                    : FoxRunGenerationDescriptorConstants.Ros2NativeSource,
                targets: mismatch == "targets"
                    ? FoxRunGenerationDescriptorConstants.Ros2BridgeTarget
                    : FoxRunGenerationDescriptorConstants.Ros2NativeTarget,
                qosProfile: mismatch == "profile"
                    ? FoxRunGenerationDescriptorConstants.SensorDataQosProfile
                    : FoxRunGenerationDescriptorConstants.DefaultQosProfile,
                qosReliability: mismatch == "reliability"
                    ? FoxRunGenerationDescriptorConstants.BestEffortQosReliability
                    : FoxRunGenerationDescriptorConstants.ReliableQosReliability,
                qosDurability: mismatch == "durability"
                    ? FoxRunGenerationDescriptorConstants.TransientLocalQosDurability
                    : FoxRunGenerationDescriptorConstants.VolatileQosDurability,
                qosHistory: mismatch == "history"
                    ? FoxRunGenerationDescriptorConstants.KeepAllQosHistory
                    : FoxRunGenerationDescriptorConstants.KeepLastQosHistory,
                qosDepth: mismatch == "depth" ? 19 : 17);

            var diagnostics = FoxRunGenerationModelValidator.Validate(
                FoxRunGenerationModel.FromMembers(new[] { first, second }));

            Assert.Single(diagnostics, diagnostic => diagnostic.Id == "FOXRUN615");
        }

        [Fact]
        public void SharedTopicAcceptsIdenticalDirectionalQosContract()
        {
            const FoxRunNamedArgumentPresence presence =
                FoxRunNamedArgumentPresence.Mode
                | FoxRunNamedArgumentPresence.Source
                | FoxRunNamedArgumentPresence.Targets
                | FoxRunNamedArgumentPresence.QoS
                | FoxRunNamedArgumentPresence.Reliability
                | FoxRunNamedArgumentPresence.Durability
                | FoxRunNamedArgumentPresence.History
                | FoxRunNamedArgumentPresence.Depth;
            var model = FoxRunGenerationModel.FromMembers(new[]
            {
                BuildGroupedQosMember("_first", presence, mode: (int)FoxRunFlow.Publish),
                BuildGroupedQosMember(
                    "_second",
                    presence & ~FoxRunNamedArgumentPresence.Mode,
                    mode: (int)FoxRunFlow.Publish)
            });

            Assert.DoesNotContain(
                FoxRunGenerationModelValidator.Validate(model),
                diagnostic => diagnostic.Id == "FOXRUN615");
        }

        [Fact]
        public void SharedTopicAllowsIndependentSubscribeOnlyTransportContracts()
        {
            const FoxRunNamedArgumentPresence presence =
                FoxRunNamedArgumentPresence.Mode
                | FoxRunNamedArgumentPresence.Source
                | FoxRunNamedArgumentPresence.QoS;
            var model = FoxRunGenerationModel.FromMembers(new[]
            {
                BuildGroupedQosMember(
                    "_first",
                    presence,
                    mode: (int)FoxRunFlow.Subscribe,
                    source: FoxRunGenerationDescriptorConstants.Ros2NativeSource,
                    qosProfile: FoxRunGenerationDescriptorConstants.DefaultQosProfile),
                BuildGroupedQosMember(
                    "_second",
                    presence,
                    mode: (int)FoxRunFlow.Subscribe,
                    source: FoxRunGenerationDescriptorConstants.Ros2NativeSource,
                    qosProfile: FoxRunGenerationDescriptorConstants.SensorDataQosProfile)
            });

            Assert.DoesNotContain(
                FoxRunGenerationModelValidator.Validate(model),
                diagnostic => diagnostic.Id == "FOXRUN615");
        }

        [Fact]
        public void SharedTopicAllowsIndependentPublishAndSubscribeMembers()
        {
            const FoxRunNamedArgumentPresence publishPresence =
                FoxRunNamedArgumentPresence.Mode
                | FoxRunNamedArgumentPresence.Targets
                | FoxRunNamedArgumentPresence.QoS;
            const FoxRunNamedArgumentPresence subscribePresence =
                FoxRunNamedArgumentPresence.Mode
                | FoxRunNamedArgumentPresence.Source
                | FoxRunNamedArgumentPresence.QoS;
            var model = FoxRunGenerationModel.FromMembers(new[]
            {
                BuildGroupedQosMember(
                    "_publish",
                    publishPresence,
                    mode: (int)FoxRunFlow.Publish,
                    source: FoxRunGenerationDescriptorConstants.InheritSource,
                    targets: FoxRunGenerationDescriptorConstants.Ros2NativeTarget),
                BuildGroupedQosMember(
                    "_subscribe",
                    subscribePresence,
                    mode: (int)FoxRunFlow.Subscribe,
                    source: FoxRunGenerationDescriptorConstants.Ros2NativeSource,
                    targets: FoxRunGenerationDescriptorConstants.InheritTargets)
            });

            Assert.DoesNotContain(
                FoxRunGenerationModelValidator.Validate(model),
                diagnostic => diagnostic.Id == "FOXRUN615");
        }

        [Fact]
        public void SharedTopicMixedDirectionalQosAnchorsFirstPublishingMember()
        {
            const FoxRunNamedArgumentPresence publishPresence =
                FoxRunNamedArgumentPresence.Mode
                | FoxRunNamedArgumentPresence.Targets
                | FoxRunNamedArgumentPresence.QoS;
            const FoxRunNamedArgumentPresence subscribePresence =
                FoxRunNamedArgumentPresence.Mode
                | FoxRunNamedArgumentPresence.Source
                | FoxRunNamedArgumentPresence.QoS;
            var model = FoxRunGenerationModel.FromMembers(new[]
            {
                BuildGroupedQosMember(
                    "_aSubscribe",
                    subscribePresence,
                    mode: (int)FoxRunFlow.Subscribe,
                    source: FoxRunGenerationDescriptorConstants.Ros2NativeSource,
                    targets: FoxRunGenerationDescriptorConstants.InheritTargets),
                BuildGroupedQosMember(
                    "_bPublish",
                    publishPresence,
                    mode: (int)FoxRunFlow.Publish,
                    source: FoxRunGenerationDescriptorConstants.InheritSource,
                    targets: FoxRunGenerationDescriptorConstants.Ros2NativeTarget),
                BuildGroupedQosMember(
                    "_cPublish",
                    publishPresence,
                    mode: (int)FoxRunFlow.Publish,
                    source: FoxRunGenerationDescriptorConstants.InheritSource,
                    targets: FoxRunGenerationDescriptorConstants.Ros2BridgeTarget)
            });

            var diagnostic = Assert.Single(
                FoxRunGenerationModelValidator.Validate(model),
                candidate => candidate.Id == "FOXRUN615");

            Assert.Equal("_bPublish", diagnostic.MemberName);
            Assert.Contains("every publishing member", diagnostic.Message, StringComparison.Ordinal);
        }

        private static FoxRunGenerationMember BuildGroupedQosMember(
            string memberName,
            FoxRunNamedArgumentPresence presence,
            int mode = (int)FoxRunFlow.PublishAndSubscribe,
            string source = FoxRunGenerationDescriptorConstants.Ros2NativeSource,
            string targets = FoxRunGenerationDescriptorConstants.Ros2NativeTarget,
            string qosProfile = FoxRunGenerationDescriptorConstants.DefaultQosProfile,
            string qosReliability = FoxRunGenerationDescriptorConstants.ReliableQosReliability,
            string qosDurability = FoxRunGenerationDescriptorConstants.VolatileQosDurability,
            string qosHistory = FoxRunGenerationDescriptorConstants.KeepLastQosHistory,
            int qosDepth = 17)
            => new(
                "Demo",
                "GroupedQos",
                memberName,
                "field",
                "System.Single",
                true,
                false,
                "",
                "/phase184/grouped-qos",
                10f,
                "Demo.GroupedQos",
                (int)FoxRunPolicy.FixedRate,
                0f,
                "UnitTest",
                0,
                "",
                mode: mode,
                source: source,
                qosProfile: qosProfile,
                namedArgumentPresence: presence,
                targets: targets,
                qosReliability: qosReliability,
                qosDurability: qosDurability,
                qosHistory: qosHistory,
                qosDepth: qosDepth);

        private static FoxRunGenerationModel BuildModel(
            int provider,
            int qosProfile,
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
                    source: provider, qosProfile: qosProfile,
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
