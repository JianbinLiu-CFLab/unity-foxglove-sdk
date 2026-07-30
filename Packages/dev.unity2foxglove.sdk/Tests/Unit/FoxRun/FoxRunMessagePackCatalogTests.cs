// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/FoxRun
// Purpose: Phase185-D direction-specific MessagePack catalog coverage.

using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.FoxRun
{
    public sealed class FoxRunMessagePackCatalogTests
    {
        [Fact]
        [Trait("Phase", "185-D")]
        public void InspectorSummariesSplitDirectionsWhileCatalogReturnsOnlyEffectiveInput()
        {
            var manifest = Manifest();
            FoxRunSchemaInfoRegistry.ClearForTests();
            try
            {
                FoxRunSchemaInfoRegistry.RegisterGenerated(manifest);
                var summaries = FoxRunSchemaInfoRegistry.GetTopicSummaries(
                    FoxRunEncoding.MessagePack,
                    FoxRunEncoding.Protobuf);

                Assert.Equal(2, summaries.Count);
                var publish = Assert.Single(
                    summaries,
                    summary => summary.Direction == "Publish");
                var subscribe = Assert.Single(
                    summaries,
                    summary => summary.Direction == "Subscribe");
                Assert.Equal(FoxRunEncoding.MessagePack, publish.EffectiveEncoding);
                Assert.Equal(string.Empty, publish.WireSchemaName);
                Assert.Equal(FoxRunEncoding.Protobuf, subscribe.EffectiveEncoding);
                Assert.NotEqual(string.Empty, subscribe.WireSchemaName);

                var response = FoxRunSubscriptionCatalog.BuildResponse(
                    manifest,
                    subscriptionsEnabled: true,
                    FoxRunEncoding.MessagePack,
                    FoxRunEncoding.Protobuf,
                    subscriptionRateLimitHz: 60,
                    requestedTopic: "/phase185/duplex-catalog",
                    includeDescriptor: true);
                var input = Assert.Single((JArray)response["contracts"]);

                Assert.Equal("Subscribe", input.Value<string>("flow"));
                Assert.Equal("protobuf", input.Value<string>("encoding"));
                Assert.Equal("Demo.DuplexInput", input.Value<string>("logicalSchemaName"));
                Assert.NotEqual(string.Empty, input.Value<string>("schemaName"));
                Assert.True(input.Value<bool>("protobufDescriptorAvailable"));
                Assert.NotNull(input["protobufDescriptorBase64"]);
            }
            finally
            {
                FoxRunSchemaInfoRegistry.ClearForTests();
            }
        }

        [Fact]
        [Trait("Phase", "185-D")]
        public void MessagePackInputCatalogIsSchemalessAndNeverSubstitutesOutputMetadata()
        {
            var response = FoxRunSubscriptionCatalog.BuildResponse(
                Manifest(),
                subscriptionsEnabled: true,
                FoxRunEncoding.Protobuf,
                FoxRunEncoding.MessagePack,
                subscriptionRateLimitHz: 60,
                requestedTopic: "/phase185/duplex-catalog",
                includeDescriptor: true);
            var input = Assert.Single((JArray)response["contracts"]);

            Assert.Equal("Subscribe", input.Value<string>("flow"));
            Assert.Equal("msgpack", input.Value<string>("encoding"));
            Assert.Equal(string.Empty, input.Value<string>("schemaName"));
            Assert.Equal(string.Empty, input.Value<string>("wireSchemaName"));
            Assert.Equal("Demo.DuplexInput", input.Value<string>("logicalSchemaName"));
            Assert.False(input.Value<bool>("protobufDescriptorAvailable"));
            Assert.Null(input["protobufDescriptorBase64"]);
            Assert.Equal(
                new[] { "command" },
                ((JArray)input["fields"])
                    .Select(field => field.Value<string>("name"))
                    .ToArray());
        }

        private static FoxRunSchemaManifestInfo Manifest()
        {
            var outputFields = new[]
            {
                new FoxRunSchemaFieldInfo(
                    "state",
                    "_state",
                    "field",
                    "int32",
                    nullable: false,
                    array: false,
                    typeShape: Canonical("int32"))
            };
            var inputFields = new[]
            {
                new FoxRunSchemaFieldInfo(
                    "command",
                    "_command",
                    "field",
                    "int32",
                    nullable: false,
                    array: false,
                    typeShape: Canonical("int32"))
            };
            var contracts = new[]
            {
                Contract("Publish", "json", "Demo.DuplexOutput", outputFields),
                Contract("Publish", "protobuf", "Demo.DuplexOutput", outputFields),
                Contract("Publish", "msgpack", "Demo.DuplexOutput", outputFields),
                Contract("Subscribe", "json", "Demo.DuplexInput", inputFields),
                Contract("Subscribe", "protobuf", "Demo.DuplexInput", inputFields),
                Contract("Subscribe", "msgpack", "Demo.DuplexInput", inputFields)
            };
            return new FoxRunSchemaManifestInfo(
                manifestVersion: 5,
                packageName: "Unity2Foxglove",
                generatorName: "FoxRun",
                generatorMajorVersion: 5,
                globalManifestHash: "phase185-d",
                foxRunManifestHash: "phase185-d",
                types: new[]
                {
                    new FoxRunSchemaTypeInfo("Demo.DuplexInput", contracts)
                },
                subscriptionManifestHash: "phase185-d-input",
                subscriptionBindings: new[]
                {
                    new FoxRunSchemaSubscriptionBindingInfo(
                        "Demo.DuplexInput",
                        "_command",
                        "/phase185/duplex-catalog",
                        "PublishAndSubscribe",
                        publishTransportIds: new[]
                        {
                            FoxgloveWebSocketTransport.Id
                        },
                        subscribeTransportId:
                            FoxgloveWebSocketTransport.Id,
                        reliability: "inherit",
                        durability: "inherit",
                        history: "inherit",
                        depth: 0,
                        supportsWebSocket: true)
                });
        }

        private static FoxRunSchemaContractInfo Contract(
            string flow,
            string encoding,
            string logicalSchemaName,
            FoxRunSchemaFieldInfo[] fields)
        {
            var wireSchemaName = encoding == "msgpack"
                ? string.Empty
                : logicalSchemaName + "." + encoding;
            return new FoxRunSchemaContractInfo(
                "Demo.DuplexInput",
                "/phase185/duplex-catalog",
                wireSchemaName,
                encoding,
                contractHash: encoding + "-" + flow,
                bindingHash: flow,
                policyHash: "policy",
                mode: "Change",
                hz: 10f,
                tolerance: 0f,
                fields,
                flow,
                protobufDescriptorSet: encoding == "protobuf"
                    ? new byte[] { 0x0a, 0x00 }
                    : Array.Empty<byte>(),
                logicalSchemaName);
        }

        private static FoxRunTypeShapeInfo Canonical(string canonicalType)
            => new FoxRunTypeShapeInfo(
                FoxRunTypeShapeInfoKind.Canonical,
                typeName: string.Empty,
                canonicalType,
                nullable: false,
                FoxRunCollectionInfoKind.None,
                elementShape: null,
                fields: Array.Empty<FoxRunTypeFieldInfo>(),
                enumValues: Array.Empty<FoxRunEnumValueInfo>());
    }
}
