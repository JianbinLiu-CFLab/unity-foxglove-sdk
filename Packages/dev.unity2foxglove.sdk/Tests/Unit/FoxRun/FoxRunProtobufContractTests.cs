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
                        encoding: (int)FoxRunWireEncoding.Json)
                },
                manifestVersion: 1);
            var jsonContract = Assert.Single(Assert.Single(jsonManifest.Sections.FoxRun.Types).Contracts);

            Assert.Equal("d241d4a5445597e86dacb8cd4fa6cb0693a025eb8aecceb37631c7da3efe3e16", jsonContract.ContractHash);
            Assert.Equal("dd4037ff4397dca2231b374e9972cce8838883482d0ace1d422132193fdf9f52", jsonContract.BindingHash);
            Assert.Equal("86bde8645ea3d1246bb10dc5a648b52c2da83848b7c63e30931e30a9cdd4f20d", jsonContract.PolicyHash);
            Assert.Equal("594de9104932f9719fc70c4132c65aa0b3b106b57262ea7b6b64d324c14e1f8e", jsonManifest.Sections.FoxRun.ManifestHash);

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
            Assert.NotNull(typeof(FoxRunManifestSections).GetProperty("Subscriptions"));
            Assert.NotNull(typeof(FoxRunManifestMember).GetProperty("SubscriptionProvider"));
            Assert.NotNull(typeof(FoxRunManifestMember).GetProperty("GeneratesWebSocketCodec"));
            Assert.NotNull(typeof(FoxRunManifestMember).GetProperty("GeneratesRos2NativeRegistration"));

            var writerSource = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunManifestWriter.cs");
            Assert.Contains("CurrentManifestVersion", writerSource, StringComparison.Ordinal);
            Assert.NotNull(typeof(FoxRunSchemaManifestInfo).GetProperty("SubscriptionBindings"));
            Assert.NotNull(typeof(FoxRunSchemaManifestInfo).Assembly.GetType(
                "Unity.FoxgloveSDK.Components.FoxRunSchemaSubscriptionBindingInfo"));
        }

        [Fact]
        public void NativeProviderMetadataUsesSeparateV2DigestWithoutChangingWireDigests()
        {
            var json = new FoxRunManifestMember(
                "Demo", "RobotState", "_batteryLevel", "field", "System.Single", true, false, "",
                "/phase112/battery", 10f, "", (int)FoxRunPolicy.Change, 0.001f,
                encoding: (int)FoxRunWireEncoding.Json,
                subscriptionProvider: FoxRunGenerationDescriptorConstants.FoxgloveWebSocketSubscriptionProvider,
                generatesWebSocketCodec: true);
            var native = new FoxRunManifestMember(
                "Demo", "RobotState", "_nativeText", "field", "std_msgs.msg.String", false, false, "",
                "/phase179/native", 0f, "std_msgs/msg/String", (int)FoxRunPolicy.FixedRate, 0f,
                flow: (int)FoxRunFlow.Subscribe,
                encoding: (int)FoxRunWireEncoding.Inherit,
                subscriptionProvider: FoxRunGenerationDescriptorConstants.Ros2NativeSubscriptionProvider,
                ros2Qos: FoxRunGenerationDescriptorConstants.SensorDataRos2Qos,
                generatesWebSocketCodec: false,
                generatesRos2NativeRegistration: true);

            var manifest = FoxRunManifestBuilder.Build(new[] { json, native }, manifestVersion: 2);
            var jsonOnlyV1 = FoxRunManifestBuilder.Build(new[] { json }, manifestVersion: 1);
            var contract = Assert.Single(Assert.Single(manifest.Sections.FoxRun.Types).Contracts);

            Assert.Equal(2, manifest.ManifestVersion);
            Assert.Equal("json", contract.Encoding);
            Assert.Equal("d241d4a5445597e86dacb8cd4fa6cb0693a025eb8aecceb37631c7da3efe3e16", contract.ContractHash);
            Assert.Equal("dd4037ff4397dca2231b374e9972cce8838883482d0ace1d422132193fdf9f52", contract.BindingHash);
            Assert.Single(manifest.Sections.Subscriptions.Bindings);
            Assert.True(FoxRunManifestHasher.IsLowercaseSha256Hex(manifest.Sections.Subscriptions.ManifestHash));
            Assert.Equal("594de9104932f9719fc70c4132c65aa0b3b106b57262ea7b6b64d324c14e1f8e", jsonOnlyV1.Sections.FoxRun.ManifestHash);

            var canonical = FoxRunManifestJsonWriter.WriteCanonical(manifest);
            Assert.Contains("\"subscriptions\"", canonical, StringComparison.Ordinal);
            Assert.Contains("\"declaredProvider\":\"ros2-native\"", canonical, StringComparison.Ordinal);
            Assert.DoesNotContain("\"encoding\":\"cdr\"", canonical, StringComparison.Ordinal);
            Assert.DoesNotContain("\"encoding\":\"ros2\"", canonical, StringComparison.Ordinal);

            var qosChanged = new FoxRunManifestMember(
                "Demo", "RobotState", "_nativeText", "field", "std_msgs.msg.String", false, false, "",
                "/phase179/native", 0f, "std_msgs/msg/String", (int)FoxRunPolicy.FixedRate, 0f,
                flow: (int)FoxRunFlow.Subscribe,
                encoding: (int)FoxRunWireEncoding.Inherit,
                subscriptionProvider: FoxRunGenerationDescriptorConstants.Ros2NativeSubscriptionProvider,
                ros2Qos: FoxRunGenerationDescriptorConstants.ReliableRos2Qos,
                generatesWebSocketCodec: false,
                generatesRos2NativeRegistration: true);
            var changed = FoxRunManifestBuilder.Build(new[] { json, qosChanged }, manifestVersion: 2);

            Assert.Equal(manifest.Sections.FoxRun.ManifestHash, changed.Sections.FoxRun.ManifestHash);
            Assert.NotEqual(manifest.Sections.Subscriptions.ManifestHash, changed.Sections.Subscriptions.ManifestHash);
            Assert.NotEqual(manifest.GlobalManifestHash, changed.GlobalManifestHash);
        }

        [Fact]
        public void SchemaInfoWriterCarriesV2SubscriptionBindingsIntoRuntimeMetadata()
        {
            var shape = new FoxRunRos2MessageShape(
                "std_msgs.msg.String",
                "std_msgs/msg/String",
                hasPublicParameterlessConstructor: true,
                implementsRos2Message: true,
                copyShapeIdentity: "std-string-copy-v1",
                members: Array.Empty<FoxRunRos2MessageMemberShape>(),
                diagnostics: Array.Empty<string>());
            var manifest = FoxRunManifestBuilder.Build(
                new[]
                {
                    new FoxRunManifestMember(
                        "Demo", "RobotState", "_nativeText", "field", "std_msgs.msg.String", false, false, "",
                        "/phase179/native", 0f, "std_msgs/msg/String", (int)FoxRunPolicy.FixedRate, 0f,
                        flow: (int)FoxRunFlow.Subscribe,
                        encoding: (int)FoxRunWireEncoding.Inherit,
                        subscriptionProvider: FoxRunGenerationDescriptorConstants.Ros2NativeSubscriptionProvider,
                        ros2Qos: FoxRunGenerationDescriptorConstants.SensorDataRos2Qos,
                        generatesWebSocketCodec: false,
                        generatesRos2NativeRegistration: true,
                        ros2MessageShape: shape)
                },
                manifestVersion: 2);

            var source = FoxRunSchemaInfoWriter.GenerateSource(manifest);

            Assert.Contains("public const int ManifestVersion = 2;", source, StringComparison.Ordinal);
            Assert.Contains("public const int SubscriptionBindingCount = 1;", source, StringComparison.Ordinal);
            Assert.Contains("public const int CustomNativeContractCount = 0;", source, StringComparison.Ordinal);
            Assert.Contains("public const string SubscriptionManifestHash =", source, StringComparison.Ordinal);
            Assert.Contains("new FoxRunSchemaSubscriptionBindingInfo(", source, StringComparison.Ordinal);
            Assert.Contains("FoxRunSubscriptionProvider.Ros2Native", source, StringComparison.Ordinal);
            Assert.Contains("FoxRunRos2QosPreset.SensorData", source, StringComparison.Ordinal);
            Assert.Contains("\"std_msgs.msg.String\"", source, StringComparison.Ordinal);
            Assert.Contains("\"std_msgs/msg/String\"", source, StringComparison.Ordinal);
            Assert.Contains("\"std-string-copy-v1\"", source, StringComparison.Ordinal);
            Assert.Contains("SubscriptionBindings,", source, StringComparison.Ordinal);
            Assert.Contains("CustomNativeContracts));", source, StringComparison.Ordinal);
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
                        encoding: (int)FoxRunWireEncoding.Json)
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
                    field.ProtobufFieldNumber,
                    field.ProtobufTypeShape)).ToList()))
                .MessageFullName;

            Assert.Equal(expectedSchemaName, protobufContract.SchemaName);
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
        public void NestedDtoFieldNumberCollisionNamesTheExplicitTagEscapeHatch()
        {
            var conflictingDto = FoxRunProtobufTypeShape.Object(
                "Demo.ConflictingDto",
                new[]
                {
                    new FoxRunProtobufTypeField("first", "First", FoxRunProtobufTypeShape.Canonical("int32"), protobufFieldNumber: 7),
                    new FoxRunProtobufTypeField("second", "Second", FoxRunProtobufTypeShape.Canonical("int32"), protobufFieldNumber: 7)
                });
            var contract = new FoxRunProtobufContractInput(
                "Demo.WireState",
                "/phase175/conflicting_dto",
                "Demo.WireState",
                new[]
                {
                    new FoxRunProtobufFieldInput("payload", "_payload", "Demo.ConflictingDto", false, typeShape: conflictingDto)
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

                var summary = Assert.Single(FoxRunSchemaInfoRegistry.GetTopicSummaries(FoxRunWireEncoding.Protobuf));

                Assert.Equal(FoxRunWireEncoding.Inherit, summary.DeclaredEncoding);
                Assert.Equal(FoxRunWireEncoding.Protobuf, summary.EffectiveEncoding);
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
                    FoxRunWireEncoding.Protobuf,
                    FoxRunWireEncoding.Json);

                Assert.Collection(
                    summaries,
                    input =>
                    {
                        Assert.Equal("/phase176/input", input.Topic);
                        Assert.Equal(FoxRunWireEncoding.Json, input.EffectiveEncoding);
                    },
                    output =>
                    {
                        Assert.Equal("/phase176/output", output.Topic);
                        Assert.Equal(FoxRunWireEncoding.Protobuf, output.EffectiveEncoding);
                    });
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
                    "/phase175/wire_state", 10f, "Demo.WireState", (int)FoxRunPolicy.FixedRate, 0f,
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
