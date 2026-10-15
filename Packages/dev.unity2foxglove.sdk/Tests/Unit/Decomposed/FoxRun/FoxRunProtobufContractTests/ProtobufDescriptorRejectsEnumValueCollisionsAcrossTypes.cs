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
        [Trait("Phase", "187-R2-E01-001")]
        public void ProtobufDescriptorRejectsEnumValueCollisionsAcrossTypes()
        {
            var first = FoxRunTypeShape.Enum(
                "Demo.FirstState",
                new[]
                {
                    new FoxRunEnumValue("first_none", 0),
                    new FoxRunEnumValue("ready", 1)
                });
            var second = FoxRunTypeShape.Enum(
                "Demo.SecondState",
                new[]
                {
                    new FoxRunEnumValue("second_none", 0),
                    new FoxRunEnumValue("ready", 1)
                });
            var contract = new FoxRunProtobufContractInput(
                "Demo.EnumSource",
                "/phase187/e01/enum-scope",
                "Demo.EnumEnvelope",
                new[]
                {
                    new FoxRunProtobufFieldInput(
                        "first",
                        "First",
                        first.TypeName,
                        false,
                        protobufFieldNumber: 1,
                        typeShape: first),
                    new FoxRunProtobufFieldInput(
                        "second",
                        "Second",
                        second.TypeName,
                        false,
                        protobufFieldNumber: 2,
                        typeShape: second)
                });

            var error = Assert.Throws<InvalidOperationException>(
                () => FoxRunProtobufContractBuilder.Build(contract));
            Assert.Contains("enum", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ready", error.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        [Trait("Phase", "187-R2-E01-001")]
        public void ProtobufDescriptorUsesUniqueSyntheticEnumZeroNamesAcrossTypes()
        {
            var first = FoxRunTypeShape.Enum(
                "Demo.FirstState",
                new[] { new FoxRunEnumValue("ready", 1) });
            var second = FoxRunTypeShape.Enum(
                "Demo.SecondState",
                new[] { new FoxRunEnumValue("running", 1) });
            var contract = new FoxRunProtobufContractInput(
                "Demo.EnumSource",
                "/phase187/e01/enum-synthetic-scope",
                "Demo.EnumEnvelope",
                new[]
                {
                    new FoxRunProtobufFieldInput(
                        "first",
                        "First",
                        first.TypeName,
                        false,
                        protobufFieldNumber: 1,
                        typeShape: first),
                    new FoxRunProtobufFieldInput(
                        "second",
                        "Second",
                        second.TypeName,
                        false,
                        protobufFieldNumber: 2,
                        typeShape: second)
                });

            var descriptor = FileDescriptorSet.Parser.ParseFrom(
                FoxRunProtobufContractBuilder.Build(contract).FileDescriptorSet);
            var values = Assert.Single(descriptor.File).EnumType
                .Select(enumDescriptor => enumDescriptor.Value[0].Name)
                .ToArray();

            Assert.Equal(new[] { "UNSPECIFIED", "UNSPECIFIED_2" }, values);
        }

        [Fact]
        [Trait("Phase", "187-R2-E01-001")]
        public void ProtobufDescriptorSyntheticEnumZeroNamesAvoidFutureDeclaredValues()
        {
            var first = FoxRunTypeShape.Enum(
                "Demo.FirstState",
                new[] { new FoxRunEnumValue("UNSPECIFIED_2", 1) });
            var second = FoxRunTypeShape.Enum(
                "Demo.SecondState",
                new[] { new FoxRunEnumValue("UNSPECIFIED", 1) });
            var contract = new FoxRunProtobufContractInput(
                "Demo.EnumSource",
                "/phase187/e01/enum-synthetic-future",
                "Demo.EnumEnvelope",
                new[]
                {
                    new FoxRunProtobufFieldInput(
                        "a",
                        "A",
                        first.TypeName,
                        false,
                        protobufFieldNumber: 1,
                        typeShape: first),
                    new FoxRunProtobufFieldInput(
                        "b",
                        "B",
                        second.TypeName,
                        false,
                        protobufFieldNumber: 2,
                        typeShape: second)
                });

            var descriptor = FileDescriptorSet.Parser.ParseFrom(
                FoxRunProtobufContractBuilder.Build(contract).FileDescriptorSet);
            var values = Assert.Single(descriptor.File).EnumType
                .SelectMany(enumDescriptor => enumDescriptor.Value)
                .Where(value => value.Number == 0)
                .Select(value => value.Name)
                .ToArray();

            Assert.Equal(new[] { "UNSPECIFIED_3", "UNSPECIFIED_4" }, values);
        }

        [Fact]
        [Trait("Phase", "187-R2-E01-001")]
        public void ProtobufDescriptorSuffixesTypeBeforeFutureEnumValueCollision()
        {
            var objectShape = FoxRunTypeShape.Object(
                "Demo_Ready",
                Array.Empty<FoxRunTypeField>());
            var enumShape = FoxRunTypeShape.Enum(
                "Demo.Enum",
                new[] { new FoxRunEnumValue("Demo_Ready", 0) });
            var contract = new FoxRunProtobufContractInput(
                "Demo.Source",
                "/phase187/e01/type-value-order",
                "Demo.Envelope",
                new[]
                {
                    new FoxRunProtobufFieldInput(
                        "object",
                        "A",
                        objectShape.TypeName,
                        false,
                        protobufFieldNumber: 1,
                        typeShape: objectShape),
                    new FoxRunProtobufFieldInput(
                        "enum",
                        "B",
                        enumShape.TypeName,
                        false,
                        protobufFieldNumber: 2,
                        typeShape: enumShape)
                });

            var descriptor = FileDescriptorSet.Parser.ParseFrom(
                FoxRunProtobufContractBuilder.Build(contract).FileDescriptorSet);

            Assert.Contains(
                Assert.Single(descriptor.File).MessageType,
                message => message.Name == "Demo_Ready_2");
            Assert.Contains(
                Assert.Single(descriptor.File).EnumType,
                enumDescriptor => enumDescriptor.Value.Any(value => value.Name == "Demo_Ready"));
        }

        [Fact]
        public void NestedDtoFieldNumberCollisionNamesTheExplicitTagEscapeHatch()
        {
            var conflictingDto = FoxRunTypeShape.Object(
                "Demo.ConflictingDto",
                new[]
                {
                    new FoxRunTypeField("first", "First", FoxRunTypeShape.Canonical("int32")),
                    new FoxRunTypeField("second", "Second", FoxRunTypeShape.Canonical("int32"))
                });
            var protobufMetadata = new FoxRunProtobufMetadata(
                0,
                new FoxRunProtobufTypeMetadata(
                    "Demo.ConflictingDto",
                    new[]
                    {
                        new FoxRunProtobufFieldMetadata("First", "first", 7),
                        new FoxRunProtobufFieldMetadata("Second", "second", 7)
                    }));
            var contract = new FoxRunProtobufContractInput(
                "Demo.WireState",
                "/phase175/conflicting_dto",
                "Demo.WireState",
                new[]
                {
                    new FoxRunProtobufFieldInput(
                        "payload",
                        "_payload",
                        "Demo.ConflictingDto",
                        false,
                        typeShape: conflictingDto,
                        protobufMetadata: protobufMetadata)
                });

            var error = Assert.Throws<InvalidOperationException>(() => FoxRunProtobufContractBuilder.Build(contract));

            Assert.Contains("Demo.ConflictingDto", error.Message, StringComparison.Ordinal);
            Assert.Contains("ProtobufFieldNumber", error.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void ModelValidatorRejectsMixedWirePoliciesAndDuplicateExplicitTags()
        {
            var model = FoxRunGenerationModel.FromMembers(new[]
            {
                new FoxRunGenerationMember(
                    "Demo", "WireState", "_count", "field", "System.Int32", true, false, "",
                    "/phase175/wire_state", 10f, "", (int)FoxRunPolicy.FixedRate, 0f, "UnitTest", 0, "",
                    encoding: "json", protobufFieldNumber: 17),
                new FoxRunGenerationMember(
                    "Demo", "WireState", "_otherCount", "field", "System.Int32", true, false, "",
                    "/phase175/wire_state", 10f, "", (int)FoxRunPolicy.FixedRate, 0f, "UnitTest", 1, "",
                    encoding: "protobuf", protobufFieldNumber: 17)
            });

            var diagnostics = FoxRunGenerationModelValidator.Validate(model);

            Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "FOXRUN604");
            Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "FOXRUN605");
        }

        [Fact]
        public void ModelValidatorRejectsPartialSchemaNameDeclarationsDeterministically()
        {
            var model = FoxRunGenerationModel.FromMembers(new[]
            {
                new FoxRunGenerationMember(
                    "Demo", "WireState", "_count", "field", "System.Int32", true, false, "",
                    "/phase175/partial_schema", 10f, "Demo.WireState", (int)FoxRunPolicy.FixedRate, 0f,
                    "UnitTest", 0, "", encoding: "protobuf"),
                new FoxRunGenerationMember(
                    "Demo", "WireState", "_enabled", "field", "System.Boolean", true, false, "",
                    "/phase175/partial_schema", 10f, "", (int)FoxRunPolicy.FixedRate, 0f,
                    "UnitTest", 1, "", encoding: "protobuf")
            });

            var diagnostic = Assert.Single(
                FoxRunGenerationModelValidator.Validate(model),
                value => value.Id == "FOXRUN002");

            Assert.Equal(
                "Topic members must either all omit SchemaName or all declare the same SchemaName.",
                diagnostic.Message);
        }

        [Fact]
        public void ModelValidatorReportsOneDeterministicExplicitTagCollision()
        {
            var model = FoxRunGenerationModel.FromMembers(new[]
            {
                new FoxRunGenerationMember(
                    "Demo", "WireState", "_first", "field", "System.Int32", true, false, "",
                    "/phase175/duplicate", 10f, "", (int)FoxRunPolicy.FixedRate, 0f, "UnitTest", 0, "",
                    encoding: "protobuf", protobufFieldNumber: 17),
                new FoxRunGenerationMember(
                    "Demo", "WireState", "_second", "field", "System.Int32", true, false, "",
                    "/phase175/duplicate", 10f, "", (int)FoxRunPolicy.FixedRate, 0f, "UnitTest", 1, "",
                    encoding: "protobuf", protobufFieldNumber: 17)
            });

            var collision = Assert.Single(
                FoxRunGenerationModelValidator.Validate(model),
                diagnostic => diagnostic.Id == "FOXRUN605");

            Assert.Equal(
                "FoxRun topic '/phase175/duplicate' has duplicate ProtobufFieldNumber 17.",
                collision.Message);
        }

        [Fact]
        public void SchemaInfoWriterEmbedsProtobufDescriptorSetForProtobufContracts()
        {
            var manifest = FoxRunManifestBuilder.Build(new[]
            {
                new FoxRunManifestMember(
                    "Demo", "WireState", "_count", "field", "System.Int32", true, false, "",
                    "/phase175/wire_state", 10f, "Demo.WireState", (int)FoxRunPolicy.FixedRate, 0f,
                    encoding: 1, protobufFieldNumber: 17)
            });

            var source = FoxRunSchemaInfoWriter.GenerateSource(manifest);

            Assert.Contains("global::System.Convert.FromBase64String", source, StringComparison.Ordinal);
            Assert.Contains("ProtobufDescriptorSet", typeof(Unity.FoxgloveSDK.Components.FoxRunSchemaContractInfo).GetProperty("ProtobufDescriptorSet").Name, StringComparison.Ordinal);
        }

        [Fact]
        [Trait("Phase", "186-A")]
        public void SchemaInfoWriterEmitsValidSyntaxAfterNeutralSubscriptionBindings()
        {
            var manifest = FoxRunManifestBuilder.Build(
                new[]
                {
                    new FoxRunManifestMember(
                        "Demo",
                        "InputPort",
                        "_value",
                        "field",
                        "System.Int32",
                        true,
                        false,
                        string.Empty,
                        "/phase186/input",
                        10f,
                        string.Empty,
                        (int)FoxRunPolicy.FixedRate,
                        0f,
                        flow: (int)FoxRunFlow.Subscribe,
                        generatesWebSocketCodec: false,
                        subscribeTransportId: "unity2foxglove.r2fu")
                },
                manifestVersion:
                    FoxrunManifestWriter.CurrentManifestVersion);

            var errors = CSharpSyntaxTree
                .ParseText(FoxRunSchemaInfoWriter.GenerateSource(manifest))
                .GetDiagnostics()
                .Where(diagnostic =>
                    diagnostic.Severity
                    == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                .ToArray();

            Assert.Empty(errors);
        }

    }
}
