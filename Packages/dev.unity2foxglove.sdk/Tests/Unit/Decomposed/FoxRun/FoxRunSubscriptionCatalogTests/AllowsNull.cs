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
        private static bool AllowsNull(JToken schema)
        {
            var oneOf = schema?["oneOf"] as JArray;
            return oneOf != null
                   && oneOf.Any(option =>
                       string.Equals(
                           option?["type"]?.Value<string>(),
                           "null",
                           StringComparison.Ordinal));
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

    }
}
