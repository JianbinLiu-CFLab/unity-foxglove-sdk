// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Safe, deterministic contract catalog for FoxRun subscription clients.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>Builds the data-only catalog consumed by the FoxRun Publish panel.</summary>
    public static class FoxRunSubscriptionCatalog
    {
        public const int Version = 1;

        /// <summary>
        /// Builds a catalog response without client identifiers, tokens, or queue state.
        /// Protobuf descriptors are emitted only for a requested topic.
        /// </summary>
        public static JObject BuildResponse(
            FoxRunSchemaManifestInfo manifest,
            bool subscriptionsEnabled,
            FoxRunWireEncoding publishDefault,
            FoxRunWireEncoding subscriptionDefault,
            int subscriptionRateLimitHz,
            string requestedTopic,
            bool includeDescriptor)
        {
            publishDefault = FoxRunWireEncodingResolver.ValidateManagerDefault(publishDefault);
            subscriptionDefault = FoxRunWireEncodingResolver.ValidateManagerDefault(subscriptionDefault);

            var contracts = new JArray();
            var response = new JObject
            {
                ["version"] = Version,
                ["subscriptionsEnabled"] = subscriptionsEnabled,
                ["subscriptionRateLimitHz"] = Math.Max(1, subscriptionRateLimitHz),
                ["contracts"] = contracts
            };
            if (!subscriptionsEnabled || manifest == null)
                return response;

            foreach (var entry in EnumerateContracts(manifest, publishDefault, subscriptionDefault)
                         .Where(entry => string.IsNullOrEmpty(requestedTopic)
                                         || string.Equals(entry.Contract.Topic, requestedTopic, StringComparison.Ordinal))
                         .OrderBy(entry => entry.Contract.Topic, StringComparer.Ordinal)
                         .ThenBy(entry => entry.Contract.DeclaringType, StringComparer.Ordinal))
            {
                var contract = entry.Contract;
                var hasDetail = !string.IsNullOrEmpty(requestedTopic);
                var descriptor = contract.ProtobufDescriptorSet;
                var objectValue = new JObject
                {
                    ["declaringType"] = contract.DeclaringType,
                    ["topic"] = contract.Topic,
                    ["flowMode"] = contract.FlowMode,
                    ["encoding"] = FoxRunWireEncodingResolver.ToProtocolEncoding(entry.EffectiveEncoding),
                    ["schemaName"] = contract.SchemaName,
                    ["rateHz"] = contract.RateHz,
                    ["writableFieldCount"] = contract.Fields?.Count ?? 0,
                    ["protobufDescriptorAvailable"] = descriptor.Length > 0,
                    ["protobufDescriptorDigest"] = descriptor.Length > 0 ? ComputeSha256Hex(descriptor) : string.Empty
                };
                if (hasDetail)
                    objectValue["fields"] = BuildFields(contract.Fields);

                if (includeDescriptor
                    && hasDetail
                    && entry.EffectiveEncoding == FoxRunWireEncoding.Protobuf
                    && descriptor.Length > 0)
                {
                    objectValue["protobufDescriptorBase64"] = Convert.ToBase64String(descriptor);
                }

                contracts.Add(objectValue);
            }

            return response;
        }

        private static IEnumerable<CatalogContract> EnumerateContracts(
            FoxRunSchemaManifestInfo manifest,
            FoxRunWireEncoding publishDefault,
            FoxRunWireEncoding subscriptionDefault)
        {
            foreach (var type in manifest.Types ?? Array.Empty<FoxRunSchemaTypeInfo>())
            {
                if (type == null)
                    continue;

                foreach (var group in type.Contracts
                             .Where(contract => contract != null
                                                && IsSubscriptionFlow(contract.FlowMode)
                                                && IsWebSocketEncoding(contract.Encoding))
                             .GroupBy(contract => new ContractKey(contract.Topic, contract.FlowMode)))
                {
                    var variants = group.ToArray();
                    var declared = ResolveDeclaredEncoding(variants);
                    var mode = ParseFlowMode(group.Key.FlowMode);
                    var effective = FoxRunWireEncodingResolver.Resolve(
                        declared,
                        mode,
                        publishDefault,
                        subscriptionDefault);
                    var protocolEncoding = FoxRunWireEncodingResolver.ToProtocolEncoding(effective);
                    var selected = variants.FirstOrDefault(contract =>
                        string.Equals(contract.Encoding, protocolEncoding, StringComparison.Ordinal)) ?? variants[0];
                    yield return new CatalogContract(selected, effective);
                }
            }
        }

        private static JArray BuildFields(IReadOnlyList<FoxRunSchemaFieldInfo> fields)
        {
            var values = new JArray();
            foreach (var field in fields ?? Array.Empty<FoxRunSchemaFieldInfo>())
            {
                if (field == null)
                    continue;

                values.Add(new JObject
                {
                    ["name"] = field.JsonName,
                    ["type"] = field.Type,
                    ["nullable"] = field.Nullable,
                    ["array"] = field.Array,
                    ["protobufFieldNumber"] = field.ProtobufFieldNumber
                });
            }
            return values;
        }

        private static string ComputeSha256Hex(byte[] bytes)
        {
            using (var sha256 = SHA256.Create())
            {
                var digest = sha256.ComputeHash(bytes);
                return BitConverter.ToString(digest).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private static FoxRunWireEncoding ResolveDeclaredEncoding(IReadOnlyList<FoxRunSchemaContractInfo> variants)
        {
            var hasJson = variants.Any(contract => string.Equals(contract.Encoding, "json", StringComparison.Ordinal));
            var hasProtobuf = variants.Any(contract => string.Equals(contract.Encoding, "protobuf", StringComparison.Ordinal));
            return hasJson && hasProtobuf
                ? FoxRunWireEncoding.Inherit
                : hasProtobuf ? FoxRunWireEncoding.Protobuf : FoxRunWireEncoding.Json;
        }

        private static bool IsSubscriptionFlow(string flowMode)
            => string.Equals(flowMode, "SubscribeOnly", StringComparison.Ordinal)
               || string.Equals(flowMode, "PublishAndSubscribe", StringComparison.Ordinal);

        private static bool IsWebSocketEncoding(string encoding)
            => string.Equals(encoding, "json", StringComparison.Ordinal)
               || string.Equals(encoding, "protobuf", StringComparison.Ordinal);

        private static FoxRunMode ParseFlowMode(string flowMode)
        {
            if (string.Equals(flowMode, "SubscribeOnly", StringComparison.Ordinal))
                return FoxRunMode.SubscribeOnly;
            if (string.Equals(flowMode, "PublishAndSubscribe", StringComparison.Ordinal))
                return FoxRunMode.PublishAndSubscribe;
            throw new ArgumentException("Unsupported FoxRun subscription flow mode: " + (flowMode ?? string.Empty), nameof(flowMode));
        }

        private readonly struct CatalogContract
        {
            public CatalogContract(FoxRunSchemaContractInfo contract, FoxRunWireEncoding effectiveEncoding)
            {
                Contract = contract;
                EffectiveEncoding = effectiveEncoding;
            }

            public FoxRunSchemaContractInfo Contract { get; }
            public FoxRunWireEncoding EffectiveEncoding { get; }
        }

        private readonly struct ContractKey : IEquatable<ContractKey>
        {
            public ContractKey(string topic, string flowMode)
            {
                Topic = topic ?? string.Empty;
                FlowMode = flowMode ?? string.Empty;
            }

            public string Topic { get; }
            public string FlowMode { get; }

            public bool Equals(ContractKey other)
                => string.Equals(Topic, other.Topic, StringComparison.Ordinal)
                   && string.Equals(FlowMode, other.FlowMode, StringComparison.Ordinal);

            public override bool Equals(object obj) => obj is ContractKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = StringComparer.Ordinal.GetHashCode(Topic);
                    return (hash * 397) ^ StringComparer.Ordinal.GetHashCode(FlowMode);
                }
            }
        }
    }

    // Foxglove renders a JSON service form by recursively reading object and array item types.
    // Keep the catalog schemas complete even though summary and selected-topic responses omit
    // optional fields at different times.
    internal static class FoxRunSubscriptionCatalogServiceSchemas
    {
        internal const string Request =
            "{\"type\":\"object\",\"properties\":{\"topic\":{\"type\":\"string\"},\"includeDescriptor\":{\"type\":\"boolean\"}}}";

        internal static readonly string Response = BuildResponse();

        private static string BuildResponse()
        {
            var fields = new JObject
            {
                ["name"] = TypeSchema("string"),
                ["type"] = TypeSchema("string"),
                ["nullable"] = TypeSchema("boolean"),
                ["array"] = TypeSchema("boolean"),
                ["protobufFieldNumber"] = TypeSchema("integer")
            };
            var contracts = new JObject
            {
                ["declaringType"] = TypeSchema("string"),
                ["topic"] = TypeSchema("string"),
                ["flowMode"] = TypeSchema("string"),
                ["encoding"] = TypeSchema("string"),
                ["schemaName"] = TypeSchema("string"),
                ["rateHz"] = TypeSchema("number"),
                ["writableFieldCount"] = TypeSchema("integer"),
                ["protobufDescriptorAvailable"] = TypeSchema("boolean"),
                ["protobufDescriptorDigest"] = TypeSchema("string"),
                ["fields"] = ArraySchema(fields),
                ["protobufDescriptorBase64"] = TypeSchema("string")
            };
            var response = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["version"] = TypeSchema("integer"),
                    ["subscriptionsEnabled"] = TypeSchema("boolean"),
                    ["subscriptionRateLimitHz"] = TypeSchema("integer"),
                    ["contracts"] = ArraySchema(contracts)
                }
            };
            return response.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static JObject TypeSchema(string type)
        {
            return new JObject { ["type"] = type };
        }

        private static JObject ArraySchema(JObject itemProperties)
        {
            return new JObject
            {
                ["type"] = "array",
                ["items"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = itemProperties
                }
            };
        }
    }
}
