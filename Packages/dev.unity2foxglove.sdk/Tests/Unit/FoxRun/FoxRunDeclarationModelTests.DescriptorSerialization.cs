// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.SourceGenerators;
using Unity.FoxgloveSDK.Util;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.FoxRun
{
    public sealed partial class FoxRunDeclarationModelTests
    {
        [Fact]
        public void DescriptorJsonIncludesExplicitFoxRunFlow()
        {
            var model = FoxRunGenerationModel.FromMembers(new[]
            {
                new FoxRunGenerationMember(
                    "Demo", "CommandInput", "_incomingVelocity", "field", "UnityEngine.Vector3",
                    true, false, "", "/phase157/cmd_vel", 10f, "",
                    1, 0f, "UnitTest", 0, "",
                    mode: (int)FoxRunFlow.Subscribe)
            });

            var json = FoxRunGenerationDescriptorJsonWriter.Write(model);

            Assert.Contains("\"mode\":\"Subscribe\"", json, StringComparison.Ordinal);
        }

        [Fact]
        public void DescriptorComparerTreatsFoxRunFlowAsSemanticState()
        {
            var publish = ModelWithMode(FoxRunFlow.Publish);
            var subscribe = ModelWithMode(FoxRunFlow.Subscribe);

            var comparison = FoxRunGenerationDescriptorComparer.Compare(publish, subscribe);

            Assert.False(comparison.IsSemanticEqual);
            Assert.Contains(
                comparison.SemanticDifferences,
                difference => difference.Contains("mode", StringComparison.Ordinal));
        }

        [Fact]
        [Trait("Phase", "185-A")]
        public void DescriptorComparerTreatsValueTypeClassificationAsSemanticState()
        {
            FoxRunGenerationModel Build(bool isValueType)
                => FoxRunGenerationModel.FromMembers(new[]
                {
                    new FoxRunGenerationMember(
                        "Demo",
                        "ValueTypeProbe",
                        "_value",
                        "field",
                        "System.Int32",
                        isValueType,
                        false,
                        string.Empty,
                        "/phase185/value-type",
                        10f,
                        "Demo.Value",
                        (int)FoxRunPolicy.FixedRate,
                        0f,
                        "UnitTest",
                        0,
                        string.Empty)
                });

            var comparison = FoxRunGenerationDescriptorComparer.Compare(
                Build(isValueType: true),
                Build(isValueType: false));

            Assert.False(comparison.IsSemanticEqual);
            Assert.Contains(
                comparison.SemanticDifferences,
                difference => difference.Contains(
                    "isValueType",
                    StringComparison.Ordinal));
        }

        [Fact]
        [Trait("Phase", "184-E")]
        public void DescriptorWriterAndComparerPreserveStreamSemantics()
        {
            var streamMember = new FoxRunGenerationMember(
                "Demo", "StreamInput", "_samples", "field", "System.Int32",
                true, false, "", "/phase184/stream", 0f, "",
                1, 0f, "UnitTest", 0, "",
                mode: (int)FoxRunFlow.Subscribe,
                isStream: true);
            var stream = FoxRunGenerationModel.FromMembers(new[] { streamMember });
            var ordinary = FoxRunGenerationModel.FromMembers(new[]
            {
                new FoxRunGenerationMember(
                    "Demo", "StreamInput", "_samples", "field", "System.Int32",
                    true, false, "", "/phase184/stream", 0f, "",
                    1, 0f, "UnitTest", 0, "",
                    mode: (int)FoxRunFlow.Subscribe)
            });

            var json = FoxRunGenerationDescriptorJsonWriter.Write(stream);
            var comparison = FoxRunGenerationDescriptorComparer.Compare(stream, ordinary);
            using var descriptor = JsonDocument.Parse(json);
            var serializedMember = descriptor.RootElement
                .GetProperty("types")[0]
                .GetProperty("members")[0];

            Assert.Contains("\"isStream\":true", json, StringComparison.Ordinal);
            Assert.True(serializedMember.GetProperty("isStream").GetBoolean());
            Assert.False(comparison.IsSemanticEqual);
            Assert.Contains(
                comparison.SemanticDifferences,
                difference => difference.Contains("isStream", StringComparison.Ordinal));
        }



        [Fact]
        public void DescriptorComparerTreatsMatchingNanFloatsAsSameValue()
        {
            var compare = typeof(FoxRunGenerationDescriptorComparer).GetMethod(
                "CompareSemantic",
                BindingFlags.NonPublic | BindingFlags.Static,
                null,
                new[] { typeof(string), typeof(string), typeof(float), typeof(float), typeof(List<string>) },
                null);
            Assert.NotNull(compare);
            var diffs = new List<string>();

            compare.Invoke(null, new object[] { "member", "rateHz", float.NaN, float.NaN, diffs });

            Assert.Empty(diffs);
        }

        [Fact]
        public void FoxRunJsonSchemaBuilderAcceptsDecimalFieldsAsNumbers()
        {
            var contract = new FoxRunSchemaContractInfo(
                "Demo.DecimalState",
                "/phase173/decimal",
                "",
                "json",
                "contract",
                "binding",
                "policy",
                "FixedRate",
                10f,
                0f,
                new[]
                {
                    new FoxRunSchemaFieldInfo("amount", "_amount", "field", "decimal", false, false)
                });

            var json = FoxRunJsonSchemaBuilder.Build(contract);

            Assert.Contains("\"amount\":{\"anyOf\":[{\"type\":\"number\"},{\"type\":\"null\"}]}", json, StringComparison.Ordinal);
        }

        [Fact]
        public void ManifestRecordsInboundFlowWithoutChangingDefaultCanonicalShape()
        {
            var publish = FoxRunManifestBuilder.Build(new[]
            {
                ManifestMember(FoxRunFlow.Publish)
            }, manifestVersion: FoxrunManifestWriter.CurrentManifestVersion);
            var subscribe = FoxRunManifestBuilder.Build(new[]
            {
                ManifestMember(FoxRunFlow.Subscribe)
            }, manifestVersion: FoxrunManifestWriter.CurrentManifestVersion);
            var publishAndSubscribe = FoxRunManifestBuilder.Build(new[]
            {
                ManifestMember(FoxRunFlow.PublishAndSubscribe)
            }, manifestVersion: FoxrunManifestWriter.CurrentManifestVersion);

            var publishJson = FoxRunManifestJsonWriter.WriteCanonical(publish);
            var subscribeJson = FoxRunManifestJsonWriter.WriteCanonical(subscribe);
            var publishAndSubscribeJson = FoxRunManifestJsonWriter.WriteCanonical(publishAndSubscribe);

            Assert.DoesNotContain("\"flow\"", publishJson, StringComparison.Ordinal);
            Assert.Contains("\"flow\":\"Subscribe\"", subscribeJson, StringComparison.Ordinal);
            Assert.Contains("\"flow\":\"PublishAndSubscribe\"", publishAndSubscribeJson, StringComparison.Ordinal);
            Assert.NotEqual(
                publish.Sections.FoxRun.Types[0].Contracts[0].ContractHash,
                subscribe.Sections.FoxRun.Types[0].Contracts[0].ContractHash);
            Assert.NotEqual(
                publish.Sections.FoxRun.Types[0].Contracts[0].ContractHash,
                publishAndSubscribe.Sections.FoxRun.Types[0].Contracts[0].ContractHash);
        }

        [Fact]
        public void ManifestExpandsInheritedWirePolicyIntoJsonAndProtobufContracts()
        {
            var manifest = FoxRunManifestBuilder.Build(new[]
            {
                new FoxRunManifestMember(
                    "Demo",
                    "WireState",
                    "_count",
                    "field",
                    "System.Int32",
                    true,
                    false,
                    "",
                    "/phase175/wire_state",
                    10f,
                    "Demo.WireState",
                    1,
                    0f,
                    encoding: (int)(FoxRunEncoding)0,
                    protobufFieldNumber: 17)
            });

            var contracts = manifest.Sections.FoxRun.Types.Single().Contracts;

            Assert.Equal(
                new[] { "json", "msgpack", "protobuf" },
                contracts.Select(contract => contract.Encoding).OrderBy(encoding => encoding));
            Assert.Null(contracts.Single(contract => contract.Encoding == "json").Fields.Single().ProtobufMetadata);
            Assert.Null(contracts.Single(contract => contract.Encoding == "msgpack").Fields.Single().ProtobufMetadata);
            Assert.Equal(
                17,
                contracts.Single(contract => contract.Encoding == "protobuf")
                    .Fields.Single()
                    .ProtobufMetadata.FieldNumber);
        }

        [Fact]
        public void ManifestRejectsUnknownPolicy()
        {
            var member = new FoxRunManifestMember(
                "Demo",
                "CommandInput",
                "_incomingVelocity",
                "field",
                "UnityEngine.Vector3",
                true,
                false,
                "",
                "/phase157/cmd_vel",
                10f,
                "",
                99,
                0f);

            var ex = Assert.Throws<InvalidOperationException>(() => FoxRunManifestBuilder.Build(new[] { member }));

            Assert.Contains("Policy", ex.Message, StringComparison.Ordinal);
            Assert.Contains("FixedRate, Change, or Trigger", ex.Message, StringComparison.Ordinal);
        }

    }
}
