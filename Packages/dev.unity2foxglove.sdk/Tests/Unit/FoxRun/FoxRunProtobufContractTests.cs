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
        [Trait("Phase", "186-H")]
        public void ShippedManualAggregateSchemasUseDistinctProtobufRegistrationKeys()
        {
            var phase154Source = TestSources.Text(
                "Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase153and154ManualAcceptance.cs");
            var phase155Source = TestSources.Text(
                "Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase155and156ManualAcceptance.cs");

            var phase154Schema = ExtractAggregateSchemaName(phase154Source);
            var phase155Schema = ExtractAggregateSchemaName(phase155Source);
            var phase154WireName = FoxRunProtobufContractBuilder.ResolveMessageFullName(
                phase154Schema,
                "Phase153and154ManualAcceptance",
                "/phase154/vehicle");
            var phase155WireName = FoxRunProtobufContractBuilder.ResolveMessageFullName(
                phase155Schema,
                "Phase155and156ManualAcceptance",
                "/phase155/vehicle");

            Assert.NotEqual(phase154WireName, phase155WireName);
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
        [Trait("Phase", "187-R2-E01-001")]
        public void ProtobufDescriptorRejectsInvalidNormalizedIdentifiers()
        {
            var collision = new FoxRunProtobufContractInput(
                "Demo.InvalidNames",
                "/phase187/e01/collision",
                "Demo.InvalidNames",
                new[]
                {
                    new FoxRunProtobufFieldInput("a-b", "First", "int32", false),
                    new FoxRunProtobufFieldInput("a_b", "Second", "int32", false)
                });

            var collisionError = Assert.Throws<InvalidOperationException>(
                () => FoxRunProtobufContractBuilder.Build(collision));
            Assert.Contains("identifier", collisionError.Message, StringComparison.OrdinalIgnoreCase);

            var nonAscii = new FoxRunProtobufContractInput(
                "Demo.InvalidNames",
                "/phase187/e01/non-ascii",
                "Demo.InvalidNames",
                new[]
                {
                    new FoxRunProtobufFieldInput("温度", "Temperature", "float32", false)
                });

            var nonAsciiError = Assert.Throws<InvalidOperationException>(
                () => FoxRunProtobufContractBuilder.Build(nonAscii));
            Assert.Contains("ASCII", nonAsciiError.Message, StringComparison.OrdinalIgnoreCase);
        }

    }
}
