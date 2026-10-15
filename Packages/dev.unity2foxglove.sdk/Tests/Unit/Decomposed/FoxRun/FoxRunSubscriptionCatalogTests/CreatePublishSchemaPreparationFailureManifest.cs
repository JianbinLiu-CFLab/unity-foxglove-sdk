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
