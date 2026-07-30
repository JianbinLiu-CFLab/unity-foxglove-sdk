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

        [Fact]
        [Trait("Phase", "185-A")]
        public void InheritedAsymmetricTopicPreservesEveryEncodingWithoutDuplicateDirectionalSummaries()
        {
            var duplex = DirectionalMessagePackMember(
                "_shared",
                FoxRunFlow.PublishAndSubscribe,
                encoding: 0);
            var output = DirectionalMessagePackMember(
                "_outputOnly",
                FoxRunFlow.Publish,
                encoding: 0);
            var canonical = FoxRunManifestBuilder.Build(
                new[] { duplex, output },
                manifestVersion: FoxrunManifestWriter.CurrentManifestVersion);
            var contracts = Assert.Single(canonical.Sections.FoxRun.Types).Contracts;

            foreach (var protocolEncoding in new[] { "json", "protobuf", "msgpack" })
            {
                var publish = Assert.Single(
                    contracts,
                    contract => contract.Encoding == protocolEncoding && contract.Flow == "Publish");
                var subscribe = Assert.Single(
                    contracts,
                    contract => contract.Encoding == protocolEncoding && contract.Flow == "Subscribe");
                Assert.Equal(2, publish.Fields.Count);
                Assert.Single(subscribe.Fields);
                Assert.True(publish.PublishAvailable);
                Assert.False(publish.SubscribeAvailable);
                Assert.False(subscribe.PublishAvailable);
                Assert.True(subscribe.SubscribeAvailable);
            }

            var runtimeManifest = ToRuntimeManifest(canonical);
            FoxRunSchemaInfoRegistry.ClearForTests();
            try
            {
                FoxRunSchemaInfoRegistry.RegisterGenerated(runtimeManifest);
                var registry = new DefaultSchemaRegistry();
                FoxRunSchemaInfoRegistry.RegisterGeneratedSchemas(registry);

                var publishJson = Assert.Single(
                    contracts,
                    contract => contract.Encoding == "json" && contract.Flow == "Publish");
                Assert.True(registry.TryGetSchema(
                    publishJson.SchemaName,
                    "jsonschema",
                    out var jsonSchema));
                Assert.Contains("\"shared\"", jsonSchema.Content, StringComparison.Ordinal);
                Assert.Contains("\"outputOnly\"", jsonSchema.Content, StringComparison.Ordinal);

                var publishProtobuf = Assert.Single(
                    contracts,
                    contract => contract.Encoding == "protobuf" && contract.Flow == "Publish");
                Assert.True(registry.TryGetSchema(
                    publishProtobuf.SchemaName,
                    "protobuf",
                    out var protobufSchema));
                var descriptorSet = FileDescriptorSet.Parser.ParseFrom(protobufSchema.RawContent);
                var descriptor = Assert.Single(Assert.Single(descriptorSet.File).MessageType);
                Assert.Equal(
                    new[] { "outputOnly", "shared" },
                    descriptor.Field
                        .Select(field => field.JsonName)
                        .OrderBy(name => name, StringComparer.Ordinal));

                foreach (var encoding in new[]
                         {
                             FoxRunEncoding.JSON,
                             FoxRunEncoding.Protobuf,
                             FoxRunEncoding.MessagePack
                         })
                {
                    var summaries = FoxRunSchemaInfoRegistry.GetTopicSummaries(
                        encoding,
                        encoding);
                    Assert.Equal(2, summaries.Count);
                    Assert.Equal(
                        new[] { "Publish", "Subscribe" },
                        summaries.Select(summary => summary.Direction).OrderBy(value => value, StringComparer.Ordinal));
                    Assert.All(summaries, summary =>
                    {
                        Assert.Equal(encoding, summary.EffectiveEncoding);
                        Assert.True(summary.Available);
                    });

                    var response = FoxRunSubscriptionCatalog.BuildResponse(
                        runtimeManifest,
                        subscriptionsEnabled: true,
                        encoding,
                        encoding,
                        subscriptionRateLimitHz: 60,
                        requestedTopic: "/phase185/asymmetric",
                        includeDescriptor: true);
                    var catalogContract = Assert.Single((JArray)response["contracts"]);
                    Assert.Equal(
                        FoxRunEncodingResolver.ToProtocolEncoding(encoding),
                        catalogContract.Value<string>("encoding"));
                    Assert.Equal(1, catalogContract.Value<int>("writableFieldCount"));
                }
            }
            finally
            {
                FoxRunSchemaInfoRegistry.ClearForTests();
            }
        }

        [Theory]
        [InlineData("json")]
        [InlineData("protobuf")]
        [Trait("Phase", "185-A")]
        public void GeneratedPublishSchemaRegistrationRejectsConflictingSameKeyBeforeAnyWrite(
            string encoding)
        {
            FoxRunSchemaInfoRegistry.ClearForTests();
            var failures = new List<(string Message, Exception Error)>();
            void OnFailure(string message, Exception error) => failures.Add((message, error));
            FoxRunSchemaInfoRegistry.GeneratedSchemaRegistrationFailed += OnFailure;
            try
            {
                var registry = new CountingSchemaRegistry();
                FoxRunSchemaInfoRegistry.RegisterGenerated(
                    CreatePublishSchemaCollisionManifest(encoding, identical: false));

                FoxRunSchemaInfoRegistry.RegisterGeneratedSchemas(registry);

                Assert.Equal(0, registry.RegisterCalls);
                Assert.False(registry.TryGetSchema(
                    PublishCollisionSchemaName(encoding),
                    SchemaEncoding(encoding),
                    out _));
                var failure = Assert.Single(failures);
                Assert.IsType<InvalidOperationException>(failure.Error);
                Assert.Contains("conflicting generated FoxRun publish schemas", failure.Message, StringComparison.Ordinal);
            }
            finally
            {
                FoxRunSchemaInfoRegistry.GeneratedSchemaRegistrationFailed -= OnFailure;
                FoxRunSchemaInfoRegistry.ClearForTests();
            }
        }

        [Theory]
        [InlineData("json")]
        [InlineData("protobuf")]
        [Trait("Phase", "185-A")]
        public void GeneratedPublishSchemaRegistrationDeduplicatesIdenticalSameKey(
            string encoding)
        {
            FoxRunSchemaInfoRegistry.ClearForTests();
            var failures = new List<(string Message, Exception Error)>();
            void OnFailure(string message, Exception error) => failures.Add((message, error));
            FoxRunSchemaInfoRegistry.GeneratedSchemaRegistrationFailed += OnFailure;
            try
            {
                var registry = new CountingSchemaRegistry();
                FoxRunSchemaInfoRegistry.RegisterGenerated(
                    CreatePublishSchemaCollisionManifest(encoding, identical: true));

                FoxRunSchemaInfoRegistry.RegisterGeneratedSchemas(registry);

                Assert.Equal(1, registry.RegisterCalls);
                Assert.Empty(failures);
                Assert.True(registry.TryGetSchema(
                    PublishCollisionSchemaName(encoding),
                    SchemaEncoding(encoding),
                    out _));
            }
            finally
            {
                FoxRunSchemaInfoRegistry.GeneratedSchemaRegistrationFailed -= OnFailure;
                FoxRunSchemaInfoRegistry.ClearForTests();
            }
        }

        [Fact]
        [Trait("Phase", "185-A")]
        public void GeneratedPublishSchemaPreparationFailureBlocksWholeKeyButNotIndependentKeys()
        {
            FoxRunSchemaInfoRegistry.ClearForTests();
            var failures = new List<(string Message, Exception Error)>();
            void OnFailure(string message, Exception error) => failures.Add((message, error));
            FoxRunSchemaInfoRegistry.GeneratedSchemaRegistrationFailed += OnFailure;
            try
            {
                var registry = new CountingSchemaRegistry();
                FoxRunSchemaInfoRegistry.RegisterGenerated(
                    CreatePublishSchemaPreparationFailureManifest());

                FoxRunSchemaInfoRegistry.RegisterGeneratedSchemas(registry);

                Assert.Equal(1, registry.RegisterCalls);
                Assert.False(registry.TryGetSchema(
                    "Demo.PreparationFailure",
                    FoxgloveSchemaDefinitions.JsonSchemaEncoding,
                    out _));
                Assert.True(registry.TryGetSchema(
                    "Demo.Independent",
                    FoxgloveSchemaDefinitions.JsonSchemaEncoding,
                    out _));
                var failure = Assert.Single(failures);
                Assert.IsType<InvalidOperationException>(failure.Error);
                Assert.Contains(
                    "Failed to prepare generated FoxRun json schema",
                    failure.Message,
                    StringComparison.Ordinal);
            }
            finally
            {
                FoxRunSchemaInfoRegistry.GeneratedSchemaRegistrationFailed -= OnFailure;
                FoxRunSchemaInfoRegistry.ClearForTests();
            }
        }

        [Fact]
        [Trait("Phase", "184-E")]
        public void CatalogPublishesStreamSemanticsFromTheMaintainedSubscriptionBinding()
        {
            var response = FoxRunSubscriptionCatalog.BuildResponse(
                CreateManifest(isStream: true),
                subscriptionsEnabled: true,
                FoxRunEncoding.Protobuf,
                FoxRunEncoding.JSON,
                subscriptionRateLimitHz: 60,
                requestedTopic: null,
                includeDescriptor: false);

            var contract = Assert.Single((JArray)response["contracts"]);
            Assert.True(contract.Value<bool>("isStream"));
            Assert.Equal(60, response.Value<int>("subscriptionRateLimitHz"));
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

            var actualResponse = FoxRunSubscriptionCatalog.BuildResponse(
                CreateManifest(),
                subscriptionsEnabled: true,
                FoxRunEncoding.Protobuf,
                FoxRunEncoding.Protobuf,
                subscriptionRateLimitHz: 12,
                requestedTopic: "/phase176/input",
                includeDescriptor: true);
            var actualContract = Assert.Single((JArray)actualResponse["contracts"]!);
            var schemaKeys = ((JObject)contracts["items"]!["properties"]!)
                .Properties()
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            var actualKeys = ((JObject)actualContract)
                .Properties()
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            Assert.Equal(schemaKeys, actualKeys);
            Assert.Contains("hz", schemaKeys);
            Assert.Contains("isStream", schemaKeys);
            Assert.DoesNotContain("rateHz", schemaKeys);
            Assert.Contains("hz", actualKeys);
            Assert.Contains("isStream", actualKeys);
            Assert.DoesNotContain("rateHz", actualKeys);
        }

        [Fact]
        public void PublishPanelValidatesTheMaintainedCatalogFieldNames()
        {
            var panel = TestSources.Text(
                "Tools/foxglove-extensions/foxrun-publish-panel/src/index.ts");

            Assert.Contains("typeof contract.flow === \"string\"", panel, StringComparison.Ordinal);
            Assert.Contains("typeof contract.hz === \"number\"", panel, StringComparison.Ordinal);
            Assert.Contains("typeof contract.isStream === \"boolean\"", panel, StringComparison.Ordinal);
            Assert.DoesNotContain("contract.flowMode", panel, StringComparison.Ordinal);
            Assert.DoesNotContain("contract.rateHz", panel, StringComparison.Ordinal);
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
        [Trait("Phase", "185-A")]
        public void InspectorSummariesExcludeRos2CdrAndUnknownVariantsWithoutThrowing()
        {
            FoxRunSchemaInfoRegistry.ClearForTests();
            try
            {
                FoxRunSchemaInfoRegistry.RegisterGenerated(
                    CreateUnsupportedEncodingManifest());

                var summaries = FoxRunSchemaInfoRegistry.GetTopicSummaries(
                    FoxRunEncoding.Protobuf,
                    FoxRunEncoding.Protobuf);

                Assert.Equal(2, summaries.Count);
                Assert.Equal(
                    new[] { "/phase179/json", "/phase179/protobuf" },
                    summaries.Select(summary => summary.Topic).ToArray());
            }
            finally
            {
                FoxRunSchemaInfoRegistry.ClearForTests();
            }
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
                "subscriptionPolicy.WebSocketEncoding",
                handler,
                StringComparison.Ordinal);
            Assert.Contains(
                "subscriptionPolicy.DefaultProvider.Value",
                handler,
                StringComparison.Ordinal);
            Assert.Contains(
                "subscriptionPolicy.TransportAdmissionRateLimitHz",
                handler,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "subscriptionPolicy.DefaultSubscribeRateHz",
                handler,
                StringComparison.Ordinal);
            Assert.DoesNotContain("subscriptionPolicy.DefaultSource", handler, StringComparison.Ordinal);
            Assert.DoesNotContain("_defaultFoxRunSubscriptionSource", handler, StringComparison.Ordinal);
        }

        private static FoxRunSchemaManifestInfo CreateManifest(bool isStream = false)
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
                    WebSocketBinding("Demo.Input", "_requested", "/phase176/input", isStream)
                });
        }

        private static FoxRunManifestMember DirectionalMessagePackMember(
            string memberName,
            FoxRunFlow flow,
            int encoding = (int)FoxRunEncoding.MessagePack)
            => new(
                "Demo",
                "Asymmetric",
                memberName,
                "field",
                "System.Int32",
                true,
                false,
                string.Empty,
                "/phase185/asymmetric",
                10f,
                "Demo.Asymmetric",
                (int)FoxRunPolicy.FixedRate,
                0f,
                isAggregateMember: true,
                jsonFieldName: memberName.TrimStart('_'),
                flow: (int)flow,
                encoding: encoding,
                typeShape: FoxRunTypeShape.Canonical("int32"));

        private static string ContractIdentity(FoxRunManifestContract contract)
            => contract.Flow + "|" + contract.BindingHash + "|" + contract.ContractHash + "|"
               + string.Join(",", contract.Fields.Select(field => field.MemberName));

        private static FoxRunSchemaManifestInfo ToRuntimeManifest(
            FoxRunCanonicalManifest manifest)
        {
            var types = manifest.Sections.FoxRun.Types
                .Select(type => new FoxRunSchemaTypeInfo(
                    type.DeclaringType,
                    type.Contracts.Select(contract => new FoxRunSchemaContractInfo(
                        contract.DeclaringType,
                        contract.Topic,
                        contract.SchemaName,
                        contract.Encoding,
                        contract.ContractHash,
                        contract.BindingHash,
                        contract.PolicyHash,
                        contract.Policy.Mode,
                        contract.Policy.Hz,
                        contract.Policy.Tolerance,
                        contract.Fields.Select(field => new FoxRunSchemaFieldInfo(
                            field.JsonName,
                            field.MemberName,
                            field.MemberKind,
                            field.Type,
                            field.Nullable,
                            field.Array,
                            field.Aggregate,
                            field.ProtobufMetadata?.FieldNumber ?? 0))
                            .ToArray(),
                        contract.Flow,
                        protobufDescriptorSet: string.Equals(
                            contract.Encoding,
                            "protobuf",
                            StringComparison.Ordinal)
                            ? BuildRuntimeProtobufDescriptor(contract)
                            : null,
                        logicalSchemaName: contract.LogicalSchemaName,
                        publishAvailable: contract.PublishAvailable,
                        subscribeAvailable: contract.SubscribeAvailable,
                        publishUnavailableDiagnosticId: contract.PublishUnavailableDiagnosticId,
                        publishUnavailableReason: contract.PublishUnavailableReason,
                        subscribeUnavailableDiagnosticId: contract.SubscribeUnavailableDiagnosticId,
                        subscribeUnavailableReason: contract.SubscribeUnavailableReason))
                        .ToArray()))
                .ToArray();
            var bindings = manifest.Sections.Subscriptions.Bindings
                .Select(binding => new FoxRunSchemaSubscriptionBindingInfo(
                    binding.DeclaringType,
                    binding.MemberName,
                    binding.Topic,
                    binding.Flow,
                    binding.PublishTransportIds,
                    binding.SubscribeTransportId,
                    binding.Reliability,
                    binding.Durability,
                    binding.History,
                    binding.Depth,
                    binding.SupportsWebSocket,
                    isStream: binding.IsStream))
                .ToArray();

            return new FoxRunSchemaManifestInfo(
                manifest.ManifestVersion,
                manifest.Package,
                manifest.Generator.Name,
                manifest.Generator.MajorVersion,
                manifest.GlobalManifestHash,
                manifest.Sections.FoxRun.ManifestHash,
                types,
                manifest.Sections.Subscriptions.ManifestHash,
                bindings);
        }

        private static byte[] BuildRuntimeProtobufDescriptor(
            FoxRunManifestContract contract)
            => FoxRunProtobufContractBuilder.Build(
                    new FoxRunProtobufContractInput(
                        contract.DeclaringType,
                        contract.Topic,
                        contract.SchemaName,
                        contract.Fields.Select(field => new FoxRunProtobufFieldInput(
                                field.JsonName,
                                field.MemberName,
                                field.Type,
                                field.Array,
                                field.ProtobufMetadata?.FieldNumber ?? 0,
                                field.TypeShape,
                                field.ProtobufMetadata))
                            .ToArray()))
                .FileDescriptorSet;

        private static FoxRunSchemaManifestInfo CreatePublishSchemaCollisionManifest(
            string encoding,
            bool identical)
        {
            var schemaName = PublishCollisionSchemaName(encoding);
            var shared = new FoxRunSchemaFieldInfo(
                "shared",
                "_shared",
                "field",
                "int32",
                false,
                false,
                aggregate: true,
                protobufFieldNumber: 1);
            var second = identical
                ? new FoxRunSchemaFieldInfo(
                    "shared",
                    "_shared",
                    "field",
                    "int32",
                    false,
                    false,
                    aggregate: true,
                    protobufFieldNumber: 1)
                : new FoxRunSchemaFieldInfo(
                    "outputOnly",
                    "_outputOnly",
                    "field",
                    "int32",
                    false,
                    false,
                    aggregate: true,
                    protobufFieldNumber: 2);
            var firstFields = new[] { shared };
            var secondFields = new[] { second };
            var first = PublishCollisionContract(
                encoding,
                schemaName,
                "Publish",
                "first",
                firstFields);
            var duplicate = PublishCollisionContract(
                encoding,
                schemaName,
                "PublishAndSubscribe",
                "second",
                secondFields);
            return new FoxRunSchemaManifestInfo(
                FoxrunManifestWriter.CurrentManifestVersion,
                "Unity2Foxglove",
                "FoxRun",
                1,
                "collision-global",
                "collision-foxrun",
                new[]
                {
                    new FoxRunSchemaTypeInfo(
                        "Demo.PublishCollision",
                        new[] { first, duplicate })
                });
        }

        private static FoxRunSchemaManifestInfo CreatePublishSchemaPreparationFailureManifest()
        {
            var valid = new FoxRunSchemaFieldInfo(
                "shared",
                "_shared",
                "field",
                "int32",
                false,
                false,
                aggregate: true,
                protobufFieldNumber: 0);
            var unsupported = new FoxRunSchemaFieldInfo(
                "unsupported",
                "_unsupported",
                "field",
                "demo.unsupported",
                false,
                false,
                aggregate: true,
                protobufFieldNumber: 0);
            var successfulSameKey = PublishCollisionContract(
                "json",
                "Demo.PreparationFailure",
                "Publish",
                "preparation-success",
                new[] { valid });
            var failingSameKey = PublishCollisionContract(
                "json",
                "Demo.PreparationFailure",
                "PublishAndSubscribe",
                "preparation-failure",
                new[] { unsupported });
            var independent = PublishCollisionContract(
                "json",
                "Demo.Independent",
                "Publish",
                "independent",
                new[] { valid });
            return new FoxRunSchemaManifestInfo(
                FoxrunManifestWriter.CurrentManifestVersion,
                "Unity2Foxglove",
                "FoxRun",
                1,
                "preparation-failure-global",
                "preparation-failure-foxrun",
                new[]
                {
                    new FoxRunSchemaTypeInfo(
                        "Demo.PreparationFailure",
                        new[] { successfulSameKey, failingSameKey, independent })
                });
        }

        private static FoxRunSchemaContractInfo PublishCollisionContract(
            string encoding,
            string schemaName,
            string flow,
            string identity,
            IReadOnlyList<FoxRunSchemaFieldInfo> fields)
        {
            var descriptor = string.Equals(encoding, "protobuf", StringComparison.Ordinal)
                ? FoxRunProtobufContractBuilder.Build(
                        new FoxRunProtobufContractInput(
                            "Demo.PublishCollision",
                            "/phase185/publish-collision",
                            schemaName,
                            fields.Select(field => new FoxRunProtobufFieldInput(
                                    field.JsonName,
                                    field.MemberName,
                                    field.Type,
                                    field.Array,
                                    field.ProtobufFieldNumber))
                                .ToArray()))
                    .FileDescriptorSet
                : null;
            return new FoxRunSchemaContractInfo(
                "Demo.PublishCollision",
                "/phase185/publish-collision",
                schemaName,
                encoding,
                identity + "-contract",
                identity + "-binding",
                "policy",
                "FixedRate",
                10f,
                0f,
                fields,
                flow,
                descriptor);
        }

        private static string PublishCollisionSchemaName(string encoding)
            => string.Equals(encoding, "protobuf", StringComparison.Ordinal)
                ? "unity2foxglove.foxrun.PublishCollision"
                : "Demo.PublishCollision";

        private static string SchemaEncoding(string encoding)
            => string.Equals(encoding, "protobuf", StringComparison.Ordinal)
                ? "protobuf"
                : "jsonschema";

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
                FoxrunManifestWriter.CurrentManifestVersion,
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
                FoxrunManifestWriter.CurrentManifestVersion,
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

        private static FoxRunSchemaContractInfo SubscriptionContract(
            string topic,
            string encoding,
            IReadOnlyList<FoxRunSchemaFieldInfo> fields,
            string subscribeTransportId = null)
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
                    : null,
                subscribeTransportId: subscribeTransportId);

        private static FoxRunSchemaSubscriptionBindingInfo WebSocketBinding(
            string declaringType,
            string memberName,
            string topic,
            bool isStream = false)
            => new(
                declaringType,
                memberName,
                topic,
                "Subscribe",
                publishTransportIds: null,
                subscribeTransportId: FoxgloveWebSocketTransport.Id,
                reliability: "inherit",
                durability: "inherit",
                history: "inherit",
                depth: 0,
                supportsWebSocket: true,
                isStream: isStream);

        private sealed class CountingSchemaRegistry : IEncodingAwareSchemaRegistry
        {
            private readonly DefaultSchemaRegistry _inner = new();

            public int RegisterCalls { get; private set; }

            public bool TryGetSchema(string name, out SchemaEntry entry)
                => _inner.TryGetSchema(name, out entry);

            public bool TryGetSchema(
                string name,
                string encoding,
                out SchemaEntry entry)
                => _inner.TryGetSchema(name, encoding, out entry);

            public void Register(SchemaEntry entry)
            {
                RegisterCalls++;
                _inner.Register(entry);
            }
        }
    }
}
