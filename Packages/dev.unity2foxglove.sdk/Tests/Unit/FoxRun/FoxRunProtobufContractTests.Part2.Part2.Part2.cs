// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/FoxRun
// Purpose: Locks deterministic FoxRun Protobuf contract metadata before live transport support.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Google.Protobuf.Reflection;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.UnitTests.Harness;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.FoxRun
{
    public sealed partial class FoxRunProtobufContractTests
    {
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
                encoding: (int)FoxRunEncoding.Protobuf);
            var member = FoxRunReflectionGenerationModelLowerer.Lower(new[] { reflected.ToReflectionMember() })
                .Types[0]
                .Members[0];

            Assert.NotNull(member.TypeShape);
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
                            0,
                            member.TypeShape,
                            member.ProtobufMetadata)
                    })).FileDescriptorSet);

            Assert.Contains(Assert.Single(descriptor.File).MessageType, message => message.Name.EndsWith("ReflectionTelemetry", StringComparison.Ordinal));
        }

        [Fact]
        [Trait("Phase", "185-A")]
        public void ExplicitMessagePackManifestNeverCarriesProtobufWireMetadata()
        {
            var shape = FoxRunReflectionTypeShapeBuilder.Build(typeof(ReflectionTelemetry));
            var manifest = FoxRunManifestBuilder.Build(new[]
            {
                new FoxRunManifestMember(
                    "Demo", "WireState", "_telemetry", "field", "Demo.Telemetry", false, false, "",
                    "/phase185/messagepack", 10f, "Demo.Telemetry", (int)FoxRunPolicy.FixedRate, 0f,
                    encoding: (int)FoxRunEncoding.MessagePack,
                    typeShape: shape)
            });

            var contract = Assert.Single(Assert.Single(manifest.Sections.FoxRun.Types).Contracts);
            Assert.Equal("msgpack", contract.Encoding);
            Assert.Equal(string.Empty, contract.SchemaName);
            Assert.All(contract.Fields, field => Assert.Null(field.ProtobufMetadata));
        }

        [Fact]
        [Trait("Phase", "185-A")]
        public void ProtobufDescriptorSynthesizesZeroWithoutPollutingEncodingNeutralEnumShape()
        {
            var shape = FoxRunTypeShape.Enum(
                "Demo.NoZeroEnum",
                new[]
                {
                    new FoxRunEnumValue("First", 1),
                    new FoxRunEnumValue("Second", 2)
                });
            var contract = new FoxRunProtobufContractInput(
                "Demo.EnumSource",
                "/phase185/enum",
                "Demo.EnumEnvelope",
                new[]
                {
                    new FoxRunProtobufFieldInput(
                        "value",
                        "_value",
                        "Demo.NoZeroEnum",
                        false,
                        typeShape: shape)
                });

            var descriptor = FileDescriptorSet.Parser.ParseFrom(
                FoxRunProtobufContractBuilder.Build(contract).FileDescriptorSet);
            var protobufEnum = Assert.Single(Assert.Single(descriptor.File).EnumType);

            Assert.Equal(new[] { 1, 2 }, shape.EnumValues.Select(value => value.Number).ToArray());
            Assert.Equal(0, protobufEnum.Value[0].Number);
            Assert.Equal("UNSPECIFIED", protobufEnum.Value[0].Name);
            Assert.Equal(new[] { 1, 2 }, protobufEnum.Value.Skip(1).Select(value => value.Number).ToArray());
        }

        [Fact]
        [Trait("Phase", "185-A")]
        public void UnityObjectShapesKeepCanonicalProtobufDescriptors()
        {
            var cases = new[]
            {
                (Type: typeof(UnityEngine.Vector2), Message: "Unity_Vector2", Components: new[] { "x", "y" }),
                (Type: typeof(UnityEngine.Vector3), Message: "Unity_Vector3", Components: new[] { "x", "y", "z" }),
                (Type: typeof(UnityEngine.Quaternion), Message: "Unity_Quaternion", Components: new[] { "x", "y", "z", "w" }),
                (Type: typeof(UnityEngine.Color), Message: "Unity_Color", Components: new[] { "r", "g", "b", "a" })
            };

            foreach (var testCase in cases)
            {
                var shape = FoxRunReflectionTypeShapeBuilder.Build(testCase.Type);
                var contract = new FoxRunProtobufContractInput(
                    "Demo.UnityValueSource",
                    "/phase185/unity-value",
                    "Demo.UnityValueEnvelope",
                    new[]
                    {
                        new FoxRunProtobufFieldInput(
                            "value",
                            "_value",
                            shape.TypeName,
                            false,
                            protobufFieldNumber: 17,
                            typeShape: shape)
                    });

                var descriptor = FileDescriptorSet.Parser.ParseFrom(
                    FoxRunProtobufContractBuilder.Build(contract).FileDescriptorSet);
                var nested = Assert.Single(
                    Assert.Single(descriptor.File).MessageType,
                    message => message.Name == testCase.Message);

                Assert.Equal(
                    testCase.Components,
                    nested.Field.OrderBy(field => field.Number).Select(field => field.Name).ToArray());
                Assert.Equal(
                    Enumerable.Range(1, testCase.Components.Length),
                    nested.Field.OrderBy(field => field.Number).Select(field => field.Number));
            }
        }

        [Fact]
        [Trait("Phase", "185-A")]
        public void ReusedProtobufObjectTypeNameRejectsShapeDrift()
        {
            var first = FoxRunTypeShape.Object(
                "Demo.SharedPayload",
                new[]
                {
                    new FoxRunTypeField("value", "Value", FoxRunTypeShape.Canonical("int32"))
                });
            var conflicting = FoxRunTypeShape.Object(
                "Demo.SharedPayload",
                new[]
                {
                    new FoxRunTypeField("label", "Label", FoxRunTypeShape.Canonical("string"))
                });
            var contract = new FoxRunProtobufContractInput(
                "Demo.ShapeConflictSource",
                "/phase185/shape-conflict",
                "Demo.ShapeConflictEnvelope",
                new[]
                {
                    new FoxRunProtobufFieldInput("first", "_first", first.TypeName, false, 17, first),
                    new FoxRunProtobufFieldInput("second", "_second", conflicting.TypeName, false, 19, conflicting)
                });

            var error = Assert.Throws<InvalidOperationException>(
                () => FoxRunProtobufContractBuilder.Build(contract));

            Assert.Contains("Demo.SharedPayload", error.Message, StringComparison.Ordinal);
            Assert.Contains("inconsistent", error.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        [Trait("Phase", "185-A")]
        public void ReusedProtobufObjectTypeNameRejectsNestedTagDrift()
        {
            var shape = FoxRunTypeShape.Object(
                "Demo.SharedPayload",
                new[]
                {
                    new FoxRunTypeField("value", "Value", FoxRunTypeShape.Canonical("int32"))
                });
            var contract = new FoxRunProtobufContractInput(
                "Demo.MetadataConflictSource",
                "/phase185/metadata-conflict",
                "Demo.MetadataConflictEnvelope",
                new[]
                {
                    new FoxRunProtobufFieldInput(
                        "first",
                        "_first",
                        shape.TypeName,
                        false,
                        17,
                        shape,
                        new FoxRunProtobufMetadata(
                            17,
                            new FoxRunProtobufTypeMetadata(
                                shape.TypeName,
                                new[] { new FoxRunProtobufFieldMetadata("Value", "value", 7) }))),
                    new FoxRunProtobufFieldInput(
                        "second",
                        "_second",
                        shape.TypeName,
                        false,
                        19,
                        shape,
                        new FoxRunProtobufMetadata(
                            19,
                            new FoxRunProtobufTypeMetadata(
                                shape.TypeName,
                                new[] { new FoxRunProtobufFieldMetadata("Value", "value", 11) })))
                });

            var error = Assert.Throws<InvalidOperationException>(
                () => FoxRunProtobufContractBuilder.Build(contract));

            Assert.Contains("Demo.SharedPayload", error.Message, StringComparison.Ordinal);
            Assert.Contains("inconsistent", error.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        [Trait("Phase", "185-A")]
        public void ProtobufTagChangesDoNotAffectMessagePackHashButDoAffectProtobufDescriptor()
        {
            var shape = FoxRunTypeShape.Object(
                "Demo.TaggedPayload",
                new[]
                {
                    new FoxRunTypeField(
                        "value",
                        "Value",
                        FoxRunTypeShape.Canonical("int32"))
                });
            var first = BuildInheritedTaggedManifest(shape, nestedFieldNumber: 7);
            var second = BuildInheritedTaggedManifest(shape, nestedFieldNumber: 11);
            var firstContracts = Assert.Single(first.Sections.FoxRun.Types).Contracts;
            var secondContracts = Assert.Single(second.Sections.FoxRun.Types).Contracts;
            var firstMessagePack = Assert.Single(firstContracts, contract => contract.Encoding == "msgpack");
            var secondMessagePack = Assert.Single(secondContracts, contract => contract.Encoding == "msgpack");
            var firstProtobuf = Assert.Single(firstContracts, contract => contract.Encoding == "protobuf");
            var secondProtobuf = Assert.Single(secondContracts, contract => contract.Encoding == "protobuf");

            Assert.Equal(firstMessagePack.ContractHash, secondMessagePack.ContractHash);
            Assert.All(firstMessagePack.Fields, field => Assert.Null(field.ProtobufMetadata));
            Assert.NotEqual(
                BuildDescriptor(firstProtobuf),
                BuildDescriptor(secondProtobuf));
        }

        [Fact]
        [Trait("Phase", "185-A")]
        public void MessagePackLogicalSchemaIdentityIsIndependentOfMemberDiscoveryOrder()
        {
            var alpha = MessagePackLogicalIdentityMember(
                "_alpha",
                "alpha",
                "Demo.Alpha");
            var beta = MessagePackLogicalIdentityMember(
                "_beta",
                "beta",
                "Demo.Beta");

            var forward = FoxRunManifestBuilder.Build(new[] { alpha, beta });
            var reverse = FoxRunManifestBuilder.Build(new[] { beta, alpha });
            var forwardContract = Assert.Single(
                Assert.Single(forward.Sections.FoxRun.Types).Contracts);
            var reverseContract = Assert.Single(
                Assert.Single(reverse.Sections.FoxRun.Types).Contracts);

            Assert.Equal("Demo.LogicalOwner", forwardContract.LogicalSchemaName);
            Assert.Equal(forwardContract.LogicalSchemaName, reverseContract.LogicalSchemaName);
            Assert.Equal(forwardContract.ContractHash, reverseContract.ContractHash);

            var oneExplicit = FoxRunManifestBuilder.Build(new[]
            {
                MessagePackLogicalIdentityMember("_alpha", "alpha", "Demo.Alpha"),
                MessagePackLogicalIdentityMember("_beta", "beta", string.Empty)
            });
            Assert.Equal(
                "Demo.Alpha",
                Assert.Single(Assert.Single(oneExplicit.Sections.FoxRun.Types).Contracts)
                    .LogicalSchemaName);
        }

        [Fact]
        [Trait("Phase", "185-A")]
        public void SchemaInfoWriterCarriesObjectConstructionCapabilityIntoGeneratedRuntimeShape()
        {
            var shape = FoxRunTypeShape.Object(
                "Demo.NoDefaultConstructor",
                Array.Empty<FoxRunTypeField>(),
                nullable: true,
                canConstruct: false);
            var manifest = FoxRunManifestBuilder.Build(new[]
            {
                new FoxRunManifestMember(
                    "Demo",
                    "ConstructionSource",
                    "_value",
                    "field",
                    "Demo.NoDefaultConstructor",
                    false,
                    false,
                    string.Empty,
                    "/phase185/construction",
                    10f,
                    "Demo.NoDefaultConstructor",
                    (int)FoxRunPolicy.FixedRate,
                    0f,
                    flow: (int)FoxRunFlow.Subscribe,
                    encoding: (int)FoxRunEncoding.MessagePack,
                    typeShape: shape)
            }, manifestVersion: FoxrunManifestWriter.CurrentManifestVersion);

            var source = FoxRunSchemaInfoWriter.GenerateSource(manifest);
            var creation = Assert.Single(
                CSharpSyntaxTree.ParseText(source)
                    .GetRoot()
                    .DescendantNodes()
                    .OfType<ObjectCreationExpressionSyntax>(),
                candidate =>
                    candidate.Type.ToString().EndsWith(
                        "FoxRunTypeShapeInfo",
                        StringComparison.Ordinal)
                    && candidate.ArgumentList.Arguments.Count > 1
                    && string.Equals(
                        candidate.ArgumentList.Arguments[1].Expression.ToString(),
                        "\"Demo.NoDefaultConstructor\"",
                        StringComparison.Ordinal));

            Assert.Equal(10, creation.ArgumentList.Arguments.Count);
            Assert.Equal("true", creation.ArgumentList.Arguments[3].Expression.ToString());
            Assert.Equal("false", creation.ArgumentList.Arguments[8].Expression.ToString());
            Assert.Equal("false", creation.ArgumentList.Arguments[9].Expression.ToString());
        }

        private static string ExtractAggregateSchemaName(string source)
        {
            var match = Regex.Match(
                source ?? string.Empty,
                @"\[FoxRunMessage\([^\]]*SchemaName\s*=\s*""(?<schema>[^""]+)""",
                RegexOptions.Singleline);
            Assert.True(match.Success, "Expected a FoxRunMessage SchemaName declaration.");
            return match.Groups["schema"].Value;
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

        private static FoxRunManifestMember MessagePackLogicalIdentityMember(
            string memberName,
            string jsonName,
            string schemaName)
            => new FoxRunManifestMember(
                "Demo",
                "LogicalOwner",
                memberName,
                "field",
                "System.Int32",
                true,
                false,
                string.Empty,
                "/phase185/logical-identity",
                10f,
                schemaName,
                (int)FoxRunPolicy.FixedRate,
                0f,
                jsonFieldName: jsonName,
                encoding: (int)FoxRunEncoding.MessagePack,
                typeShape: FoxRunTypeShape.Canonical("int32"));

        private static FoxRunCanonicalManifest BuildProtobufManifest(FoxRunTypeShape shape)
        {
            return FoxRunManifestBuilder.Build(new[]
            {
                new FoxRunManifestMember(
                    "Demo", "WireState", "_telemetry", "field", "Demo.Telemetry", false, false, "",
                    "/phase175/wire_state", 10f, "Demo.WireState", (int)FoxRunPolicy.FixedRate, 0f,
                    encoding: (int)FoxRunEncoding.Protobuf,
                    typeShape: shape)
            });
        }

        private static FoxRunCanonicalManifest BuildInheritedTaggedManifest(
            FoxRunTypeShape shape,
            int nestedFieldNumber)
        {
            return FoxRunManifestBuilder.Build(new[]
            {
                new FoxRunManifestMember(
                    "Demo",
                    "TaggedSource",
                    "_payload",
                    "field",
                    "Demo.TaggedPayload",
                    false,
                    false,
                    "",
                    "/phase185/tag-isolation",
                    10f,
                    "Demo.TaggedEnvelope",
                    (int)FoxRunPolicy.FixedRate,
                    0f,
                    encoding: 0,
                    typeShape: shape,
                    protobufMetadata: new FoxRunProtobufMetadata(
                        17,
                        new FoxRunProtobufTypeMetadata(
                            "Demo.TaggedPayload",
                            new[]
                            {
                                new FoxRunProtobufFieldMetadata(
                                    "Value",
                                    "value",
                                    nestedFieldNumber)
                            })))
            });
        }

        private static string BuildDescriptor(FoxRunManifestContract contract)
        {
            var input = new FoxRunProtobufContractInput(
                contract.DeclaringType,
                contract.Topic,
                contract.SchemaName,
                contract.Fields.Select(field => new FoxRunProtobufFieldInput(
                    field.JsonName,
                    field.MemberName,
                    field.Type,
                    field.Array,
                    typeShape: field.TypeShape,
                    protobufMetadata: field.ProtobufMetadata)).ToArray());
            return Convert.ToBase64String(
                FoxRunProtobufContractBuilder.Build(input).FileDescriptorSet);
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
