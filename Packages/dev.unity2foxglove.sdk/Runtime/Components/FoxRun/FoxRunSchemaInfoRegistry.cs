// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Runtime registry for generated FoxRun schema metadata.

using System;
using System.Collections.Generic;
using System.Linq;
using Unity.FoxgloveSDK.Schemas;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>Holds the generated FoxRun manifest snapshot for runtime evidence consumers.</summary>
    public static class FoxRunSchemaInfoRegistry
    {
        private static readonly object Sync = new();
        private static FoxRunSchemaManifestInfo _current;
        private static bool _hasConflict;
        private static string _conflictMessage = string.Empty;
        private static string _conflictingHash = string.Empty;
        private static readonly Dictionary<string, string> GeneratedSchemaCache = new Dictionary<string, string>(StringComparer.Ordinal);

        public static event Action<string, Exception> GeneratedSchemaRegistrationFailed;

        public static bool HasGeneratedSchemaInfo { get { lock (Sync) return _current != null; } }
        public static bool HasConflict { get { lock (Sync) return _hasConflict; } }
        public static string ConflictMessage { get { lock (Sync) return _conflictMessage; } }
        public static string ConflictingHash { get { lock (Sync) return _conflictingHash; } }
        public static FoxRunSchemaManifestInfo Current { get { lock (Sync) return _current; } }

        /// <summary>Builds Inspector-friendly effective topic contracts from generated metadata.</summary>
        public static IReadOnlyList<FoxRunTopicSummary> GetTopicSummaries(FoxRunWireEncoding managerDefault)
        {
            managerDefault = FoxRunWireEncodingResolver.ValidateManagerDefault(managerDefault);
            lock (Sync)
            {
                if (_current == null)
                    return Array.Empty<FoxRunTopicSummary>();

                var summaries = new List<FoxRunTopicSummary>();
                foreach (var type in _current.Types)
                {
                    if (type == null)
                        continue;

                    foreach (var group in type.Contracts
                                 .Where(contract => contract != null)
                                 .GroupBy(contract => new ContractKey(contract.Topic, contract.FlowMode)))
                    {
                        var contracts = group.ToList();
                        var hasJson = contracts.Any(contract => string.Equals(contract.Encoding, "json", StringComparison.Ordinal));
                        var hasProtobuf = contracts.Any(contract => string.Equals(contract.Encoding, "protobuf", StringComparison.Ordinal));
                        var declared = hasJson && hasProtobuf
                            ? FoxRunWireEncoding.Inherit
                            : hasProtobuf ? FoxRunWireEncoding.Protobuf : FoxRunWireEncoding.Json;
                        var effective = FoxRunWireEncodingResolver.Resolve(declared, managerDefault);
                        var protocolEncoding = FoxRunWireEncodingResolver.ToProtocolEncoding(effective);
                        var contract = contracts.FirstOrDefault(candidate =>
                            string.Equals(candidate.Encoding, protocolEncoding, StringComparison.Ordinal)) ?? contracts[0];
                        summaries.Add(new FoxRunTopicSummary(
                            type.DeclaringType,
                            contract.Topic,
                            contract.FlowMode,
                            declared,
                            effective,
                            contract.SchemaName));
                    }
                }

                return summaries
                    .OrderBy(summary => summary.Topic, StringComparer.Ordinal)
                    .ThenBy(summary => summary.DeclaringType, StringComparer.Ordinal)
                    .ToArray();
            }
        }

#if UNITY_5_3_OR_NEWER
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForRuntimeLoad()
        {
            ResetState();
        }
#endif

        public static void RegisterGenerated(FoxRunSchemaManifestInfo manifest)
        {
            if (manifest == null)
                throw new ArgumentNullException(nameof(manifest));

            lock (Sync)
            {
                if (_current == null)
                {
                    _current = manifest;
                    return;
                }

                if (string.Equals(_current.GlobalManifestHash, manifest.GlobalManifestHash, StringComparison.Ordinal))
                    return;

                _hasConflict = true;
                _conflictingHash = manifest.GlobalManifestHash ?? string.Empty;
                _conflictMessage =
                    "A generated FoxRun schema info snapshot with a different manifest hash attempted to register. " +
                    "The first snapshot remains active.";
            }
        }

        /// <summary>Registers generated FoxRun JSON schemas into a runtime schema registry.</summary>
        public static void RegisterGeneratedSchemas(ISchemaRegistry registry)
        {
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));

            FoxRunSchemaManifestInfo current;
            lock (Sync)
            {
                current = _current;
            }

            if (current == null)
                return;

            foreach (var type in current.Types)
            {
                if (type == null)
                    continue;

                foreach (var contract in type.Contracts)
                {
                    if (contract == null || string.IsNullOrWhiteSpace(contract.SchemaName))
                    {
                        continue;
                    }

                    try
                    {
                        if (string.Equals(contract.Encoding, "json", StringComparison.Ordinal))
                        {
                            if (!IsGeneratedAggregateContract(contract))
                                continue;

                            registry.Register(new SchemaEntry
                            {
                                Name = contract.SchemaName,
                                Encoding = FoxgloveSchemaDefinitions.JsonSchemaEncoding,
                                Content = GetOrBuildGeneratedSchema(contract)
                            });
                        }
                        else if (string.Equals(contract.Encoding, "protobuf", StringComparison.Ordinal)
                                 && contract.ProtobufDescriptorSet.Length > 0)
                        {
                            registry.Register(new SchemaEntry
                            {
                                Name = contract.SchemaName,
                                Encoding = "protobuf",
                                Content = Convert.ToBase64String(contract.ProtobufDescriptorSet),
                                RawContent = contract.ProtobufDescriptorSet
                            });
                        }
                    }
                    catch (Exception ex) when (IsRecoverableSchemaException(ex))
                    {
                        var message =
                            "Failed to register generated FoxRun " + (contract.Encoding ?? string.Empty) + " schema for topic '"
                            + (contract.Topic ?? string.Empty)
                            + "' and schema '"
                            + (contract.SchemaName ?? string.Empty)
                            + "': "
                            + ex.Message;
                        GeneratedSchemaRegistrationFailed?.Invoke(message, ex);
#if UNITY_5_3_OR_NEWER
                        UnityEngine.Debug.LogWarning("[FoxRun] " + message);
#endif
                    }
                }
            }
        }

        private static bool IsRecoverableSchemaException(Exception ex)
        {
            return !(ex is OutOfMemoryException)
                   && !(ex is StackOverflowException)
                   && !(ex is AccessViolationException)
                   && !(ex is AppDomainUnloadedException);
        }

        private static string GetOrBuildGeneratedSchema(FoxRunSchemaContractInfo contract)
        {
            var key = !string.IsNullOrEmpty(contract.ContractHash)
                ? contract.ContractHash
                : contract.SchemaName ?? string.Empty;

            lock (Sync)
            {
                if (GeneratedSchemaCache.TryGetValue(key, out var schema))
                    return schema;
            }

            var built = FoxRunJsonSchemaBuilder.Build(contract);

            lock (Sync)
            {
                if (GeneratedSchemaCache.TryGetValue(key, out var schema))
                    return schema;

                GeneratedSchemaCache[key] = built;
                return built;
            }
        }

        private readonly struct ContractKey : IEquatable<ContractKey>
        {
            public ContractKey(string topic, string flowMode)
            {
                Topic = topic ?? string.Empty;
                FlowMode = flowMode ?? string.Empty;
            }

            private string Topic { get; }
            private string FlowMode { get; }

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

        private static bool IsGeneratedAggregateContract(FoxRunSchemaContractInfo contract)
        {
            if (contract.Fields == null || contract.Fields.Count == 0)
                return false;

            foreach (var field in contract.Fields)
            {
                if (field == null || !field.Aggregate)
                    return false;
            }

            return true;
        }

        /// <summary>Clears generated registry state for validation tests.</summary>
        internal static void ClearForTests()
        {
            ResetState();
        }

        private static void ResetState()
        {
            lock (Sync)
            {
                _current = null;
                _hasConflict = false;
                _conflictMessage = string.Empty;
                _conflictingHash = string.Empty;
                GeneratedSchemaCache.Clear();
                GeneratedSchemaRegistrationFailed = null;
            }
        }
    }
}
