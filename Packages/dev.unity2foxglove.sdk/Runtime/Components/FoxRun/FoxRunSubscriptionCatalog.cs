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
            FoxRunEncoding publishDefault,
            FoxRunEncoding subscriptionDefault,
            int subscriptionRateLimitHz,
            string requestedTopic,
            bool includeDescriptor)
            => BuildResponse(
                manifest,
                subscriptionsEnabled,
                publishDefault,
                subscriptionDefault,
                FoxRunEndpoint.Foxglove,
                subscriptionRateLimitHz,
                requestedTopic,
                includeDescriptor);

        public static JObject BuildResponse(
            FoxRunSchemaManifestInfo manifest,
            bool subscriptionsEnabled,
            FoxRunEncoding publishDefault,
            FoxRunEncoding subscriptionDefault,
            FoxRunEndpoint defaultProvider,
            int subscriptionRateLimitHz,
            string requestedTopic,
            bool includeDescriptor)
        {
            publishDefault = FoxRunEncodingResolver.ValidateProfileDefault(publishDefault);
            subscriptionDefault = FoxRunEncodingResolver.ValidateProfileDefault(subscriptionDefault);
            defaultProvider = FoxRunEndpointResolver.ValidateProfileSource(defaultProvider);

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

            foreach (var entry in EnumerateContracts(manifest, publishDefault, subscriptionDefault, defaultProvider)
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
                    ["flow"] = contract.Flow,
                    ["encoding"] = FoxRunEncodingResolver.ToProtocolEncoding(entry.EffectiveEncoding),
                    ["schemaName"] = contract.WireSchemaName,
                    ["wireSchemaName"] = contract.WireSchemaName,
                    ["logicalSchemaName"] = string.IsNullOrWhiteSpace(contract.LogicalSchemaName)
                        ? contract.DeclaringType
                        : contract.LogicalSchemaName,
                    ["subscribeAvailable"] = contract.SubscribeAvailable,
                    ["unavailableDiagnosticId"] = contract.SubscribeUnavailableDiagnosticId,
                    ["unavailableReason"] = contract.SubscribeUnavailableReason,
                    ["hz"] = contract.Hz,
                    ["isStream"] = entry.IsStream,
                    ["writableFieldCount"] = contract.Fields?.Count ?? 0,
                    ["protobufDescriptorAvailable"] = descriptor.Length > 0,
                    ["protobufDescriptorDigest"] = descriptor.Length > 0 ? ComputeSha256Hex(descriptor) : string.Empty
                };
                if (hasDetail)
                    objectValue["fields"] = BuildFields(contract.Fields);

                if (includeDescriptor
                    && hasDetail
                    && entry.EffectiveEncoding == FoxRunEncoding.Protobuf
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
            FoxRunEncoding publishDefault,
            FoxRunEncoding subscriptionDefault,
            FoxRunEndpoint defaultProvider)
        {
            foreach (var type in manifest.Types ?? Array.Empty<FoxRunSchemaTypeInfo>())
            {
                if (type == null)
                    continue;

                foreach (var group in type.Contracts
                             .Where(contract => contract != null
                                                && IsSubscriptionFlow(contract.Flow)
                                                && IsWebSocketEncoding(contract.Encoding))
                             .GroupBy(contract => contract.Topic, StringComparer.Ordinal))
                {
                    var variants = group.ToArray();
                    var declared = ResolveDeclaredEncoding(variants);
                    if (!TryResolveToWebSocket(
                            manifest,
                            variants,
                            FoxRunFlow.Subscribe,
                            declared,
                            defaultProvider,
                            out var isStream))
                    {
                        continue;
                    }
                    var effective = FoxRunEncodingResolver.Resolve(declared, subscriptionDefault);
                    var protocolEncoding = FoxRunEncodingResolver.ToProtocolEncoding(effective);
                    var selected = variants.FirstOrDefault(contract =>
                        string.Equals(contract.Encoding, protocolEncoding, StringComparison.Ordinal));
                    if (selected == null)
                        continue;
                    yield return new CatalogContract(selected, effective, isStream);
                }
            }
        }

        private static bool TryResolveToWebSocket(
            FoxRunSchemaManifestInfo manifest,
            IReadOnlyList<FoxRunSchemaContractInfo> contracts,
            FoxRunFlow mode,
            FoxRunEncoding declaredEncoding,
            FoxRunEndpoint defaultProvider,
            out bool isStream)
        {
            isStream = false;
            if (contracts == null || contracts.Count == 0)
                return false;
            var contract = contracts[0];
            var bindings = (manifest.SubscriptionBindings
                            ?? Array.Empty<FoxRunSchemaSubscriptionBindingInfo>())
                .Where(binding => binding != null
                                  && string.Equals(binding.DeclaringType, contract.DeclaringType, StringComparison.Ordinal)
                                  && string.Equals(binding.Topic, contract.Topic, StringComparison.Ordinal)
                                  && BindingSupportsDirection(binding.Flow, mode))
                .OrderBy(binding => binding.MemberName, StringComparer.Ordinal)
                .ToArray();
            if (bindings.Length == 0)
                return false;

            if (manifest.ManifestVersion < 3
                || !BindingIdentityMatchesContracts(contracts, bindings))
                return false;

            foreach (var binding in bindings)
            {
                var resolution = FoxRunEndpointResolver.Resolve(
                    mode,
                    binding.DeclaredSource,
                    hasExplicitSource: binding.DeclaredSource != 0,
                    declaredTargets: 0,
                    hasExplicitTargets: false,
                    declaredEncoding,
                    hasExplicitEncoding: declaredEncoding != 0,
                    defaultProvider,
                    defaultTargets: FoxRunEndpoint.Foxglove,
                    publishDefaultEncoding: FoxRunEncoding.Protobuf,
                    subscribeDefaultEncoding: FoxRunEncoding.Protobuf);
                if (!resolution.Success
                    || resolution.Topology.Source != FoxRunEndpoint.Foxglove
                    || !binding.SupportsWebSocket)
                {
                    return false;
                }
            }
            isStream = bindings.Any(binding => binding.IsStream);
            return true;
        }

        private static bool BindingIdentityMatchesContracts(
            IReadOnlyList<FoxRunSchemaContractInfo> contracts,
            IReadOnlyList<FoxRunSchemaSubscriptionBindingInfo> bindings)
        {
            var expectedMembers = contracts
                .Where(contract => contract != null)
                .SelectMany(contract => contract.Fields ?? Array.Empty<FoxRunSchemaFieldInfo>())
                .Where(field => field != null && !string.IsNullOrEmpty(field.MemberName))
                .Select(field => field.MemberName)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(memberName => memberName, StringComparer.Ordinal)
                .ToArray();
            var actualMembers = bindings
                .Where(binding => binding != null && !string.IsNullOrEmpty(binding.MemberName))
                .Select(binding => binding.MemberName)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(memberName => memberName, StringComparer.Ordinal)
                .ToArray();

            return expectedMembers.Length > 0
                   && actualMembers.Length == bindings.Count
                   && expectedMembers.SequenceEqual(actualMembers, StringComparer.Ordinal);
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
                    ["protobufFieldNumber"] = field.ProtobufFieldNumber,
                    ["typeShape"] = BuildTypeShape(field.TypeShape),
                    ["normalizedSchedule"] = BuildNormalizedSchedule(field.NormalizedSchedule)
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

        private static FoxRunEncoding ResolveDeclaredEncoding(IReadOnlyList<FoxRunSchemaContractInfo> variants)
        {
            var concrete = variants
                .Select(contract => FoxRunEncodingResolver.FromProtocolEncoding(contract.Encoding))
                .Distinct()
                .ToArray();
            return concrete.Length == 1 ? concrete[0] : (FoxRunEncoding)0;
        }

        private static bool IsSubscriptionFlow(string flow)
            => string.Equals(flow, "Subscribe", StringComparison.Ordinal)
               || string.Equals(flow, "PublishAndSubscribe", StringComparison.Ordinal);

        private static bool BindingSupportsDirection(string flow, FoxRunFlow direction)
        {
            if (direction == FoxRunFlow.Subscribe)
            {
                return string.Equals(flow, "Subscribe", StringComparison.Ordinal)
                       || string.Equals(flow, "PublishAndSubscribe", StringComparison.Ordinal);
            }

            if (direction == FoxRunFlow.Publish)
            {
                return string.Equals(flow, "Publish", StringComparison.Ordinal)
                       || string.Equals(flow, "PublishAndSubscribe", StringComparison.Ordinal);
            }

            return string.Equals(flow, "PublishAndSubscribe", StringComparison.Ordinal);
        }

        private static bool IsWebSocketEncoding(string encoding)
            => string.Equals(encoding, "json", StringComparison.Ordinal)
               || string.Equals(encoding, "protobuf", StringComparison.Ordinal)
               || string.Equals(encoding, "msgpack", StringComparison.Ordinal);

        private static JToken BuildTypeShape(FoxRunTypeShapeInfo shape)
        {
            if (shape == null)
                return JValue.CreateNull();

            var fields = new JArray();
            foreach (var field in shape.Fields)
            {
                fields.Add(new JObject
                {
                    ["jsonName"] = field.JsonName,
                    ["memberName"] = field.MemberName,
                    ["repeated"] = field.Repeated,
                    ["collectionKind"] = field.RepeatedCollectionKind.ToString(),
                    ["canAssign"] = field.CanAssign,
                    ["nullable"] = field.Nullable,
                    ["typeShape"] = BuildTypeShape(field.TypeShape)
                });
            }

            var enumValues = new JArray();
            foreach (var value in shape.EnumValues)
            {
                enumValues.Add(new JObject
                {
                    ["name"] = value.Name,
                    ["number"] = value.Number
                });
            }

            return new JObject
            {
                ["kind"] = shape.Kind.ToString(),
                ["typeName"] = shape.TypeName,
                ["canonicalType"] = shape.CanonicalType,
                ["nullable"] = shape.Nullable,
                ["collectionKind"] = shape.CollectionKind.ToString(),
                ["binary"] = shape.IsBinary,
                ["canConstruct"] = shape.CanConstruct,
                ["elementShape"] = BuildTypeShape(shape.ElementShape),
                ["fields"] = fields,
                ["enumValues"] = enumValues
            };
        }

        private static JToken BuildNormalizedSchedule(FoxRunNormalizedScheduleInfo schedule)
        {
            if (schedule == null)
                return JValue.CreateNull();

            return new JObject
            {
                ["policy"] = schedule.Policy,
                ["hasExplicitHz"] = schedule.HasExplicitHz,
                ["hz"] = schedule.Hz,
                ["tolerance"] = schedule.Tolerance,
                ["onlyIf"] = schedule.OnlyIf,
                ["conditionMemberKind"] = schedule.ConditionMemberKind
            };
        }

        private readonly struct CatalogContract
        {
            public CatalogContract(
                FoxRunSchemaContractInfo contract,
                FoxRunEncoding effectiveEncoding,
                bool isStream)
            {
                Contract = contract;
                EffectiveEncoding = effectiveEncoding;
                IsStream = isStream;
            }

            public FoxRunSchemaContractInfo Contract { get; }
            public FoxRunEncoding EffectiveEncoding { get; }
            public bool IsStream { get; }
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
                ["protobufFieldNumber"] = TypeSchema("integer"),
                ["typeShape"] = TypeSchema("object"),
                ["normalizedSchedule"] = TypeSchema("object")
            };
            var contracts = new JObject
            {
                ["declaringType"] = TypeSchema("string"),
                ["topic"] = TypeSchema("string"),
                ["flow"] = TypeSchema("string"),
                ["encoding"] = TypeSchema("string"),
                ["schemaName"] = TypeSchema("string"),
                ["wireSchemaName"] = TypeSchema("string"),
                ["logicalSchemaName"] = TypeSchema("string"),
                ["subscribeAvailable"] = TypeSchema("boolean"),
                ["unavailableDiagnosticId"] = TypeSchema("string"),
                ["unavailableReason"] = TypeSchema("string"),
                ["hz"] = TypeSchema("number"),
                ["isStream"] = TypeSchema("boolean"),
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
