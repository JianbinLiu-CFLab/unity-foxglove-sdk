// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.SourceGenerators;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.FoxRun
{

    [Collection("FoxRunSchemaRegistry")]
    public sealed partial class FoxRunAggregationEmitterTests
    {
        [Fact]
        [Trait("Phase", "185-B")]
        public void AggregateMessagePackEmitsOneDirectStableMapBuilderWithoutJsonFallback()
        {
            var type = new FoxRunGenerationType(
                "Demo",
                "MessagePackTelemetry",
                new[]
                {
                    new FoxRunGenerationMember(
                        "Demo", "MessagePackTelemetry", "_zeta", "field", "System.Double",
                        true, false, "", "/phase185/aggregate", 10f, "Demo.MessagePackTelemetry",
                        0, 0f, "UnitTest", 1, "",
                        isAggregateMember: true,
                        jsonFieldName: "zeta",
                        encoding: FoxRunGenerationDescriptorConstants.MessagePackEncoding,
                        typeShape: FoxRunTypeShape.Canonical("float64")),
                    new FoxRunGenerationMember(
                        "Demo", "MessagePackTelemetry", "_alpha", "field", "System.Int32",
                        true, false, "", "/phase185/aggregate", 10f, "Demo.MessagePackTelemetry",
                        0, 0f, "UnitTest", 0, "",
                        isAggregateMember: true,
                        jsonFieldName: "alpha",
                        encoding: FoxRunGenerationDescriptorConstants.MessagePackEncoding,
                        typeShape: FoxRunTypeShape.Canonical("int32"))
                });

            var source = FoxgloveSourceEmitter.EmitClass(type);

            Assert.Contains("__BuildFoxRunMessagePack_0", source, StringComparison.Ordinal);
            Assert.Contains("FoxgloveMsgPackWriter", source, StringComparison.Ordinal);
            Assert.True(
                source.IndexOf("WriteString(\"alpha\")", StringComparison.Ordinal)
                < source.IndexOf("WriteString(\"zeta\")", StringComparison.Ordinal));
            Assert.DoesNotContain("__BuildFoxRunJson_0", source, StringComparison.Ordinal);
            Assert.DoesNotContain("JsonConvert", source, StringComparison.Ordinal);
        }

        [Fact]
        public void AggregateMemberEmitsExplicitJsonBytesWithoutDictionaryPayload()
        {
            var type = new FoxRunGenerationType(
                "Demo",
                "VehicleTelemetry",
                new[]
                {
                    new FoxRunGenerationMember(
                        "Demo",
                        "VehicleTelemetry",
                        "_speed",
                        "field",
                        "System.Single",
                        true,
                        false,
                        "",
                        "/phase154/vehicle",
                        10f,
                        "Demo.VehicleTelemetry",
                        0,
                        0f,
                        "UnitTest",
                        0,
                        "",
                        isAggregateMember: true,
                        jsonFieldName: "speed"),
                    new FoxRunGenerationMember(
                        "Demo",
                        "VehicleTelemetry",
                        "_enabled",
                        "field",
                        "System.Boolean",
                        true,
                        false,
                        "",
                        "/phase154/vehicle",
                        10f,
                        "Demo.VehicleTelemetry",
                        0,
                        0f,
                        "UnitTest",
                        1,
                        "",
                        isAggregateMember: true,
                        jsonFieldName: "enabled")
                });

            var source = FoxgloveSourceEmitter.EmitClass(type);

            Assert.Contains("PublishFoxRunJsonBytes", source, StringComparison.Ordinal);
            Assert.Contains("mgr.PublishFoxRunJsonBytes(\"/phase154/vehicle\", \"Demo.VehicleTelemetry\"", source, StringComparison.Ordinal);
            Assert.Contains("__WriteFoxRunJson_0", source, StringComparison.Ordinal);
            Assert.Contains("\\\"speed\\\"", source, StringComparison.Ordinal);
            Assert.Contains("\\\"enabled\\\"", source, StringComparison.Ordinal);
            Assert.DoesNotContain("mgr.PublishJson(\"/phase154/vehicle\"", source, StringComparison.Ordinal);
            Assert.DoesNotContain("new Dictionary<string, object>", source, StringComparison.Ordinal);
        }

        [Fact]
        public void AggregateMemberEmitsSinkFanoutSideChannelReusingExplicitJsonBytes()
        {
            var type = new FoxRunGenerationType(
                "Demo",
                "VehicleTelemetry",
                new[]
                {
                    new FoxRunGenerationMember(
                        "Demo", "VehicleTelemetry", "_speed", "field", "System.Single",
                        true, false, "", "/phase155/vehicle", 10f, "Demo.VehicleTelemetry",
                        0, 0f, "UnitTest", 0, "",
                        isAggregateMember: true, jsonFieldName: "speed")
                });

            var source = FoxgloveSourceEmitter.EmitClass(type);

            Assert.Contains("IFoxgloveTopicSinkSource", source, StringComparison.Ordinal);
            Assert.Contains("void IFoxgloveTopicSinkSource.FoxgloveLog_PublishToSinks(int topicIndex, FoxTopicSinkRouter router, ulong nowNs)", source, StringComparison.Ordinal);
            Assert.Contains("if (router == null || !router.HasSinks)", source, StringComparison.Ordinal);
            Assert.Contains("private byte[] __foxRunLastJson_0;", source, StringComparison.Ordinal);
            Assert.Contains("__foxRunLastJson_0 = __payload_0;", source, StringComparison.Ordinal);
            Assert.Contains("var __sink_0 = __foxRunLastJson_0 ?? __BuildFoxRunJson_0();", source, StringComparison.Ordinal);
            Assert.Contains("__foxRunLastJson_0 = null;", source, StringComparison.Ordinal);
            Assert.Contains("router.PublishCompatible(((IFoxgloveTopicContractSource)this).FoxgloveLog_GetContract(0), FoxRunEncoding.JSON, nowNs, __sink_0,", source, StringComparison.Ordinal);
        }

        [Fact]
        public void AggregateMemberEmitsBusSideChannelReusingExplicitJsonBytes()
        {
            var type = new FoxRunGenerationType(
                "Demo",
                "VehicleTelemetry",
                new[]
                {
                    new FoxRunGenerationMember(
                        "Demo", "VehicleTelemetry", "_speed", "field", "System.Single",
                        true, false, "", "/phase173/bus", 10f, "Demo.VehicleTelemetry",
                        0, 0f, "UnitTest", 0, "",
                        isAggregateMember: true, jsonFieldName: "speed")
                });

            var source = FoxgloveSourceEmitter.EmitClass(type);

            Assert.Contains("var __payload_0 = __foxRunLastJson_0 ?? __BuildFoxRunJson_0();", source, StringComparison.Ordinal);
            Assert.Contains("bus.Publish(((IFoxgloveTopicContractSource)this).FoxgloveLog_GetContract(0), nowNs, in __payload_0,", source, StringComparison.Ordinal);
        }

        [Fact]
        public void LegacySingleFieldTopicEmitsSinkFanoutSideChannel()
        {
            var type = new FoxRunGenerationType(
                "Demo",
                "ScalarTelemetry",
                new[]
                {
                    new FoxRunGenerationMember(
                        "Demo", "ScalarTelemetry", "_status", "field", "System.String",
                        true, false, "", "/phase155/status", 10f, "foxglove.Log",
                        0, 0f, "UnitTest", 0, "",
                        isAggregateMember: false, jsonFieldName: "message")
                });

            var source = FoxgloveSourceEmitter.EmitClass(type);

            Assert.Contains("mgr.PublishJson(\"/phase155/status\", \"foxglove.Log\"", source, StringComparison.Ordinal);
            Assert.Contains("void IFoxgloveTopicSinkSource.FoxgloveLog_PublishToSinks(int topicIndex, FoxTopicSinkRouter router, ulong nowNs)", source, StringComparison.Ordinal);
            Assert.Contains("var __sink_0 = __BuildFoxRunJson_0();", source, StringComparison.Ordinal);
            Assert.Contains("router.PublishCompatible(((IFoxgloveTopicContractSource)this).FoxgloveLog_GetContract(0), FoxRunEncoding.JSON, nowNs, __sink_0,", source, StringComparison.Ordinal);
            Assert.Contains("\\\"message\\\"", source, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(FoxRunGenerationDescriptorConstants.JsonEncoding)]
        [InlineData(FoxRunGenerationDescriptorConstants.ProtobufEncoding)]
        [InlineData(FoxRunGenerationDescriptorConstants.InheritEncoding)]
        [Trait("Phase", "185-F")]
        public void NonMessagePackSinkSideChannelAlwaysExcludesTargetSinks(
            string encoding)
        {
            var type = new FoxRunGenerationType(
                "Demo",
                "CompatibleSinkTelemetry",
                new[]
                {
                    new FoxRunGenerationMember(
                        "Demo",
                        "CompatibleSinkTelemetry",
                        "_value",
                        "field",
                        "System.Int32",
                        true,
                        false,
                        "",
                        "/phase185f/compatible-sink",
                        10f,
                        "Demo.CompatibleSink",
                        0,
                        0f,
                        "UnitTest",
                        0,
                        "",
                        jsonFieldName: "value",
                        encoding: encoding,
                        typeShape:
                            FoxRunTypeShape.Canonical("int32"))
                });

            var source = FoxgloveSourceEmitter.EmitClass(type);

            Assert.Contains(
                "router.PublishCompatible(((IFoxgloveTopicContractSource)this).FoxgloveLog_GetContract(0), FoxRunEncoding.JSON",
                source,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "router.Publish(((IFoxgloveTopicContractSource)this).FoxgloveLog_GetContract(0)",
                source,
                StringComparison.Ordinal);
        }

        [Fact]
        public void LegacyArrayFieldTopicEmitsJsonArrayForSinkFanout()
        {
            var type = new FoxRunGenerationType(
                "Demo",
                "ArrayTelemetry",
                new[]
                {
                    new FoxRunGenerationMember(
                        "Demo", "ArrayTelemetry", "_samples", "field", "System.Single[]",
                        true, false, "", "/phase155/array", 10f, "",
                        0, 0f, "UnitTest", 0, "",
                        isAggregateMember: false,
                        jsonFieldName: "samples",
                        encoding: FoxRunGenerationDescriptorConstants.JsonEncoding)
                });

            var source = FoxgloveSourceEmitter.EmitClass(type);

            Assert.Contains("mgr.PublishJson(\"/phase155/array\", \"\"", source, StringComparison.Ordinal);
            Assert.Contains("var __sink_0 = __BuildFoxRunJson_0();", source, StringComparison.Ordinal);
            Assert.DoesNotContain("_samples == null ? null : _samples.ToString()", source, StringComparison.Ordinal);
            Assert.Contains("__json.Append('[');", source, StringComparison.Ordinal);
        }

        [Fact]
        public void LegacyStringSinkFanoutEscapesSurrogates()
        {
            var type = new FoxRunGenerationType(
                "Demo",
                "StringTelemetry",
                new[]
                {
                    new FoxRunGenerationMember(
                        "Demo", "StringTelemetry", "_text", "field", "System.String",
                        true, false, "", "/phase173/string", 10f, "",
                        0, 0f, "UnitTest", 0, "",
                        isAggregateMember: false, jsonFieldName: "text")
                });

            var source = FoxgloveSourceEmitter.EmitClass(type);

            Assert.Contains("global::System.Char.IsSurrogate(__c)", source, StringComparison.Ordinal);
        }

        [Fact]
        public void LegacyUnityVector4AndColor32EmitStructuredJsonForSinkFanout()
        {
            var type = new FoxRunGenerationType(
                "Demo",
                "UnityTelemetry",
                new[]
                {
                    new FoxRunGenerationMember(
                        "Demo", "UnityTelemetry", "_vector", "field", "UnityEngine.Vector4",
                        true, false, "", "/phase173/unity", 10f, "",
                        0, 0f, "UnitTest", 0, "",
                        isAggregateMember: false,
                        jsonFieldName: "vector",
                        encoding: FoxRunGenerationDescriptorConstants.JsonEncoding),
                    new FoxRunGenerationMember(
                        "Demo", "UnityTelemetry", "_color", "field", "UnityEngine.Color32",
                        true, false, "", "/phase173/unity", 10f, "",
                        0, 0f, "UnitTest", 1, "",
                        isAggregateMember: false,
                        jsonFieldName: "color",
                        encoding: FoxRunGenerationDescriptorConstants.JsonEncoding)
                });

            var source = FoxgloveSourceEmitter.EmitClass(type);

            Assert.Contains("__foxRunCapture_0_1.w", source, StringComparison.Ordinal);
            Assert.Contains("((float)__foxRunCapture_0_0.r / 255f)", source, StringComparison.Ordinal);
        }

        [Fact]
        public void ArrayFieldContractFingerprintUsesElementCanonicalType()
        {
            var type = new FoxRunGenerationType(
                "Demo",
                "ArrayTelemetry",
                new[]
                {
                    new FoxRunGenerationMember(
                        "Demo", "ArrayTelemetry", "_samples", "field", "System.Single[]",
                        true, true, "System.Single", "/phase155/array", 10f, "",
                        0, 0f, "UnitTest", 0, "",
                        isAggregateMember: false, jsonFieldName: "samples")
                });

            var source = FoxgloveSourceEmitter.EmitClass(type);

            Assert.Contains(
                "|field|json=7#samples",
                source,
                StringComparison.Ordinal);
            Assert.Contains(
                "|canonical=7#float32",
                source,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "|canonical=7#float[]",
                source,
                StringComparison.Ordinal);
        }

        [Fact]
        [Trait("Phase", "185-F")]
        public void ContractFingerprintTracksCompleteShapeButNotDiscoveryOrder()
        {
            var baselineEnum = FoxRunTypeShape.Enum(
                "Demo.Mode",
                new[]
                {
                    new FoxRunEnumValue("Idle", 0),
                    new FoxRunEnumValue("Active", 1)
                });
            var changedEnum = FoxRunTypeShape.Enum(
                "Demo.Mode",
                new[]
                {
                    new FoxRunEnumValue("Idle", 0),
                    new FoxRunEnumValue("Active", 2)
                });
            var value = new FoxRunTypeField(
                "value",
                "Value",
                FoxRunTypeShape.Canonical("int32"));
            var optional = new FoxRunTypeField(
                "optional",
                "Optional",
                FoxRunTypeShape.Canonical("int32"));
            var nullableOptional = new FoxRunTypeField(
                "optional",
                "Optional",
                FoxRunTypeShape.Canonical("int32", nullable: true),
                isNullable: true);
            var mode = new FoxRunTypeField("mode", "Mode", baselineEnum);
            var changedMode = new FoxRunTypeField(
                "mode",
                "Mode",
                changedEnum);
            var baseline = FoxRunTypeShape.Object(
                "Demo.Envelope",
                new[] { value, optional, mode });
            var reversedDiscovery = FoxRunTypeShape.Object(
                "Demo.Envelope",
                new[] { mode, optional, value });
            var nullableChanged = FoxRunTypeShape.Object(
                "Demo.Envelope",
                new[] { value, nullableOptional, mode });
            var enumChanged = FoxRunTypeShape.Object(
                "Demo.Envelope",
                new[] { value, optional, changedMode });

            string Fingerprint(FoxRunTypeShape shape)
            {
                var member = new FoxgloveSourceEmitter.TopicMember(
                    "_value",
                    "Demo.Envelope",
                    "/phase185f/contract",
                    10f,
                    "Demo.Envelope",
                    FoxRunGenerationDescriptorConstants.MessagePackEncoding,
                    typeShape: shape);
                return TopicMetadataEmitter.Sha256Hex(
                    TopicMetadataEmitter.CanonicalTopicShape(
                        member.Topic,
                        member.SchemaName,
                        FoxRunGenerationDescriptorConstants.MessagePackEncoding,
                        new[] { member }));
            }

            Assert.Equal(
                Fingerprint(baseline),
                Fingerprint(reversedDiscovery));
            Assert.NotEqual(
                Fingerprint(baseline),
                Fingerprint(nullableChanged));
            Assert.NotEqual(
                Fingerprint(baseline),
                Fingerprint(enumChanged));

            var alpha = new FoxgloveSourceEmitter.TopicMember(
                "_alpha",
                "System.Int32",
                "/phase185f/top-level-order",
                10f,
                "Demo.TopLevel",
                policy: 1,
                tolerance: 0f,
                jsonFieldName: "alpha",
                canonicalType: "int32",
                encoding: FoxRunGenerationDescriptorConstants.MessagePackEncoding,
                typeShape: FoxRunTypeShape.Canonical("int32"));
            var zeta = new FoxgloveSourceEmitter.TopicMember(
                "_zeta",
                "System.String",
                "/phase185f/top-level-order",
                10f,
                "Demo.TopLevel",
                policy: 1,
                tolerance: 0f,
                jsonFieldName: "zeta",
                canonicalType: "string",
                encoding: FoxRunGenerationDescriptorConstants.MessagePackEncoding,
                typeShape: FoxRunTypeShape.Canonical("string"));

            var ordered = TopicMetadataEmitter.CanonicalTopicShape(
                alpha.Topic,
                alpha.SchemaName,
                alpha.Encoding,
                new[] { alpha, zeta });
            var reversed = TopicMetadataEmitter.CanonicalTopicShape(
                alpha.Topic,
                alpha.SchemaName,
                alpha.Encoding,
                new[] { zeta, alpha });

            Assert.Equal(ordered, reversed);
        }
    }
}
