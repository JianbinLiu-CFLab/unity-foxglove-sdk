// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/FoxRun
// Purpose: Locks strict v5 descriptor provenance and the one supported v4 read path.

using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.Tests;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.FoxRun
{
    [Trait("Phase", "185-A")]
    [Trait("Domain", "FoxRun")]
    public sealed class FoxRunGenerationDescriptorCompatibilityTests
    {
        [Fact]
        public void CurrentWriterUsesTheLockedV5PairAndSharedMessagePackSpelling()
        {
            Assert.Equal(5, FoxRunGenerationDescriptorConstants.DescriptorVersion);
            Assert.Equal("5.0.0", FoxRunGenerationDescriptorConstants.GeneratorVersion);

            var field = typeof(FoxRunGenerationDescriptorConstants).GetField("MessagePackEncoding");
            Assert.NotNull(field);
            Assert.Equal("msgpack", field.GetRawConstantValue());
        }

        [Fact]
        public void DescriptorCarriesRequiredV5ShapeAvailabilityAndScheduleFields()
        {
            var model = FoxRunGenerationModel.FromMembers(new[]
            {
                new FoxRunGenerationMember(
                    "Demo", "State", "_value", "field", "System.Int32",
                    true, false, "", "/phase185/state", 10f, "Demo.State",
                    1, 0f, "UnitTest", 0, "",
                    encoding: "msgpack",
                    typeShape: FoxRunTypeShape.Canonical("int32"))
            });

            var json = FoxRunGenerationDescriptorJsonWriter.Write(model);

            Assert.Contains("\"typeShape\":", json, StringComparison.Ordinal);
            Assert.Contains("\"isValueType\":true", json, StringComparison.Ordinal);
            Assert.Contains("\"encodingVariants\":", json, StringComparison.Ordinal);
            Assert.Contains("\"normalizedSchedule\":", json, StringComparison.Ordinal);
            Assert.Contains("\"publishUnavailableDiagnosticId\":", json, StringComparison.Ordinal);
            Assert.Contains("\"subscribeUnavailableDiagnosticId\":", json, StringComparison.Ordinal);
        }

        [Fact]
        public void StrictV5RejectsNullTypeShapeWhenMessagePackVariantIsPresent()
        {
            var root = JObject.Parse(CurrentDescriptorJson());
            var member = (JObject)root["types"]![0]!["members"]![0]!;
            member["typeShape"] = JValue.CreateNull();

            var error = Assert.Throws<InvalidOperationException>(
                () => FoxRunGenerationDescriptorJsonReader.Read(root.ToString()));

            Assert.Contains("MessagePack", error.Message, StringComparison.Ordinal);
            Assert.Contains("typeShape", error.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void StrictV5RoundTripPreservesRecursiveShapeAvailabilityAndSchedule()
        {
            var typeShape = FoxRunTypeShape.Object(
                "Demo.State",
                new[]
                {
                    new FoxRunTypeField(
                        "mode",
                        "Mode",
                        FoxRunTypeShape.Enum(
                            "Demo.Mode",
                            new[]
                            {
                                new FoxRunEnumValue("UNSPECIFIED", 0),
                                new FoxRunEnumValue("ACTIVE", 7)
                            })),
                    new FoxRunTypeField(
                        "samples",
                        "Samples",
                        FoxRunTypeShape.Canonical("float32"),
                        repeated: true,
                        repeatedCollectionKind: FoxRunCollectionKind.List)
                },
                isValueType: true);
            var protobufMetadata = new FoxRunProtobufMetadata(
                5,
                new FoxRunProtobufTypeMetadata(
                    "Demo.State",
                    new[]
                    {
                        new FoxRunProtobufFieldMetadata("Mode", "mode", 5),
                        new FoxRunProtobufFieldMetadata("Samples", "samples", 9)
                    }));
            var variants = new[]
            {
                new FoxRunEncodingVariantAvailability(
                    "msgpack",
                    publishAvailable: false,
                    subscribeAvailable: false,
                    publishUnavailableDiagnosticId: "FOXRUN619",
                    publishUnavailableReason: "mixed publish schedule",
                    subscribeUnavailableDiagnosticId: "FOXRUN618",
                    subscribeUnavailableReason: "mixed stream topology")
            };
            var schedule = new FoxRunNormalizedScheduleTuple(
                (int)FoxRunPolicy.Change,
                hasExplicitHz: true,
                hz: 30f,
                tolerance: 0.25f,
                onlyIf: "IsReady",
                conditionMemberKind: FoxRunConditionMemberKind.Property);
            var model = FoxRunGenerationModel.FromMembers(new[]
            {
                new FoxRunGenerationMember(
                    "Demo", "State", "_value", "field", "Demo.State",
                    false, false, "", "/phase185/state", 30f, "Demo.State",
                    (int)FoxRunPolicy.Change, 0.25f, "UnitTest", 0, "",
                    onlyIf: "IsReady",
                    mode: (int)FoxRunFlow.PublishAndSubscribe,
                    encoding: FoxRunGenerationDescriptorConstants.MessagePackEncoding,
                    protobufFieldNumber: 5,
                    typeShape: typeShape,
                    namedArgumentPresence:
                        FoxRunNamedArgumentPresence.Encoding
                        | FoxRunNamedArgumentPresence.Hz
                        | FoxRunNamedArgumentPresence.OnlyIf,
                    conditionMemberKind: FoxRunConditionMemberKind.Property,
                    encodingVariants: variants,
                    normalizedSchedule: schedule,
                    protobufMetadata: protobufMetadata)
            });

            var read = FoxRunGenerationDescriptorJsonReader.Read(
                FoxRunGenerationDescriptorJsonWriter.Write(model));
            var comparison = FoxRunGenerationDescriptorComparer.Compare(model, read);

            Assert.True(
                comparison.IsSemanticEqual,
                string.Join(Environment.NewLine, comparison.SemanticDifferences));
            Assert.True(
                comparison.IsProvenanceEqual,
                string.Join(Environment.NewLine, comparison.ProvenanceDifferences));
            var member = Assert.Single(Assert.Single(read.Types).Members);
            Assert.Equal(FoxRunTypeShapeKind.Object, member.TypeShape.Kind);
            Assert.True(member.TypeShape.IsValueType);
            Assert.Equal("ACTIVE", member.TypeShape.Fields[0].TypeShape.EnumValues[1].Name);
            var variant = Assert.Single(member.EncodingVariants);
            Assert.False(variant.PublishAvailable);
            Assert.False(variant.SubscribeAvailable);
            Assert.Equal("FOXRUN619", variant.PublishUnavailableDiagnosticId);
            Assert.Equal("FOXRUN618", variant.SubscribeUnavailableDiagnosticId);
            Assert.Equal(string.Empty, variant.UnavailableDiagnosticId);
            Assert.Equal(FoxRunConditionMemberKind.Property, member.NormalizedSchedule.ConditionMemberKind);
        }

        [Fact]
        public void FrozenV4FixtureReadsWithoutInventingMessagePack()
        {
            const string fixture =
                "{\"descriptorVersion\":4,\"generatorVersion\":\"4.0.0\",\"types\":[{"
                + "\"namespace\":\"Demo\",\"className\":\"State\",\"members\":[{"
                + "\"memberName\":\"_value\",\"memberKind\":\"field\","
                + "\"rawTypeName\":\"System.Int32\",\"emissionTypeName\":\"System.Int32\","
                + "\"canonicalType\":\"int32\",\"isArray\":false,\"elementTypeName\":\"\","
                + "\"topic\":\"/phase185/legacy\",\"schemaName\":\"Demo.State\","
                + "\"encoding\":\"inherit\",\"source\":\"inherit\",\"targets\":\"inherit\","
                + "\"qosProfile\":\"inherit\",\"qosReliability\":\"inherit\","
                + "\"qosDurability\":\"inherit\",\"qosHistory\":\"inherit\",\"qosDepth\":0,"
                + "\"generatesWebSocketCodec\":true,\"generatesRos2NativeRegistration\":false,"
                + "\"ros2MessageShape\":null,\"ros2CustomDtoShape\":null,"
                + "\"hz\":10,\"policy\":\"FixedRate\",\"mode\":\"PublishAndSubscribe\","
                + "\"tolerance\":0,\"onlyIf\":\"\",\"onlyIfMemberKind\":\"None\","
                + "\"explicitArguments\":\"\",\"isAggregateMember\":false,\"isStream\":false,"
                + "\"protobufFieldNumber\":17,"
                + "\"jsonFieldName\":\"value\",\"hostKind\":\"Fixture\",\"rawMemberOrder\":0,"
                + "\"conditionalSymbols\":\"\"}]}]}";

            var model = FoxRunGenerationDescriptorJsonReader.Read(fixture);
            var member = Assert.Single(Assert.Single(model.Types).Members);

            Assert.Equal(4, model.DescriptorVersion);
            Assert.Equal("4.0.0", model.GeneratorVersion);
            Assert.Equal(
                new[] { "json", "protobuf" },
                member.EncodingVariants.Select(variant => variant.Encoding).ToArray());
            Assert.DoesNotContain(
                member.EncodingVariants,
                variant => string.Equals(variant.Encoding, "msgpack", StringComparison.Ordinal));
            Assert.Null(member.TypeShape);
            Assert.NotNull(member.ProtobufMetadata);
            Assert.Equal(17, member.ProtobufMetadata.FieldNumber);
        }

        [Theory]
        [InlineData("typeShape")]
        [InlineData("encodingVariants")]
        [InlineData("normalizedSchedule")]
        public void V5MissingRequiredMemberSemanticsFailsClosed(string propertyName)
        {
            var root = JObject.Parse(CurrentDescriptorJson());
            ((JObject)root["types"]![0]!["members"]![0]!).Property(propertyName)!.Remove();

            var error = Assert.Throws<InvalidOperationException>(
                () => FoxRunGenerationDescriptorJsonReader.Read(root.ToString()));

            Assert.Contains(propertyName, error.Message, StringComparison.Ordinal);
        }

        [Fact]
        [Trait("Phase", "185-F")]
        public void V5MissingNestedValueTypeIdentityFailsClosed()
        {
            var root = JObject.Parse(CurrentDescriptorJson());
            var shape = (JObject)root["types"]![0]!["members"]![0]!["typeShape"]!;
            shape.Property("isValueType")!.Remove();

            var error = Assert.Throws<InvalidOperationException>(
                () => FoxRunGenerationDescriptorJsonReader.Read(root.ToString()));

            Assert.Contains("isValueType", error.Message, StringComparison.Ordinal);
        }

        [Fact]
        [Trait("Phase", "185-F")]
        public void V5WrongNestedValueTypeIdentityTypeFailsClosed()
        {
            var root = JObject.Parse(CurrentDescriptorJson());
            var shape = (JObject)root["types"]![0]!["members"]![0]!["typeShape"]!;
            shape["isValueType"] = "true";

            var error = Assert.Throws<InvalidOperationException>(
                () => FoxRunGenerationDescriptorJsonReader.Read(root.ToString()));

            Assert.Contains("isValueType", error.Message, StringComparison.Ordinal);
        }

        [Fact]
        [Trait("Phase", "185-F")]
        public void V5InconsistentNestedValueTypeIdentityFailsClosed()
        {
            var root = JObject.Parse(CurrentDescriptorJson());
            var shape = (JObject)root["types"]![0]!["members"]![0]!["typeShape"]!;
            shape["isValueType"] = false;

            var error = Assert.Throws<InvalidOperationException>(
                () => FoxRunGenerationDescriptorJsonReader.Read(root.ToString()));

            Assert.Contains("isValueType", error.Message, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("publishUnavailableDiagnosticId")]
        [InlineData("publishUnavailableReason")]
        [InlineData("subscribeUnavailableDiagnosticId")]
        [InlineData("subscribeUnavailableReason")]
        public void V5MissingDirectionSpecificAvailabilityFailsClosed(string propertyName)
        {
            var root = JObject.Parse(CurrentDescriptorJson());
            var variant = (JObject)root["types"]![0]!["members"]![0]!["encodingVariants"]![0]!;
            variant.Property(propertyName)!.Remove();

            var error = Assert.Throws<InvalidOperationException>(
                () => FoxRunGenerationDescriptorJsonReader.Read(root.ToString()));

            Assert.Contains(propertyName, error.Message, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(4, "5.0.0")]
        [InlineData(5, "4.0.0")]
        [InlineData(6, "6.0.0")]
        public void CrossPairedAndFutureDescriptorVersionsFailClosed(
            int descriptorVersion,
            string generatorVersion)
        {
            var root = JObject.Parse(CurrentDescriptorJson());
            root["descriptorVersion"] = descriptorVersion;
            root["generatorVersion"] = generatorVersion;

            Assert.Throws<InvalidOperationException>(
                () => FoxRunGenerationDescriptorJsonReader.Read(root.ToString()));
        }

        [Fact]
        public void UnitProjectCompilesTheSingleRuntimeDescriptorReader()
        {
            var project = File.ReadAllText(Path.Combine(
                FindRepoRoot(),
                "Packages",
                "dev.unity2foxglove.sdk",
                "Tests",
                "Unit",
                "FoxgloveSdk.UnitTests.csproj"));

            Assert.Contains("../Runtime/FoxRunGenerationDescriptorJsonReader.cs", project.Replace('\\', '/'), StringComparison.Ordinal);
            Assert.NotNull(typeof(FoxRunGenerationModel).Assembly.GetType(
                "Unity.FoxgloveSDK.Tests.FoxRunGenerationDescriptorJsonReader"));
        }

        private static string FindRepoRoot()
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
                 directory != null;
                 directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "README.md"))
                    && Directory.Exists(Path.Combine(directory.FullName, "Packages", "dev.unity2foxglove.sdk")))
                {
                    return directory.FullName;
                }
            }

            throw new DirectoryNotFoundException("Could not locate the Unity2Foxglove repository root.");
        }

        private static string CurrentDescriptorJson()
        {
            var model = FoxRunGenerationModel.FromMembers(new[]
            {
                new FoxRunGenerationMember(
                    "Demo", "State", "_value", "field", "System.Int32",
                    true, false, "", "/phase185/state", 10f, "Demo.State",
                    (int)FoxRunPolicy.FixedRate, 0f, "UnitTest", 0, "",
                    encoding: FoxRunGenerationDescriptorConstants.MessagePackEncoding,
                    typeShape: FoxRunTypeShape.Canonical("int32"))
            });
            return FoxRunGenerationDescriptorJsonWriter.Write(model);
        }
    }
}
