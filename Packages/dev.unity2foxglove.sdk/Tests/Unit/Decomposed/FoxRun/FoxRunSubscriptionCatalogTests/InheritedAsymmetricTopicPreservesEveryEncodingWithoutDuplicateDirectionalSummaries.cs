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
        [Trait("Phase", "187-R2-E05")]
        public void ServiceResponseSchemaAcceptsNullableOptionalCatalogValues()
        {
            var type = typeof(FoxRunSubscriptionCatalog).Assembly.GetType(
                "Unity.FoxgloveSDK.Components.FoxRunSubscriptionCatalogServiceSchemas");
            Assert.NotNull(type);
            var responseField = type!.GetField("Response", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(responseField);

            var schema = JObject.Parse((string)responseField!.GetValue(null)!);
            var contractProperties = (JObject)schema["properties"]!["contracts"]!["items"]!["properties"]!;
            var fieldProperties = (JObject)contractProperties["fields"]!["items"]!["properties"]!;

            Assert.True(AllowsNull(contractProperties["publishTransportIds"]),
                "publishTransportIds schema must accept the null emitted for inherited publish destinations");
            Assert.True(AllowsNull(contractProperties["subscribeTransportId"]),
                "subscribeTransportId schema must accept the null emitted before a Provider is selected");
            Assert.True(AllowsNull(fieldProperties["typeShape"]),
                "typeShape schema must accept the null emitted when optional shape metadata is absent");
            Assert.True(AllowsNull(fieldProperties["normalizedSchedule"]),
                "normalizedSchedule schema must accept the null emitted when optional schedule metadata is absent");

            var response = FoxRunSubscriptionCatalog.BuildResponse(
                CreateManifest(),
                subscriptionsEnabled: true,
                FoxRunEncoding.JSON,
                FoxRunEncoding.JSON,
                subscriptionRateLimitHz: 10,
                requestedTopic: "/phase176/input",
                includeDescriptor: false);
            var contract = Assert.Single((JArray)response["contracts"]!);
            var fields = Assert.Single((JArray)contract["fields"]!);
            Assert.Equal(JTokenType.Null, contract["publishTransportIds"]!.Type);
            Assert.Equal(JTokenType.Null, contract["subscribeTransportId"]!.Type);
            Assert.Equal(JTokenType.Null, fields["typeShape"]!.Type);
            Assert.Equal(JTokenType.Null, fields["normalizedSchedule"]!.Type);
        }

    }
}
