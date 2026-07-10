// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/FoxRun
// Purpose: Locks deterministic FoxRun Protobuf contract metadata before live transport support.

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Google.Protobuf.Reflection;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.Schemas;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.FoxRun
{
    public sealed class FoxRunProtobufContractTests
    {
        [Fact]
        public void StableIdentityProducesStableLegalFieldNumber()
        {
            var first = FoxRunProtobufFieldNumber.Resolve("Demo.WireState|/phase175/wire_state|_count", 0);
            var second = FoxRunProtobufFieldNumber.Resolve("Demo.WireState|/phase175/wire_state|_count", 0);

            Assert.Equal(first, second);
            Assert.InRange(first, 1, FoxRunProtobufFieldNumber.MaximumFieldNumber);
            Assert.False(FoxRunProtobufFieldNumber.IsReserved(first));
        }

        [Theory]
        [InlineData(19000)]
        [InlineData(536870912)]
        public void InvalidExplicitFieldNumberIsRejected(int fieldNumber)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                FoxRunProtobufFieldNumber.Resolve("Demo.WireState|/phase175/wire_state|_count", fieldNumber));
        }

        [Fact]
        public void ContractBuilderEmitsDeterministicDescriptorWithVectorAndRepeatedFields()
        {
            var contract = CreateContract();

            var first = FoxRunProtobufContractBuilder.Build(contract);
            var second = FoxRunProtobufContractBuilder.Build(contract);
            var descriptor = FileDescriptorSet.Parser.ParseFrom(first.FileDescriptorSet);
            var message = Assert.Single(Assert.Single(descriptor.File).MessageType, candidate => candidate.Name == "WireState");

            Assert.Equal(first.FileDescriptorSet, second.FileDescriptorSet);
            Assert.Equal("WireState", message.Name);
            Assert.Contains(message.Field, field => field.Name == "count" && field.Number == 17);
            Assert.Contains(message.Field, field => field.Name == "position" && field.Type == FieldDescriptorProto.Types.Type.Message);
            Assert.Contains(message.Field, field => field.Name == "samples" && field.Label == FieldDescriptorProto.Types.Label.Repeated);
        }

        [Fact]
        public void ContractBuilderEmitsNestedDtoGraphWithoutJsonFallback()
        {
            var pose = FoxRunProtobufTypeShape.Object(
                "Demo.Pose",
                new[]
                {
                    new FoxRunProtobufTypeField("position", "position", FoxRunProtobufTypeShape.Canonical("unity.vector3.float32"))
                });
            var telemetry = FoxRunProtobufTypeShape.Object(
                "Demo.VehicleTelemetry",
                new[]
                {
                    new FoxRunProtobufTypeField("label", "label", FoxRunProtobufTypeShape.Canonical("string")),
                    new FoxRunProtobufTypeField("pose", "pose", pose),
                    new FoxRunProtobufTypeField("samples", "samples", FoxRunProtobufTypeShape.Canonical("float32"), repeated: true)
                });
            var contract = new FoxRunProtobufContractInput(
                "Demo.WireState",
                "/phase175/wire_state",
                "Demo.WireState",
                new[] { new FoxRunProtobufFieldInput("telemetry", "_telemetry", "Demo.VehicleTelemetry", false, typeShape: telemetry) });

            var descriptor = FileDescriptorSet.Parser.ParseFrom(FoxRunProtobufContractBuilder.Build(contract).FileDescriptorSet);
            var messages = Assert.Single(descriptor.File).MessageType;
            var root = Assert.Single(messages, message => message.Name == "WireState");
            var dto = Assert.Single(messages, message => message.Name == "Demo_VehicleTelemetry");
            var poseMessage = Assert.Single(messages, message => message.Name == "Demo_Pose");

            Assert.Contains(root.Field, field => field.Name == "telemetry" && field.Type == FieldDescriptorProto.Types.Type.Message);
            Assert.Contains(dto.Field, field => field.Name == "pose" && field.TypeName.EndsWith(".Demo_Pose", StringComparison.Ordinal));
            Assert.Contains(dto.Field, field => field.Name == "samples" && field.Label == FieldDescriptorProto.Types.Label.Repeated);
            Assert.Contains(poseMessage.Field, field => field.Name == "position" && field.Type == FieldDescriptorProto.Types.Type.Message);
        }

        [Fact]
        public void ModelValidatorRejectsMixedWirePoliciesAndDuplicateExplicitTags()
        {
            var model = FoxRunGenerationModel.FromMembers(new[]
            {
                new FoxRunGenerationMember(
                    "Demo", "WireState", "_count", "field", "System.Int32", true, false, "",
                    "/phase175/wire_state", 10f, "", 0, 0f, 0f, "UnitTest", 0, "",
                    encoding: "json", protobufFieldNumber: 17),
                new FoxRunGenerationMember(
                    "Demo", "WireState", "_otherCount", "field", "System.Int32", true, false, "",
                    "/phase175/wire_state", 10f, "", 0, 0f, 0f, "UnitTest", 1, "",
                    encoding: "protobuf", protobufFieldNumber: 17)
            });

            var diagnostics = FoxRunGenerationModelValidator.Validate(model);

            Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "FOXRUN032");
            Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "FOXRUN033");
        }

        [Fact]
        public void SchemaInfoWriterEmbedsProtobufDescriptorSetForProtobufContracts()
        {
            var manifest = FoxRunManifestBuilder.Build(new[]
            {
                new FoxRunManifestMember(
                    "Demo", "WireState", "_count", "field", "System.Int32", true, false, "",
                    "/phase175/wire_state", 10f, "Demo.WireState", 0, 0f, 0f,
                    encoding: 1, protobufFieldNumber: 17)
            });

            var source = FoxRunSchemaInfoWriter.GenerateSource(manifest);

            Assert.Contains("global::System.Convert.FromBase64String", source, StringComparison.Ordinal);
            Assert.Contains("ProtobufDescriptorSet", typeof(Unity.FoxgloveSDK.Components.FoxRunSchemaContractInfo).GetProperty("ProtobufDescriptorSet").Name, StringComparison.Ordinal);
        }

        [Fact]
        public void NestedDtoShapeContributesToProtobufContractHash()
        {
            var stringShape = FoxRunProtobufTypeShape.Object(
                "Demo.Telemetry",
                new[] { new FoxRunProtobufTypeField("value", "Value", FoxRunProtobufTypeShape.Canonical("string")) });
            var floatShape = FoxRunProtobufTypeShape.Object(
                "Demo.Telemetry",
                new[] { new FoxRunProtobufTypeField("value", "Value", FoxRunProtobufTypeShape.Canonical("float32")) });

            var first = BuildProtobufManifest(stringShape);
            var second = BuildProtobufManifest(floatShape);

            Assert.NotEqual(
                first.Sections.FoxRun.Types[0].Contracts[0].ContractHash,
                second.Sections.FoxRun.Types[0].Contracts[0].ContractHash);
        }

        [Fact]
        public void SchemaInfoWriterEmbedsNestedDtoDescriptorFromManifestShape()
        {
            var shape = FoxRunProtobufTypeShape.Object(
                "Demo.Telemetry",
                new[] { new FoxRunProtobufTypeField("value", "Value", FoxRunProtobufTypeShape.Canonical("string")) });
            var source = FoxRunSchemaInfoWriter.GenerateSource(BuildProtobufManifest(shape));
            var descriptorText = Regex.Match(source, "FromBase64String\\(\\\"(?<descriptor>[A-Za-z0-9+/=]+)\\\"\\)");

            Assert.True(descriptorText.Success);
            var descriptor = FileDescriptorSet.Parser.ParseFrom(Convert.FromBase64String(descriptorText.Groups["descriptor"].Value));
            Assert.Contains(Assert.Single(descriptor.File).MessageType, message => message.Name == "Demo_Telemetry");
        }

        [Fact]
        public void SchemaInfoRegistryRegistersGeneratedProtobufDescriptorBySchemaName()
        {
            var descriptor = FoxRunProtobufContractBuilder.Build(CreateContract()).FileDescriptorSet;
            var contract = new FoxRunSchemaContractInfo(
                "Demo.WireState",
                "/phase175/wire_state",
                "Demo.WireState",
                "protobuf",
                "contract",
                "binding",
                "policy",
                "FixedRate",
                10f,
                0f,
                0f,
                new[] { new FoxRunSchemaFieldInfo("count", "_count", "field", "int32", false, false, false, 17) },
                protobufDescriptorSet: descriptor);
            var manifest = new FoxRunSchemaManifestInfo(
                1,
                "Unity2Foxglove",
                "FoxRun",
                1,
                "global",
                "foxrun",
                new[] { new FoxRunSchemaTypeInfo("Demo.WireState", new[] { contract }) });
            var registry = new DefaultSchemaRegistry();

            FoxRunSchemaInfoRegistry.ClearForTests();
            try
            {
                FoxRunSchemaInfoRegistry.RegisterGenerated(manifest);
                FoxRunSchemaInfoRegistry.RegisterGeneratedSchemas(registry);

                Assert.True(registry.TryGetSchema("Demo.WireState", "protobuf", out var entry));
                Assert.Equal(descriptor, entry.RawContent);
            }
            finally
            {
                FoxRunSchemaInfoRegistry.ClearForTests();
            }
        }

        [Fact]
        public void ReflectionLowererCarriesNestedDtoShapeIntoProtobufContract()
        {
            var reflected = new FoxrunCodeGenerator.MemberData(
                "_telemetry",
                typeof(ReflectionTelemetry),
                "field",
                "Demo",
                "WireState",
                "/phase175/wire_state",
                10f,
                "Demo.WireState",
                encoding: (int)FoxRunWireEncoding.Protobuf);
            var member = FoxRunReflectionGenerationModelLowerer.Lower(new[] { reflected.ToReflectionMember() })
                .Types[0]
                .Members[0];

            Assert.NotNull(member.ProtobufTypeShape);
            var descriptor = FileDescriptorSet.Parser.ParseFrom(FoxRunProtobufContractBuilder.Build(
                new FoxRunProtobufContractInput(
                    member.DeclaringType,
                    member.Topic,
                    member.SchemaName,
                    new[]
                    {
                        new FoxRunProtobufFieldInput(
                            member.JsonFieldName,
                            member.MemberName,
                            member.CanonicalType,
                            member.IsArray,
                            member.ProtobufFieldNumber,
                            member.ProtobufTypeShape)
                    })).FileDescriptorSet);

            Assert.Contains(Assert.Single(descriptor.File).MessageType, message => message.Name.EndsWith("ReflectionTelemetry", StringComparison.Ordinal));
        }

        private static FoxRunProtobufContractInput CreateContract()
        {
            var fields = new List<FoxRunProtobufFieldInput>
            {
                new FoxRunProtobufFieldInput("count", "_count", "int32", false, protobufFieldNumber: 17),
                new FoxRunProtobufFieldInput("position", "_position", "unity.vector3.float32", false),
                new FoxRunProtobufFieldInput("samples", "_samples", "float32", true)
            };
            return new FoxRunProtobufContractInput(
                "Demo.WireState",
                "/phase175/wire_state",
                "Demo.WireState",
                fields);
        }

        private static FoxRunCanonicalManifest BuildProtobufManifest(FoxRunProtobufTypeShape shape)
        {
            return FoxRunManifestBuilder.Build(new[]
            {
                new FoxRunManifestMember(
                    "Demo", "WireState", "_telemetry", "field", "Demo.Telemetry", false, false, "",
                    "/phase175/wire_state", 10f, "Demo.WireState", 0, 0f, 0f,
                    encoding: (int)FoxRunWireEncoding.Protobuf,
                    protobufTypeShape: shape)
            });
        }

        private sealed class ReflectionTelemetry
        {
            public string Label { get; set; }
            public ReflectionPose Pose { get; set; }
            public float[] Samples { get; set; }
        }

        private sealed class ReflectionPose
        {
            public UnityEngine.Vector3 Position { get; set; }
        }
    }
}
