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
    public sealed class FoxRunProtobufContractTests
    {
        [Fact]
        public void DeclarationResetJsonAndProtobufContractsKeepCanonicalGoldenIdentity()
        {
            var jsonManifest = FoxRunManifestBuilder.Build(
                new[]
                {
                    new FoxRunManifestMember(
                        "Demo", "RobotState", "_batteryLevel", "field", "System.Single", true, false, "",
                        "/phase112/battery", 10f, "", (int)FoxRunPolicy.Change, 0.001f,
                        encoding: (int)FoxRunEncoding.JSON)
                },
                manifestVersion: 1);
            var jsonContract = Assert.Single(Assert.Single(jsonManifest.Sections.FoxRun.Types).Contracts);

            Assert.Equal("3a171385ef84247fd8fc3fd37a49619155bec770691804c04d879f7e70cf5207", jsonContract.ContractHash);
            Assert.Equal("dd4037ff4397dca2231b374e9972cce8838883482d0ace1d422132193fdf9f52", jsonContract.BindingHash);
            Assert.Equal("86bde8645ea3d1246bb10dc5a648b52c2da83848b7c63e30931e30a9cdd4f20d", jsonContract.PolicyHash);
            Assert.Equal("262502c3999d4140c8b809fc0110ea5ea2fa4898702a117743140e672502fcef", jsonManifest.Sections.FoxRun.ManifestHash);

            var protobuf = FoxRunProtobufContractBuilder.Build(CreateContract());
            Assert.Equal(
                "CvoBChZmb3hydW4vV2lyZVN0YXRlLnByb3RvEhV1bml0eTJmb3hnbG92ZS5mb3hydW4ihQEKCVdpcmVTdGF0ZRIUCgVjb3VudBgRIAEoBVIFY291bnQSRAoIcG9zaXRpb24Y35SypgEgASgLMiQudW5pdHkyZm94Z2xvdmUuZm94cnVuLlVuaXR5X1ZlY3RvcjNSCHBvc2l0aW9uEhwKB3NhbXBsZXMYr9j4ngEgAygCUgdzYW1wbGVzIjkKDVVuaXR5X1ZlY3RvcjMSDAoBeBgBIAEoAlIBeBIMCgF5GAIgASgCUgF5EgwKAXoYAyABKAJSAXpiBnByb3RvMw==",
                Convert.ToBase64String(protobuf.FileDescriptorSet));
            using var sha256 = SHA256.Create();
            Assert.Equal(
                "e4bec17adf20ae1d18763954f3e9acb8a476843ba800f042d18dd25dce4f974c",
                BitConverter.ToString(sha256.ComputeHash(protobuf.FileDescriptorSet)).Replace("-", string.Empty).ToLowerInvariant());

            var descriptor = FileDescriptorSet.Parser.ParseFrom(protobuf.FileDescriptorSet);
            var root = Assert.Single(Assert.Single(descriptor.File).MessageType, message => message.Name == "WireState");
            Assert.Equal(17, Assert.Single(root.Field, field => field.Name == "count").Number);
        }

        [Fact]
        public void CanonicalManifestExposesSeparateSubscriptionBindingSection()
        {
            Assert.Equal(4, FoxrunManifestWriter.CurrentManifestVersion);
            Assert.NotNull(typeof(FoxRunManifestSections).GetProperty("Subscriptions"));
            Assert.NotNull(typeof(FoxRunManifestMember).GetProperty("SubscribeTransportId"));
            Assert.NotNull(typeof(FoxRunManifestMember).GetProperty("PublishTransportIds"));
            Assert.NotNull(typeof(FoxRunManifestMember).GetProperty("GeneratesWebSocketCodec"));

            var writerSource = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunManifestWriter.cs");
            Assert.Contains("CurrentManifestVersion", writerSource, StringComparison.Ordinal);
            Assert.NotNull(typeof(FoxRunSchemaManifestInfo).GetProperty("SubscriptionBindings"));
            Assert.NotNull(typeof(FoxRunSchemaManifestInfo).Assembly.GetType(
                "Unity.FoxgloveSDK.Components.FoxRunSchemaSubscriptionBindingInfo"));
        }



        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public void LegacyManifestVersionsRemainAvailableForPublishOnlyHistory(int manifestVersion)
        {
            var publish = new FoxRunManifestMember(
                "Demo", "LegacyOutput", "_value", "field", "System.Int32", true, false, "",
                "/phase184/legacy-output", 10f, "", (int)FoxRunPolicy.FixedRate, 0f,
                flow: (int)FoxRunFlow.Publish,
                encoding: (int)FoxRunEncoding.JSON);

            var manifest = FoxRunManifestBuilder.Build(
                new[] { publish },
                manifestVersion: manifestVersion);

            Assert.Equal(manifestVersion, manifest.ManifestVersion);
            Assert.True(manifest.ManifestVersion < FoxrunManifestWriter.CurrentManifestVersion);
            Assert.Equal(1, manifest.Generator.MajorVersion);
            Assert.Empty(manifest.Sections.Subscriptions.Bindings);
        }

        [Fact]
        public void SubscriptionBindingSelectionUsesTheV3CapabilityGate()
        {
            var builderSource = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunManifest/FoxRunManifestBuilder.cs");
            var legacyV2 = FoxRunManifestBuilder.Build(
                Array.Empty<FoxRunManifestMember>(),
                manifestVersion: 2);

            Assert.Contains(
                "var subscriptionBindings = manifestVersion >= 3",
                builderSource,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "var subscriptionBindings = manifestVersion >= 2",
                builderSource,
                StringComparison.Ordinal);
            Assert.Empty(legacyV2.Sections.Subscriptions.Bindings);
            Assert.True(FoxRunManifestHasher.IsLowercaseSha256Hex(
                legacyV2.Sections.Subscriptions.ManifestHash));
        }





        [Fact]
        public void LegacyV1ManifestAndRuntimeDtoTreatMissingSubscriptionsAsEmpty()
        {
            var manifest = FoxRunManifestBuilder.Build(
                new[]
                {
                    new FoxRunManifestMember(
                        "Demo", "RobotState", "_batteryLevel", "field", "System.Single", true, false, "",
                        "/phase112/battery", 10f, "", (int)FoxRunPolicy.Change, 0.001f,
                        encoding: (int)FoxRunEncoding.JSON)
                },
                manifestVersion: 1);
            var canonical = FoxRunManifestJsonWriter.WriteCanonical(manifest);
            var legacyRuntimeDto = new FoxRunSchemaManifestInfo(
                1,
                "dev.unity2foxglove.sdk",
                "FoxRunSchemaManifestGenerator",
                1,
                manifest.GlobalManifestHash,
                manifest.Sections.FoxRun.ManifestHash,
                Array.Empty<FoxRunSchemaTypeInfo>());

            Assert.Empty(manifest.Sections.Subscriptions.Bindings);
            Assert.Equal(string.Empty, manifest.Sections.Subscriptions.ManifestHash);
            Assert.DoesNotContain("\"subscriptions\"", canonical, StringComparison.Ordinal);
            Assert.DoesNotContain("\"subscriptionManifestHash\"", canonical, StringComparison.Ordinal);
            Assert.Empty(legacyRuntimeDto.SubscriptionBindings);
            Assert.Equal(string.Empty, legacyRuntimeDto.SubscriptionManifestHash);
        }

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
        public void AddingFieldDoesNotChangeExistingAutomaticTag()
        {
            var count = new FoxRunProtobufFieldInput("count", "_count", "int32", false);
            var original = FoxRunProtobufContractBuilder.Build(new FoxRunProtobufContractInput(
                "Demo.WireState",
                "/phase175/wire_state",
                "Demo.WireState",
                new[] { count }));
            var expanded = FoxRunProtobufContractBuilder.Build(new FoxRunProtobufContractInput(
                "Demo.WireState",
                "/phase175/wire_state",
                "Demo.WireState",
                new[] { count, new FoxRunProtobufFieldInput("label", "_label", "string", false) }));

            var originalMessage = Assert.Single(FileDescriptorSet.Parser.ParseFrom(original.FileDescriptorSet).File.Single().MessageType);
            var expandedMessage = Assert.Single(FileDescriptorSet.Parser.ParseFrom(expanded.FileDescriptorSet).File.Single().MessageType);

            Assert.Equal(
                originalMessage.Field.Single(field => field.Name == "count").Number,
                expandedMessage.Field.Single(field => field.Name == "count").Number);
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
        public void ImplicitProtobufSchemaNamesAreStableAndUniquePerTopic()
        {
            var first = FoxRunProtobufContractBuilder.Build(new FoxRunProtobufContractInput(
                "Demo.Counter",
                "/phase175/first",
                "",
                new[] { new FoxRunProtobufFieldInput("count", "_count", "int32", false) }));
            var repeatedFirst = FoxRunProtobufContractBuilder.Build(new FoxRunProtobufContractInput(
                "Demo.Counter",
                "/phase175/first",
                "",
                new[] { new FoxRunProtobufFieldInput("count", "_count", "int32", false) }));
            var second = FoxRunProtobufContractBuilder.Build(new FoxRunProtobufContractInput(
                "Demo.Counter",
                "/phase175/second",
                "",
                new[] { new FoxRunProtobufFieldInput("count", "_count", "int32", false) }));

            Assert.Equal(first.MessageFullName, repeatedFirst.MessageFullName);
            Assert.NotEqual(first.MessageFullName, second.MessageFullName);
        }

        [Fact]
        public void ManifestUsesTheDescriptorSchemaNameForImplicitProtobufContracts()
        {
            var manifest = FoxRunManifestBuilder.Build(new[]
            {
                new FoxRunManifestMember(
                    "Demo", "Counter", "_count", "field", "System.Int32", true, false, "",
                    "/phase175/implicit", 10f, "", (int)FoxRunPolicy.FixedRate, 0f,
                    encoding: 0, protobufFieldNumber: 17)
            });
            var protobufContract = Assert.Single(
                manifest.Sections.FoxRun.Types.Single().Contracts,
                contract => contract.Encoding == "protobuf");
            var expectedSchemaName = FoxRunProtobufContractBuilder.Build(new FoxRunProtobufContractInput(
                protobufContract.DeclaringType,
                protobufContract.Topic,
                protobufContract.SchemaName,
                protobufContract.Fields.Select(field => new FoxRunProtobufFieldInput(
                    field.JsonName,
                    field.MemberName,
                    field.Type,
                    field.Array,
                    0,
                    field.TypeShape,
                    field.ProtobufMetadata)).ToList()))
                .MessageFullName;

            Assert.Equal(expectedSchemaName, protobufContract.SchemaName);
        }

        [Fact]
        public void ContractBuilderEmitsNestedDtoGraphWithoutJsonFallback()
        {
            var pose = FoxRunTypeShape.Object(
                "Demo.Pose",
                new[]
                {
                    new FoxRunTypeField("position", "position", FoxRunTypeShape.Canonical("unity.vector3.float32"))
                });
            var telemetry = FoxRunTypeShape.Object(
                "Demo.VehicleTelemetry",
                new[]
                {
                    new FoxRunTypeField("label", "label", FoxRunTypeShape.Canonical("string")),
                    new FoxRunTypeField("pose", "pose", pose),
                    new FoxRunTypeField("samples", "samples", FoxRunTypeShape.Canonical("float32"), repeated: true)
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

        [Fact]
        public void NestedDtoShapeContributesToProtobufContractHash()
        {
            var stringShape = FoxRunTypeShape.Object(
                "Demo.Telemetry",
                new[] { new FoxRunTypeField("value", "Value", FoxRunTypeShape.Canonical("string")) });
            var floatShape = FoxRunTypeShape.Object(
                "Demo.Telemetry",
                new[] { new FoxRunTypeField("value", "Value", FoxRunTypeShape.Canonical("float32")) });

            var first = BuildProtobufManifest(stringShape);
            var second = BuildProtobufManifest(floatShape);

            Assert.NotEqual(
                first.Sections.FoxRun.Types[0].Contracts[0].ContractHash,
                second.Sections.FoxRun.Types[0].Contracts[0].ContractHash);
        }

        [Fact]
        public void SchemaInfoWriterEmbedsNestedDtoDescriptorFromManifestShape()
        {
            var shape = FoxRunTypeShape.Object(
                "Demo.Telemetry",
                new[] { new FoxRunTypeField("value", "Value", FoxRunTypeShape.Canonical("string")) });
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
        public void TopicSummariesKeepInheritPolicyWhenCodecVariantsUseDistinctSchemas()
        {
            var fields = new[] { new FoxRunSchemaFieldInfo("count", "_count", "field", "int32", false, false, false, 17) };
            var json = new FoxRunSchemaContractInfo(
                "Demo.Counter", "/phase175/implicit", string.Empty, "json",
                "json-contract", "json-binding", "policy", "FixedRate", 10f, 0f, fields);
            var protobuf = new FoxRunSchemaContractInfo(
                "Demo.Counter", "/phase175/implicit", "unity2foxglove.foxrun.Demo_Counter_a1b2c3d4", "protobuf",
                "protobuf-contract", "protobuf-binding", "policy", "FixedRate", 10f, 0f, fields,
                protobufDescriptorSet: new byte[] { 1 });
            var manifest = new FoxRunSchemaManifestInfo(
                1,
                "Unity2Foxglove",
                "FoxRun",
                1,
                "global",
                "foxrun",
                new[] { new FoxRunSchemaTypeInfo("Demo.Counter", new[] { json, protobuf }) });

            FoxRunSchemaInfoRegistry.ClearForTests();
            try
            {
                FoxRunSchemaInfoRegistry.RegisterGenerated(manifest);

                var summary = Assert.Single(FoxRunSchemaInfoRegistry.GetTopicSummaries(FoxRunEncoding.Protobuf));

                Assert.Equal((FoxRunEncoding)0, summary.DeclaredEncoding);
                Assert.Equal(FoxRunEncoding.Protobuf, summary.EffectiveEncoding);
                Assert.Equal(protobuf.SchemaName, summary.SchemaName);
            }
            finally
            {
                FoxRunSchemaInfoRegistry.ClearForTests();
            }
        }

        [Fact]
        public void TopicSummariesResolveInheritedContractsAgainstTheirFlowDefaults()
        {
            var fields = new[] { new FoxRunSchemaFieldInfo("count", "_count", "field", "int32", false, false, false, 17) };
            var contracts = new[]
            {
                new FoxRunSchemaContractInfo("Demo.Output", "/phase176/output", string.Empty, "json", "json-output", "json-output", "policy", "FixedRate", 10f, 0f, fields, flow: "Publish"),
                new FoxRunSchemaContractInfo("Demo.Output", "/phase176/output", "unity2foxglove.foxrun.Demo_Output", "protobuf", "protobuf-output", "protobuf-output", "policy", "FixedRate", 10f, 0f, fields, flow: "Publish", protobufDescriptorSet: new byte[] { 1 }),
                new FoxRunSchemaContractInfo("Demo.Input", "/phase176/input", string.Empty, "json", "json-input", "json-input", "policy", "FixedRate", 10f, 0f, fields, flow: "Subscribe"),
                new FoxRunSchemaContractInfo("Demo.Input", "/phase176/input", "unity2foxglove.foxrun.Demo_Input", "protobuf", "protobuf-input", "protobuf-input", "policy", "FixedRate", 10f, 0f, fields, flow: "Subscribe", protobufDescriptorSet: new byte[] { 2 })
            };
            var manifest = new FoxRunSchemaManifestInfo(
                1,
                "Unity2Foxglove",
                "FoxRun",
                1,
                "global",
                "foxrun",
                new[] { new FoxRunSchemaTypeInfo("Demo.Contracts", contracts) });

            FoxRunSchemaInfoRegistry.ClearForTests();
            try
            {
                FoxRunSchemaInfoRegistry.RegisterGenerated(manifest);

                var summaries = FoxRunSchemaInfoRegistry.GetTopicSummaries(
                    FoxRunEncoding.Protobuf,
                    FoxRunEncoding.JSON);

                Assert.Collection(
                    summaries,
                    input =>
                    {
                        Assert.Equal("/phase176/input", input.Topic);
                        Assert.Equal(FoxRunEncoding.JSON, input.EffectiveEncoding);
                    },
                    output =>
                    {
                        Assert.Equal("/phase176/output", output.Topic);
                        Assert.Equal(FoxRunEncoding.Protobuf, output.EffectiveEncoding);
                    });
            }
            finally
            {
                FoxRunSchemaInfoRegistry.ClearForTests();
            }
        }

        [Fact]
        [Trait("Phase", "185-A")]
        public void FullDuplexTopicSummariesResolvePublishAndSubscribeDirectionsIndependently()
        {
            var fields = new[]
            {
                new FoxRunSchemaFieldInfo(
                    "state",
                    "_state",
                    "field",
                    "int32",
                    false,
                    false,
                    typeShape: new FoxRunTypeShapeInfo(
                        FoxRunTypeShapeInfoKind.Canonical,
                        "int32",
                        "int32",
                        false,
                        FoxRunCollectionInfoKind.None,
                        null,
                        Array.Empty<FoxRunTypeFieldInfo>(),
                        Array.Empty<FoxRunEnumValueInfo>()))
            };
            var contracts = new[]
            {
                new FoxRunSchemaContractInfo(
                    "Demo.Duplex", "/phase185/duplex", string.Empty, "json",
                    "json", "json", "policy", "FixedRate", 10f, 0f, fields,
                    flow: "PublishAndSubscribe", logicalSchemaName: "Demo.State"),
                new FoxRunSchemaContractInfo(
                    "Demo.Duplex", "/phase185/duplex", "Demo.State", "protobuf",
                    "protobuf", "protobuf", "policy", "FixedRate", 10f, 0f, fields,
                    flow: "PublishAndSubscribe", protobufDescriptorSet: new byte[] { 1 },
                    logicalSchemaName: "Demo.State"),
                new FoxRunSchemaContractInfo(
                    "Demo.Duplex", "/phase185/duplex", string.Empty, "msgpack",
                    "msgpack", "msgpack", "policy", "FixedRate", 10f, 0f, fields,
                    flow: "PublishAndSubscribe", logicalSchemaName: "Demo.State")
            };
            var manifest = new FoxRunSchemaManifestInfo(
                3,
                "Unity2Foxglove",
                "FoxRun",
                1,
                "global",
                "foxrun",
                new[] { new FoxRunSchemaTypeInfo("Demo.Duplex", contracts) });

            FoxRunSchemaInfoRegistry.ClearForTests();
            try
            {
                FoxRunSchemaInfoRegistry.RegisterGenerated(manifest);

                var summaries = FoxRunSchemaInfoRegistry.GetTopicSummaries(
                    FoxRunEncoding.MessagePack,
                    FoxRunEncoding.JSON);

                Assert.Collection(
                    summaries,
                    publish =>
                    {
                        Assert.Equal("Publish", publish.Direction);
                        Assert.Equal(FoxRunEncoding.MessagePack, publish.EffectiveEncoding);
                        Assert.Equal(string.Empty, publish.WireSchemaName);
                        Assert.Equal("Demo.State", publish.LogicalSchemaName);
                    },
                    subscribe =>
                    {
                        Assert.Equal("Subscribe", subscribe.Direction);
                        Assert.Equal(FoxRunEncoding.JSON, subscribe.EffectiveEncoding);
                        Assert.Equal("Demo.State", subscribe.LogicalSchemaName);
                    });
            }
            finally
            {
                FoxRunSchemaInfoRegistry.ClearForTests();
            }
        }

        [Fact]
        [Trait("Phase", "185-A")]
        public void TopicSummaryPreservesUnavailableInheritedMessagePackReasonWithoutFallback()
        {
            var fields = new[]
            {
                new FoxRunSchemaFieldInfo("state", "_state", "field", "int32", false, false)
            };
            var contracts = new[]
            {
                new FoxRunSchemaContractInfo(
                    "Demo.Input", "/phase185/input", string.Empty, "json",
                    "json", "json", "policy", "FixedRate", 10f, 0f, fields,
                    flow: "Subscribe", logicalSchemaName: "Demo.State"),
                new FoxRunSchemaContractInfo(
                    "Demo.Input", "/phase185/input", "Demo.State", "protobuf",
                    "protobuf", "protobuf", "policy", "FixedRate", 10f, 0f, fields,
                    flow: "Subscribe", logicalSchemaName: "Demo.State"),
                new FoxRunSchemaContractInfo(
                    "Demo.Input", "/phase185/input", string.Empty, "msgpack",
                    "msgpack", "msgpack", "policy", "FixedRate", 10f, 0f, fields,
                    flow: "Subscribe",
                    logicalSchemaName: "Demo.State",
                    subscribeAvailable: false,
                    unavailableDiagnosticId: "FOXRUN618",
                    unavailableReason: "mixed ordinary/stream")
            };
            var manifest = new FoxRunSchemaManifestInfo(
                3,
                "Unity2Foxglove",
                "FoxRun",
                1,
                "global",
                "foxrun",
                new[] { new FoxRunSchemaTypeInfo("Demo.Input", contracts) });

            FoxRunSchemaInfoRegistry.ClearForTests();
            try
            {
                FoxRunSchemaInfoRegistry.RegisterGenerated(manifest);

                var summary = Assert.Single(FoxRunSchemaInfoRegistry.GetTopicSummaries(
                    FoxRunEncoding.Protobuf,
                    FoxRunEncoding.MessagePack));

                Assert.Equal(FoxRunEncoding.MessagePack, summary.EffectiveEncoding);
                Assert.False(summary.Available);
                Assert.Equal("FOXRUN618", summary.UnavailableDiagnosticId);
                Assert.DoesNotContain("protobuf", summary.UnavailableReason, StringComparison.OrdinalIgnoreCase);
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
