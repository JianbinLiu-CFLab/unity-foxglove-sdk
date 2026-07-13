// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/FoxRun
// Purpose: Locks the safe, directional subscription-contract catalog for the FoxRun Publish panel.

using System;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.FoxRun
{
    public sealed class FoxRunSubscriptionCatalogTests
    {
        [Fact]
        public void DisabledCatalogReturnsVersionAndNoContracts()
        {
            var response = FoxRunSubscriptionCatalog.BuildResponse(
                CreateManifest(),
                subscriptionsEnabled: false,
                FoxRunWireEncoding.Protobuf,
                FoxRunWireEncoding.Json,
                subscriptionRateLimitHz: 0,
                requestedTopic: null,
                includeDescriptor: false);

            Assert.Equal(FoxRunSubscriptionCatalog.Version, response.Value<int>("version"));
            Assert.False(response.Value<bool>("subscriptionsEnabled"));
            Assert.Equal(1, response.Value<int>("subscriptionRateLimitHz"));
            Assert.Empty((JArray)response["contracts"]);
            Assert.Null(response["token"]);
            Assert.Null(response["clientId"]);
            Assert.Null(response["queueState"]);
            Assert.Null(response["maxPayloadBytes"]);
        }

        [Fact]
        public void CatalogUsesSubscriptionDefaultAndReturnsDescriptorOnlyOnDemand()
        {
            var response = FoxRunSubscriptionCatalog.BuildResponse(
                CreateManifest(),
                subscriptionsEnabled: true,
                FoxRunWireEncoding.Protobuf,
                FoxRunWireEncoding.Json,
                subscriptionRateLimitHz: 12,
                requestedTopic: null,
                includeDescriptor: false);

            var contracts = (JArray)response["contracts"];
            var contract = Assert.Single(contracts);
            Assert.Equal("/phase176/input", contract.Value<string>("topic"));
            Assert.Equal("SubscribeOnly", contract.Value<string>("flowMode"));
            Assert.Equal("json", contract.Value<string>("encoding"));
            Assert.Equal("json-input", contract.Value<string>("schemaName"));
            Assert.Equal(1, contract.Value<int>("writableFieldCount"));
            Assert.Null(contract["fields"]);
            Assert.Null(contract["protobufDescriptorBase64"]);

            var descriptorResponse = FoxRunSubscriptionCatalog.BuildResponse(
                CreateManifest(),
                subscriptionsEnabled: true,
                FoxRunWireEncoding.Protobuf,
                FoxRunWireEncoding.Protobuf,
                subscriptionRateLimitHz: 12,
                requestedTopic: "/phase176/input",
                includeDescriptor: true);

            var protobufContract = Assert.Single((JArray)descriptorResponse["contracts"]);
            Assert.Equal("protobuf", protobufContract.Value<string>("encoding"));
            Assert.Single((JArray)protobufContract["fields"]);
            Assert.Equal(Convert.ToBase64String(new byte[] { 4, 5, 6 }), protobufContract.Value<string>("protobufDescriptorBase64"));
        }

        [Fact]
        public void SummaryOmitsEveryDescriptorUntilOneTopicDetailIsRequested()
        {
            var manifest = CreateProtobufManifest(contractCount: 12, descriptorBytes: 128);
            var summary = FoxRunSubscriptionCatalog.BuildResponse(
                manifest,
                subscriptionsEnabled: true,
                FoxRunWireEncoding.Protobuf,
                FoxRunWireEncoding.Protobuf,
                subscriptionRateLimitHz: 20,
                requestedTopic: null,
                includeDescriptor: false);

            var summaryContracts = (JArray)summary["contracts"];
            Assert.Equal(12, summaryContracts.Count);
            Assert.All(summaryContracts, item =>
            {
                Assert.Null(item["fields"]);
                Assert.Null(item["protobufDescriptorBase64"]);
                Assert.False(string.IsNullOrWhiteSpace(item.Value<string>("protobufDescriptorDigest")));
            });

            var detail = FoxRunSubscriptionCatalog.BuildResponse(
                manifest,
                subscriptionsEnabled: true,
                FoxRunWireEncoding.Protobuf,
                FoxRunWireEncoding.Protobuf,
                subscriptionRateLimitHz: 20,
                requestedTopic: "/phase176/input/05",
                includeDescriptor: true);

            var detailContract = Assert.Single((JArray)detail["contracts"]);
            Assert.Single((JArray)detailContract["fields"]);
            Assert.Equal(Convert.ToBase64String(new byte[128]), detailContract.Value<string>("protobufDescriptorBase64"));
            Assert.True(detail.ToString().Length < summary.ToString().Length);
        }

        [Fact]
        public void ServiceResponseSchemaDefinesTypedArrayItemsForFoxgloveFormParsing()
        {
            var type = typeof(FoxRunSubscriptionCatalog).Assembly.GetType(
                "Unity.FoxgloveSDK.Components.FoxRunSubscriptionCatalogServiceSchemas");
            Assert.NotNull(type);
            var responseField = type!.GetField("Response", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(responseField);

            var response = JObject.Parse((string)responseField!.GetValue(null)!);
            var contracts = (JObject)response["properties"]!["contracts"]!;
            Assert.Equal("array", contracts.Value<string>("type"));
            Assert.Equal("object", contracts["items"]!.Value<string>("type"));

            var fields = (JObject)contracts["items"]!["properties"]!["fields"]!;
            Assert.Equal("array", fields.Value<string>("type"));
            Assert.Equal("object", fields["items"]!.Value<string>("type"));
        }

        private static FoxRunSchemaManifestInfo CreateManifest()
        {
            var fields = new[]
            {
                new FoxRunSchemaFieldInfo("requested", "_requested", "field", "float32", false, false, false, 1)
            };
            var contracts = new[]
            {
                new FoxRunSchemaContractInfo("Demo.Output", "/phase176/output", string.Empty, "json", "json-output", "json-output", "policy", "FixedRate", 10f, 0f, 0f, fields, flowMode: "PublishOnly"),
                new FoxRunSchemaContractInfo("Demo.Input", "/phase176/input", "json-input", "json", "json-input", "json-input", "policy", "FixedRate", 10f, 0f, 0f, fields, flowMode: "SubscribeOnly"),
                new FoxRunSchemaContractInfo("Demo.Input", "/phase176/input", "protobuf-input", "protobuf", "protobuf-input", "protobuf-input", "policy", "FixedRate", 10f, 0f, 0f, fields, flowMode: "SubscribeOnly", protobufDescriptorSet: new byte[] { 4, 5, 6 })
            };
            return new FoxRunSchemaManifestInfo(
                1,
                "Unity2Foxglove",
                "FoxRun",
                1,
                "global",
                "foxrun",
                new[] { new FoxRunSchemaTypeInfo("Demo.Contracts", contracts) });
        }

        private static FoxRunSchemaManifestInfo CreateProtobufManifest(int contractCount, int descriptorBytes)
        {
            var fields = new[]
            {
                new FoxRunSchemaFieldInfo("requested", "_requested", "field", "float32", false, false, false, 1)
            };
            var contracts = Enumerable.Range(0, contractCount)
                .Select(index => new FoxRunSchemaContractInfo(
                    "Demo.Input",
                    "/phase176/input/" + index.ToString("D2"),
                    "unity2foxglove.foxrun.Demo_Input_" + index,
                    "protobuf",
                    "protobuf-input-" + index,
                    "protobuf-input-" + index,
                    "policy",
                    "FixedRate",
                    10f,
                    0f,
                    0f,
                    fields,
                    flowMode: "SubscribeOnly",
                    protobufDescriptorSet: new byte[descriptorBytes]))
                .ToArray();
            return new FoxRunSchemaManifestInfo(
                1,
                "Unity2Foxglove",
                "FoxRun",
                1,
                "global",
                "foxrun",
                new[] { new FoxRunSchemaTypeInfo("Demo.Contracts", contracts) });
        }
    }
}
