// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Runtime registry for generated FoxRun schema metadata.

using System;
using System.Collections.Generic;
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
                    if (contract == null
                        || !string.Equals(contract.Encoding, "json", StringComparison.Ordinal)
                        || string.IsNullOrWhiteSpace(contract.SchemaName)
                        || !IsGeneratedAggregateContract(contract))
                    {
                        continue;
                    }

                    try
                    {
                        registry.Register(new SchemaEntry
                        {
                            Name = contract.SchemaName,
                            Encoding = FoxgloveSchemaDefinitions.JsonSchemaEncoding,
                            Content = GetOrBuildGeneratedSchema(contract)
                        });
                    }
                    catch (Exception ex) when (IsRecoverableSchemaException(ex))
                    {
                        var message =
                            "Failed to register generated FoxRun JSON schema for topic '"
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
