// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/FoxRun
// Purpose: Locks the safe, directional subscription-contract catalog for the FoxRun Publish panel.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Google.Protobuf.Reflection;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.UnitTests.Harness;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.FoxRun
{
    public sealed partial class FoxRunSubscriptionCatalogTests
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
            Assert.False(contract.Value<bool>("isStream"));
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
        [Trait("Phase", "185-A")]
        public void MessagePackCatalogDetailIsSchemalessAndRetainsLogicalIdentity()
        {
            var scalarShape = new FoxRunTypeShapeInfo(
                FoxRunTypeShapeInfoKind.Canonical,
                "float32",
                "float32",
                false,
                FoxRunCollectionInfoKind.None,
                null,
                Array.Empty<FoxRunTypeFieldInfo>(),
                Array.Empty<FoxRunEnumValueInfo>(),
                canConstruct: true,
                isValueType: true);
            var listShape = new FoxRunTypeShapeInfo(
                FoxRunTypeShapeInfoKind.Collection,
                string.Empty,
                string.Empty,
                false,
                FoxRunCollectionInfoKind.List,
                scalarShape,
                Array.Empty<FoxRunTypeFieldInfo>(),
                Array.Empty<FoxRunEnumValueInfo>());
            var nestedObjectShape = new FoxRunTypeShapeInfo(
                FoxRunTypeShapeInfoKind.Object,
                "Demo.NestedInput",
                string.Empty,
                false,
                FoxRunCollectionInfoKind.None,
                null,
                Array.Empty<FoxRunTypeFieldInfo>(),
                Array.Empty<FoxRunEnumValueInfo>(),
                canConstruct: false,
                isValueType: true);
            var objectShape = new FoxRunTypeShapeInfo(
                FoxRunTypeShapeInfoKind.Object,
                "Demo.Input",
                string.Empty,
                false,
                FoxRunCollectionInfoKind.None,
                null,
                new[]
                {
                    new FoxRunTypeFieldInfo(
                        "samples",
                        "Samples",
                        listShape,
                        repeated: true,
                        repeatedCollectionKind: FoxRunCollectionInfoKind.List),
                    new FoxRunTypeFieldInfo(
                        "nested",
                        "Nested",
                        nestedObjectShape)
                },
                Array.Empty<FoxRunEnumValueInfo>(),
                canConstruct: false);
            var schedule = new FoxRunNormalizedScheduleInfo(
                (int)FoxRunPolicy.Change,
                hasExplicitHz: true,
                hz: 10f,
                tolerance: 0.25f,
                onlyIf: "Enabled",
                conditionMemberKind: (int)FoxRunConditionMemberKind.Property);
            var fields = new[]
            {
                new FoxRunSchemaFieldInfo(
                    "requested",
                    "_requested",
                    "field",
                    "Demo.Input",
                    false,
                    false,
                    false,
                    0,
                    objectShape,
                    schedule)
            };
            var manifest = new FoxRunSchemaManifestInfo(
                FoxrunManifestWriter.CurrentManifestVersion,
                "Unity2Foxglove",
                "FoxRun",
                1,
                "global",
                "foxrun",
                new[]
                {
                    new FoxRunSchemaTypeInfo("Demo.Contracts", new[]
                    {
                        new FoxRunSchemaContractInfo(
                            "Demo.Input", "/phase185/input", string.Empty, "msgpack",
                            string.Empty, "Demo.Input", "policy", "FixedRate", 10f, 0f, fields,
                            flow: "Subscribe")
                    })
                },
                subscriptionManifestHash: "subscriptions",
                subscriptionBindings: new[]
                {
                    WebSocketBinding("Demo.Input", "_requested", "/phase185/input")
                });

            var response = FoxRunSubscriptionCatalog.BuildResponse(
                manifest,
                subscriptionsEnabled: true,
                FoxRunEncoding.Protobuf,
                FoxRunEncoding.MessagePack,
                subscriptionRateLimitHz: 60,
                requestedTopic: "/phase185/input",
                includeDescriptor: true);

            var contract = Assert.Single((JArray)response["contracts"]);
            Assert.Equal("msgpack", contract.Value<string>("encoding"));
            Assert.Equal(string.Empty, contract.Value<string>("schemaName"));
            Assert.Equal("Demo.Input", contract.Value<string>("logicalSchemaName"));
            Assert.False(contract.Value<bool>("protobufDescriptorAvailable"));
            Assert.Null(contract["protobufDescriptorBase64"]);
            var field = Assert.Single((JArray)contract["fields"]);
            var typeShape = (JObject)field["typeShape"];
            Assert.Equal("Object", typeShape.Value<string>("kind"));
            Assert.Equal("Demo.Input", typeShape.Value<string>("typeName"));
            Assert.False(typeShape.Value<bool>("canConstruct"));
            Assert.False(typeShape.Value<bool>("isValueType"));
            var shapeFields = (JArray)typeShape["fields"];
            var samples = Assert.Single(
                shapeFields,
                candidate => ((JObject)candidate).Value<string>("jsonName") == "samples");
            Assert.Equal("Collection", samples["typeShape"].Value<string>("kind"));
            Assert.Equal("List", samples["typeShape"].Value<string>("collectionKind"));
            Assert.Equal(
                "Canonical",
                samples["typeShape"]["elementShape"].Value<string>("kind"));
            Assert.True(
                samples["typeShape"]["elementShape"].Value<bool>("canConstruct"));
            Assert.True(
                samples["typeShape"]["elementShape"].Value<bool>("isValueType"));
            var nested = Assert.Single(
                shapeFields,
                candidate => ((JObject)candidate).Value<string>("jsonName") == "nested");
            Assert.Equal("Object", nested["typeShape"].Value<string>("kind"));
            Assert.False(nested["typeShape"].Value<bool>("canConstruct"));
            Assert.True(nested["typeShape"].Value<bool>("isValueType"));
            var normalizedSchedule = (JObject)field["normalizedSchedule"];
            Assert.Equal((int)FoxRunPolicy.Change, normalizedSchedule.Value<int>("policy"));
            Assert.True(normalizedSchedule.Value<bool>("hasExplicitHz"));
            Assert.Equal(10f, normalizedSchedule.Value<float>("hz"));
            Assert.Equal("Enabled", normalizedSchedule.Value<string>("onlyIf"));
        }

        [Fact]
        [Trait("Phase", "185-A")]
        public void CatalogPreservesUnavailableSelectedMessagePackVariantWithoutCodecFallback()
        {
            var fields = new[]
            {
                new FoxRunSchemaFieldInfo(
                    "requested",
                    "_requested",
                    "field",
                    "int32",
                    false,
                    false)
            };
            var contracts = new[]
            {
                new FoxRunSchemaContractInfo(
                    "Demo.Input", "/phase185/unavailable", "Demo.Input", "protobuf",
                    "protobuf", "protobuf", "policy", "FixedRate", 10f, 0f, fields,
                    flow: "Subscribe",
                    protobufDescriptorSet: new byte[] { 1, 2, 3 },
                    logicalSchemaName: "Demo.Input"),
                new FoxRunSchemaContractInfo(
                    "Demo.Input", "/phase185/unavailable", string.Empty, "msgpack",
                    "msgpack", "msgpack", "policy", "FixedRate", 10f, 0f, fields,
                    flow: "Subscribe",
                    logicalSchemaName: "Demo.Input",
                    publishAvailable: false,
                    subscribeAvailable: false,
                    publishUnavailableDiagnosticId: "FOXRUN619",
                    publishUnavailableReason: "mixed publish schedule",
                    subscribeUnavailableDiagnosticId: "FOXRUN618",
                    subscribeUnavailableReason: "mixed ordinary/stream")
            };
            var manifest = new FoxRunSchemaManifestInfo(
                FoxrunManifestWriter.CurrentManifestVersion,
                "Unity2Foxglove",
                "FoxRun",
                1,
                "global",
                "foxrun",
                new[] { new FoxRunSchemaTypeInfo("Demo.Contracts", contracts) },
                subscriptionManifestHash: "subscriptions",
                subscriptionBindings: new[]
                {
                    WebSocketBinding(
                        "Demo.Input",
                        "_requested",
                        "/phase185/unavailable")
                });

            var response = FoxRunSubscriptionCatalog.BuildResponse(
                manifest,
                subscriptionsEnabled: true,
                FoxRunEncoding.Protobuf,
                FoxRunEncoding.MessagePack,
                subscriptionRateLimitHz: 60,
                requestedTopic: "/phase185/unavailable",
                includeDescriptor: true);

            var contract = Assert.Single((JArray)response["contracts"]);
            Assert.Equal("msgpack", contract.Value<string>("encoding"));
            Assert.False(contract.Value<bool>("subscribeAvailable"));
            Assert.Equal("FOXRUN618", contract.Value<string>("unavailableDiagnosticId"));
            Assert.Equal(string.Empty, contract.Value<string>("wireSchemaName"));
            Assert.False(contract.Value<bool>("protobufDescriptorAvailable"));
            Assert.Null(contract["protobufDescriptorBase64"]);
        }

        [Theory]
        [InlineData(FoxRunFlow.Publish, 2, 1)]
        [InlineData(FoxRunFlow.Subscribe, 1, 2)]
        [Trait("Phase", "185-A")]
        public void MessagePackManifestSplitsAsymmetricDirectionsAndCatalogRemainsInputOnly(
            FoxRunFlow oneWayFlow,
            int expectedPublishFields,
            int expectedSubscribeFields)
        {
            var duplex = DirectionalMessagePackMember(
                "_shared",
                FoxRunFlow.PublishAndSubscribe);
            var oneWay = DirectionalMessagePackMember(
                oneWayFlow == FoxRunFlow.Publish ? "_outputOnly" : "_inputOnly",
                oneWayFlow);
            var canonical = FoxRunManifestBuilder.Build(
                new[] { duplex, oneWay },
                manifestVersion: FoxrunManifestWriter.CurrentManifestVersion);
            var reversed = FoxRunManifestBuilder.Build(
                new[] { oneWay, duplex },
                manifestVersion: FoxrunManifestWriter.CurrentManifestVersion);
            var contracts = Assert.Single(canonical.Sections.FoxRun.Types).Contracts;
            var reversedContracts = Assert.Single(reversed.Sections.FoxRun.Types).Contracts;

            var publish = Assert.Single(
                contracts,
                contract => contract.Encoding == "msgpack" && contract.Flow == "Publish");
            var subscribe = Assert.Single(
                contracts,
                contract => contract.Encoding == "msgpack" && contract.Flow == "Subscribe");

            Assert.Equal(expectedPublishFields, publish.Fields.Count);
            Assert.Equal(expectedSubscribeFields, subscribe.Fields.Count);
            Assert.True(publish.PublishAvailable);
            Assert.False(publish.SubscribeAvailable);
            Assert.False(subscribe.PublishAvailable);
            Assert.True(subscribe.SubscribeAvailable);
            Assert.NotEqual(publish.BindingHash, subscribe.BindingHash);
            Assert.Equal(
                contracts.Select(ContractIdentity),
                reversedContracts.Select(ContractIdentity));

            var runtimeManifest = ToRuntimeManifest(canonical);
            var response = FoxRunSubscriptionCatalog.BuildResponse(
                runtimeManifest,
                subscriptionsEnabled: true,
                FoxRunEncoding.MessagePack,
                FoxRunEncoding.MessagePack,
                subscriptionRateLimitHz: 60,
                requestedTopic: "/phase185/asymmetric",
                includeDescriptor: true);
            var catalogContract = Assert.Single((JArray)response["contracts"]);

            Assert.Equal("Subscribe", catalogContract.Value<string>("flow"));
            Assert.Equal(expectedSubscribeFields, catalogContract.Value<int>("writableFieldCount"));
            var catalogFields = ((JArray)catalogContract["fields"])
                .Select(field => field.Value<string>("name"))
                .ToArray();
            Assert.Contains("shared", catalogFields);
            Assert.DoesNotContain("outputOnly", catalogFields);
        }

    }
}
