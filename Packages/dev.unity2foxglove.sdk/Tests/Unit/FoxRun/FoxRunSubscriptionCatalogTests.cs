// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/FoxRun
// Purpose: Locks the safe, directional subscription-contract catalog for the FoxRun Publish panel.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.UnitTests.Harness;
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
                FoxRunEncoding.Protobuf,
                FoxRunEncoding.JSON,
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
                FoxRunEncoding.Protobuf,
                FoxRunEncoding.JSON,
                subscriptionRateLimitHz: 12,
                requestedTopic: null,
                includeDescriptor: false);

            var contracts = (JArray)response["contracts"];
            var contract = Assert.Single(contracts);
            Assert.Equal("/phase176/input", contract.Value<string>("topic"));
            Assert.Equal("Subscribe", contract.Value<string>("flow"));
            Assert.Equal("json", contract.Value<string>("encoding"));
            Assert.Equal("json-input", contract.Value<string>("schemaName"));
            Assert.Equal(1, contract.Value<int>("writableFieldCount"));
            Assert.Null(contract["fields"]);
            Assert.Null(contract["protobufDescriptorBase64"]);

            var descriptorResponse = FoxRunSubscriptionCatalog.BuildResponse(
                CreateManifest(),
                subscriptionsEnabled: true,
                FoxRunEncoding.Protobuf,
                FoxRunEncoding.Protobuf,
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
                FoxRunEncoding.Protobuf,
                FoxRunEncoding.Protobuf,
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
                FoxRunEncoding.Protobuf,
                FoxRunEncoding.Protobuf,
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

        [Fact]
        public void CatalogExcludesRos2CdrAndUnknownVariantsInsteadOfRelabelingThemAsJson()
        {
            var response = FoxRunSubscriptionCatalog.BuildResponse(
                CreateUnsupportedEncodingManifest(),
                subscriptionsEnabled: true,
                FoxRunEncoding.Protobuf,
                FoxRunEncoding.Protobuf,
                subscriptionRateLimitHz: 30,
                requestedTopic: null,
                includeDescriptor: false);

            var contracts = ((JArray)response["contracts"]).Cast<JObject>().ToArray();
            Assert.Equal(2, contracts.Length);
            Assert.Collection(
                contracts,
                contract =>
                {
                    Assert.Equal("/phase179/json", contract.Value<string>("topic"));
                    Assert.Equal("json", contract.Value<string>("encoding"));
                },
                contract =>
                {
                    Assert.Equal("/phase179/protobuf", contract.Value<string>("topic"));
                    Assert.Equal("protobuf", contract.Value<string>("encoding"));
                });
            Assert.DoesNotContain(contracts, contract =>
                contract.Value<string>("topic") is "/phase179/ros2" or "/phase179/cdr" or "/phase179/unknown");
            Assert.Null(response["token"]);
            Assert.Null(response["clientId"]);
            Assert.Null(response["queueState"]);
            Assert.Null(response["maxPayloadBytes"]);
        }

        [Fact]
        public void CatalogFiltersCoexistingBindingsByCapturedEffectiveProvider()
        {
            var manifest = CreateProviderAwareManifest();
            var webSocket = FoxRunSubscriptionCatalog.BuildResponse(
                manifest,
                subscriptionsEnabled: true,
                FoxRunEncoding.Protobuf,
                FoxRunEncoding.Protobuf,
                FoxRunEndpoint.Foxglove,
                subscriptionRateLimitHz: 30,
                requestedTopic: null,
                includeDescriptor: false);

            Assert.Collection(
                ((JArray)webSocket["contracts"]).Cast<JObject>(),
                contract =>
                {
                    Assert.Equal("/phase179/dual", contract.Value<string>("topic"));
                    Assert.Equal("protobuf", contract.Value<string>("encoding"));
                },
                contract =>
                {
                    Assert.Equal("/phase179/json", contract.Value<string>("topic"));
                    Assert.Equal("json", contract.Value<string>("encoding"));
                });

            var nativeDefault = FoxRunSubscriptionCatalog.BuildResponse(
                manifest,
                subscriptionsEnabled: true,
                FoxRunEncoding.Protobuf,
                FoxRunEncoding.JSON,
                FoxRunEndpoint.Ros2Native,
                subscriptionRateLimitHz: 30,
                requestedTopic: null,
                includeDescriptor: false);
            var onlyExplicitWebSocket = Assert.Single((JArray)nativeDefault["contracts"]);
            Assert.Equal("/phase179/json", onlyExplicitWebSocket.Value<string>("topic"));
            Assert.Equal("json", onlyExplicitWebSocket.Value<string>("encoding"));

            var nativeDetail = FoxRunSubscriptionCatalog.BuildResponse(
                manifest,
                subscriptionsEnabled: true,
                FoxRunEncoding.Protobuf,
                FoxRunEncoding.Protobuf,
                FoxRunEndpoint.Foxglove,
                subscriptionRateLimitHz: 30,
                requestedTopic: "/phase179/native",
                includeDescriptor: true);
            Assert.Empty((JArray)nativeDetail["contracts"]);
            Assert.DoesNotContain("cdr", nativeDetail.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void V2CatalogFailsClosedWhenSubscriptionBindingIsMissing()
        {
            var manifest = CreateV2CatalogIntegrityManifest(Array.Empty<FoxRunSchemaSubscriptionBindingInfo>());

            var response = FoxRunSubscriptionCatalog.BuildResponse(
                manifest,
                subscriptionsEnabled: true,
                FoxRunEncoding.JSON,
                FoxRunEncoding.JSON,
                FoxRunEndpoint.Foxglove,
                subscriptionRateLimitHz: 30,
                requestedTopic: null,
                includeDescriptor: false);

            Assert.Empty((JArray)response["contracts"]);
        }

        [Fact]
        public void LegacyCatalogWithoutDirectionalBindingsFailsClosed()
        {
            var current = CreateManifest();
            var legacy = new FoxRunSchemaManifestInfo(
                1,
                current.PackageName,
                current.GeneratorName,
                current.GeneratorMajorVersion,
                current.GlobalManifestHash,
                current.FoxRunManifestHash,
                current.Types);

            var response = FoxRunSubscriptionCatalog.BuildResponse(
                legacy,
                subscriptionsEnabled: true,
                FoxRunEncoding.Protobuf,
                FoxRunEncoding.Protobuf,
                FoxRunEndpoint.Foxglove,
                subscriptionRateLimitHz: 30,
                requestedTopic: null,
                includeDescriptor: false);

            Assert.Empty((JArray)response["contracts"]);
        }

        [Fact]
        public void V2CatalogFailsClosedWhenSubscriptionBindingIdentityDrifts()
        {
            var manifest = CreateV2CatalogIntegrityManifest(new[]
            {
                new FoxRunSchemaSubscriptionBindingInfo(
                    "Demo.Input", "_renamed", "/phase179/integrity", "Subscribe",
                    FoxRunEndpoint.Foxglove,
                    FoxRunRos2QosPreset.Inherit,
                    supportsWebSocket: true,
                    supportsRos2Native: false,
                    nativeType: string.Empty,
                    canonicalRosType: string.Empty,
                    copyShapeIdentity: string.Empty)
            });

            var response = FoxRunSubscriptionCatalog.BuildResponse(
                manifest,
                subscriptionsEnabled: true,
                FoxRunEncoding.JSON,
                FoxRunEncoding.JSON,
                FoxRunEndpoint.Foxglove,
                subscriptionRateLimitHz: 30,
                requestedTopic: null,
                includeDescriptor: false);

            Assert.Empty((JArray)response["contracts"]);
        }

        [Fact]
        public void ManagerCatalogReadsOneFrozenSnapshotWithEffectiveProviderGating()
        {
            var source = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.FoxRunSubscriptionCatalog.cs");
            var handler = TestSources.ExtractMethod(
                source,
                "private JToken HandleFoxRunSubscriptionCatalogRequest(JToken request)");

            Assert.Contains(
                "var subscriptionPolicy = ActiveFoxRunSubscriptionSessionPolicy;",
                handler,
                StringComparison.Ordinal);
            Assert.Contains(
                "subscriptionPolicy.SubscriptionsEnabled && IsFoxRunInboundAuthorized",
                handler,
                StringComparison.Ordinal);
            Assert.Contains(
                "subscriptionPolicy.FoxgloveEncoding",
                handler,
                StringComparison.Ordinal);
            Assert.Contains(
                "subscriptionPolicy.DefaultSource",
                handler,
                StringComparison.Ordinal);
            Assert.Contains(
                "subscriptionPolicy.DefaultSubscribeRateHz",
                handler,
                StringComparison.Ordinal);
            Assert.DoesNotContain("_defaultFoxRunSubscriptionSource", handler, StringComparison.Ordinal);
        }

        private static FoxRunSchemaManifestInfo CreateManifest()
        {
            var fields = new[]
            {
                new FoxRunSchemaFieldInfo("requested", "_requested", "field", "float32", false, false, false, 1)
            };
            var contracts = new[]
            {
                new FoxRunSchemaContractInfo("Demo.Output", "/phase176/output", string.Empty, "json", "json-output", "json-output", "policy", "FixedRate", 10f, 0f, fields, flow: "Publish"),
                new FoxRunSchemaContractInfo("Demo.Input", "/phase176/input", "json-input", "json", "json-input", "json-input", "policy", "FixedRate", 10f, 0f, fields, flow: "Subscribe"),
                new FoxRunSchemaContractInfo("Demo.Input", "/phase176/input", "protobuf-input", "protobuf", "protobuf-input", "protobuf-input", "policy", "FixedRate", 10f, 0f, fields, flow: "Subscribe", protobufDescriptorSet: new byte[] { 4, 5, 6 })
            };
            return new FoxRunSchemaManifestInfo(
                2,
                "Unity2Foxglove",
                "FoxRun",
                1,
                "global",
                "foxrun",
                new[] { new FoxRunSchemaTypeInfo("Demo.Contracts", contracts) },
                subscriptionManifestHash: "subscriptions",
                subscriptionBindings: new[]
                {
                    WebSocketBinding("Demo.Input", "_requested", "/phase176/input")
                });
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
                    fields,
                    flow: "Subscribe",
                    protobufDescriptorSet: new byte[descriptorBytes]))
                .ToArray();
            return new FoxRunSchemaManifestInfo(
                2,
                "Unity2Foxglove",
                "FoxRun",
                1,
                "global",
                "foxrun",
                new[] { new FoxRunSchemaTypeInfo("Demo.Contracts", contracts) },
                subscriptionManifestHash: "subscriptions",
                subscriptionBindings: Enumerable.Range(0, contractCount)
                    .Select(index => WebSocketBinding(
                        "Demo.Input",
                        "_requested",
                        "/phase176/input/" + index.ToString("D2")))
                    .ToArray());
        }

        private static FoxRunSchemaManifestInfo CreateUnsupportedEncodingManifest()
        {
            var fields = new[]
            {
                new FoxRunSchemaFieldInfo("value", "_value", "field", "int32", false, false, false, 1)
            };
            var contracts = new[]
            {
                SubscriptionContract("/phase179/json", "json", fields),
                SubscriptionContract("/phase179/protobuf", "protobuf", fields),
                SubscriptionContract("/phase179/ros2", "ros2", fields),
                SubscriptionContract("/phase179/cdr", "cdr", fields),
                SubscriptionContract("/phase179/unknown", "yaml", fields)
            };
            return new FoxRunSchemaManifestInfo(
                2,
                "Unity2Foxglove",
                "FoxRun",
                1,
                "global",
                "foxrun",
                new[] { new FoxRunSchemaTypeInfo("Demo.Contracts", contracts) },
                subscriptionManifestHash: "subscriptions",
                subscriptionBindings: contracts
                    .Select(contract => WebSocketBinding(
                        "Demo.Input",
                        "_value",
                        contract.Topic))
                    .ToArray());
        }

        private static FoxRunSchemaManifestInfo CreateProviderAwareManifest()
        {
            var jsonFields = new[]
            {
                new FoxRunSchemaFieldInfo("json", "_json", "field", "int32", false, false, false, 1)
            };
            var dualFields = new[]
            {
                new FoxRunSchemaFieldInfo("dual", "_dual", "field", "int32", false, false, false, 1)
            };
            var contracts = new[]
            {
                SubscriptionContract("/phase179/json", "json", jsonFields),
                SubscriptionContract("/phase179/dual", "json", dualFields),
                SubscriptionContract("/phase179/dual", "protobuf", dualFields)
            };
            var bindings = new[]
            {
                new FoxRunSchemaSubscriptionBindingInfo(
                    "Demo.Input", "_json", "/phase179/json", "Subscribe",
                    FoxRunEndpoint.Foxglove,
                    FoxRunRos2QosPreset.Inherit,
                    supportsWebSocket: true,
                    supportsRos2Native: false,
                    nativeType: string.Empty,
                    canonicalRosType: string.Empty,
                    copyShapeIdentity: string.Empty),
                new FoxRunSchemaSubscriptionBindingInfo(
                    "Demo.Input", "_dual", "/phase179/dual", "Subscribe",
                    (FoxRunEndpoint)0,
                    FoxRunRos2QosPreset.SensorData,
                    supportsWebSocket: true,
                    supportsRos2Native: true,
                    nativeType: "std_msgs.msg.String",
                    canonicalRosType: "std_msgs/msg/String",
                    copyShapeIdentity: "std-string-v1"),
                new FoxRunSchemaSubscriptionBindingInfo(
                    "Demo.Input", "_native", "/phase179/native", "Subscribe",
                    FoxRunEndpoint.Ros2Native,
                    FoxRunRos2QosPreset.Reliable,
                    supportsWebSocket: true,
                    supportsRos2Native: true,
                    nativeType: "geometry_msgs.msg.Twist",
                    canonicalRosType: "geometry_msgs/msg/Twist",
                    copyShapeIdentity: "twist-v1")
            };
            return new FoxRunSchemaManifestInfo(
                2,
                "Unity2Foxglove",
                "FoxRun",
                1,
                "global",
                "foxrun",
                new[] { new FoxRunSchemaTypeInfo("Demo.Contracts", contracts) },
                subscriptionManifestHash: "subscriptions",
                subscriptionBindings: bindings);
        }

        private static FoxRunSchemaManifestInfo CreateV2CatalogIntegrityManifest(
            IReadOnlyList<FoxRunSchemaSubscriptionBindingInfo> bindings)
        {
            var fields = new[]
            {
                new FoxRunSchemaFieldInfo("requested", "_requested", "field", "float32", false, false, false, 1)
            };
            var contracts = new[]
            {
                SubscriptionContract("/phase179/integrity", "json", fields)
            };
            return new FoxRunSchemaManifestInfo(
                2,
                "Unity2Foxglove",
                "FoxRun",
                1,
                "global",
                "foxrun",
                new[] { new FoxRunSchemaTypeInfo("Demo.Contracts", contracts) },
                subscriptionManifestHash: "subscriptions",
                subscriptionBindings: bindings);
        }

        private static FoxRunSchemaContractInfo SubscriptionContract(
            string topic,
            string encoding,
            IReadOnlyList<FoxRunSchemaFieldInfo> fields)
            => new(
                "Demo.Input",
                topic,
                "phase179." + encoding,
                encoding,
                encoding + "-input",
                encoding + "-input",
                "policy",
                "FixedRate",
                10f,
                0f,
                fields,
                flow: "Subscribe",
                protobufDescriptorSet: string.Equals(encoding, "protobuf", StringComparison.Ordinal)
                    ? new byte[] { 1, 2, 3 }
                    : null);

        private static FoxRunSchemaSubscriptionBindingInfo WebSocketBinding(
            string declaringType,
            string memberName,
            string topic)
            => new(
                declaringType,
                memberName,
                topic,
                "Subscribe",
                FoxRunEndpoint.Foxglove,
                FoxRunRos2QosPreset.Inherit,
                supportsWebSocket: true,
                supportsRos2Native: false,
                nativeType: string.Empty,
                canonicalRosType: string.Empty,
                copyShapeIdentity: string.Empty);
    }
}
