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
        [Trait("Phase", "187")]
        public void SchemaInfoWriterRejectsCorruptedSubscriptionBindingCount()
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
                        "/phase187/input",
                        10f,
                        string.Empty,
                        (int)FoxRunPolicy.FixedRate,
                        0f,
                        flow: (int)FoxRunFlow.Subscribe,
                        generatesWebSocketCodec: false,
                        subscribeTransportId: "unity2foxglove.r2fu")
                },
                manifestVersion: FoxrunManifestWriter.CurrentManifestVersion);
            var generated = FoxRunSchemaInfoWriter.GenerateSource(manifest);
            var corrupted = generated.Replace(
                "public const int SubscriptionBindingCount = 1;",
                "public const int SubscriptionBindingCount = 0;",
                StringComparison.Ordinal);
            Assert.NotEqual(generated, corrupted);

            var verification = FoxRunSchemaInfoWriter.VerifyGeneratedInfo(
                manifest,
                corrupted);

            Assert.False(verification.IsValid);
            Assert.Contains("SubscriptionBindingCount mismatch.", verification.Errors);
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

    }
}
