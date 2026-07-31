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

        internal static IReadOnlyList<string>
            GetExplicitPublishTransportIds()
        {
            lock (Sync)
            {
                if (_current == null)
                    return Array.Empty<string>();

                return _current.Types
                    .Where(type => type != null)
                    .SelectMany(type => type.Contracts)
                    .Where(contract =>
                        contract != null
                        && FlowSupports(
                            contract.Flow,
                            FoxRunFlow.Publish)
                        && contract.PublishTransportIds != null)
                    .SelectMany(
                        contract => contract.PublishTransportIds)
                    .Where(
                        id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(id => id, StringComparer.Ordinal)
                    .ToArray();
            }
        }

        /// <summary>Builds Inspector-friendly effective topic contracts from generated metadata.</summary>
        public static IReadOnlyList<FoxRunTopicSummary> GetTopicSummaries(FoxRunEncoding managerDefault)
            => GetTopicSummaries(managerDefault, managerDefault);

        /// <summary>
        /// Builds Inspector-friendly effective topic contracts from generated metadata using
        /// independent defaults for Unity output and client subscriptions.
        /// </summary>
        public static IReadOnlyList<FoxRunTopicSummary> GetTopicSummaries(
            FoxRunEncoding publishDefault,
            FoxRunEncoding subscriptionDefault)
        {
            publishDefault = FoxRunEncodingResolver.ValidateProfileDefault(publishDefault);
            subscriptionDefault = FoxRunEncodingResolver.ValidateProfileDefault(subscriptionDefault);
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
                                 .Where(contract => contract != null
                                                    && IsWebSocketEncoding(contract.Encoding))
                                 .GroupBy(contract => contract.Topic, StringComparer.Ordinal))
                    {
                        var contracts = group.ToList();
                        var publishContracts = contracts
                            .Where(contract => FlowSupports(contract.Flow, FoxRunFlow.Publish))
                            .ToList();
                        if (publishContracts.Count > 0)
                        {
                            AppendDirectionalSummary(
                                summaries,
                                type.DeclaringType,
                                publishContracts,
                                ResolveDeclaredEncoding(publishContracts),
                                FoxRunFlow.Publish,
                                publishDefault);
                        }

                        var subscribeContracts = contracts
                            .Where(contract => FlowSupports(contract.Flow, FoxRunFlow.Subscribe))
                            .ToList();
                        if (subscribeContracts.Count > 0)
                        {
                            AppendDirectionalSummary(
                                summaries,
                                type.DeclaringType,
                                subscribeContracts,
                                ResolveDeclaredEncoding(subscribeContracts),
                                FoxRunFlow.Subscribe,
                                subscriptionDefault);
                        }
                    }
                }

                return summaries
                    .OrderBy(summary => summary.Topic, StringComparer.Ordinal)
                    .ThenBy(summary => summary.DeclaringType, StringComparer.Ordinal)
                    .ThenBy(summary => summary.Direction, StringComparer.Ordinal)
                    .ToArray();
            }
        }

        /// <summary>
        /// Resolves the generated contract selected by one active MessagePack
        /// session direction. This is a registration gate, not a display-only
        /// summary lookup: absent, ambiguous, or unavailable metadata fails
        /// closed before a codec or route can be registered.
        /// </summary>
        internal static bool TryResolveSessionContract(
            string declaringType,
            string topic,
            FoxRunFlow direction,
            FoxRunEncoding selectedEncoding,
            out FoxRunSchemaContractInfo contract,
            out string diagnostic)
            => TryResolveSessionContract(
                string.IsNullOrEmpty(declaringType)
                    ? new HashSet<string>(StringComparer.Ordinal)
                    : new HashSet<string>(
                        new[] { NormalizeDeclaringType(declaringType) },
                        StringComparer.Ordinal),
                topic,
                direction,
                selectedEncoding,
                out contract,
                out diagnostic);

        internal static bool TryResolveSessionContract(
            Type runtimeType,
            string topic,
            FoxRunFlow direction,
            FoxRunEncoding selectedEncoding,
            out FoxRunSchemaContractInfo contract,
            out string diagnostic)
            => TryResolveSessionContract(
                RuntimeDeclaringTypes(runtimeType),
                topic,
                direction,
                selectedEncoding,
                out contract,
                out diagnostic);

        private static bool TryResolveSessionContract(
            ISet<string> declaringTypes,
            string topic,
            FoxRunFlow direction,
            FoxRunEncoding selectedEncoding,
            out FoxRunSchemaContractInfo contract,
            out string diagnostic)
        {
            contract = null;
            diagnostic = string.Empty;
            if (selectedEncoding != FoxRunEncoding.MessagePack)
                return true;

            var protocolEncoding = FoxRunEncodingResolver.ToProtocolEncoding(selectedEncoding);
            lock (Sync)
            {
                if (_current == null)
                {
                    diagnostic = MissingSessionContractDiagnostic(
                        direction,
                        topic,
                        "no generated schema metadata is registered");
                    return false;
                }

                var matches = new List<FoxRunSchemaContractInfo>();
                foreach (var type in _current.Types)
                {
                    if (type == null)
                        continue;
                    if (declaringTypes.Count > 0
                        && !declaringTypes.Contains(
                            NormalizeDeclaringType(type.DeclaringType)))
                    {
                        continue;
                    }

                    foreach (var candidate in type.Contracts)
                    {
                        if (candidate == null
                            || !string.Equals(candidate.Topic, topic, StringComparison.Ordinal)
                            || !string.Equals(candidate.Encoding, protocolEncoding, StringComparison.Ordinal)
                            || !FlowSupports(candidate.Flow, direction))
                        {
                            continue;
                        }

                        matches.Add(candidate);
                    }
                }

                if (matches.Count != 1)
                {
                    var reason = matches.Count == 0
                        ? "the selected encoding variant is absent from generated contract metadata"
                        : "generated contract metadata is ambiguous for the selected topic and direction";
                    diagnostic = MissingSessionContractDiagnostic(direction, topic, reason);
                    return false;
                }

                contract = matches[0];
                var available = direction == FoxRunFlow.Subscribe
                    ? contract.SubscribeAvailable
                    : contract.PublishAvailable;
                if (available)
                    return true;

                var directionalDiagnosticId = direction == FoxRunFlow.Subscribe
                    ? contract.SubscribeUnavailableDiagnosticId
                    : contract.PublishUnavailableDiagnosticId;
                var directionalReason = direction == FoxRunFlow.Subscribe
                    ? contract.SubscribeUnavailableReason
                    : contract.PublishUnavailableReason;
                var diagnosticId = string.IsNullOrWhiteSpace(directionalDiagnosticId)
                    ? direction == FoxRunFlow.Subscribe ? "FOXRUN618" : "FOXRUN619"
                    : directionalDiagnosticId;
                var reasonText = string.IsNullOrWhiteSpace(directionalReason)
                    ? "the generated MessagePack contract is unavailable for this direction"
                    : directionalReason;
                diagnostic = diagnosticId
                             + ": MessagePack "
                             + DirectionName(direction)
                             + " session for topic '"
                             + (topic ?? string.Empty)
                             + "' is unavailable: "
                             + reasonText
                             + ".";
                contract = null;
                return false;
            }
        }

        private static FoxRunEncoding ResolveDeclaredEncoding(
            IReadOnlyList<FoxRunSchemaContractInfo> contracts)
        {
            var concrete = contracts
                .Where(contract => contract != null)
                .Select(contract => FoxRunEncodingResolver.FromProtocolEncoding(contract.Encoding))
                .Distinct()
                .ToArray();
            return concrete.Length == 1 ? concrete[0] : (FoxRunEncoding)0;
        }

        private static HashSet<string> RuntimeDeclaringTypes(Type runtimeType)
        {
            var declaringTypes = new HashSet<string>(StringComparer.Ordinal);
            for (var current = runtimeType;
                 current != null && current != typeof(object);
                 current = current.BaseType)
            {
                declaringTypes.Add(NormalizeDeclaringType(current.FullName));
            }
            return declaringTypes;
        }

        private static bool IsWebSocketEncoding(string encoding)
            => string.Equals(encoding, "json", StringComparison.Ordinal)
               || string.Equals(encoding, "protobuf", StringComparison.Ordinal)
               || string.Equals(encoding, "msgpack", StringComparison.Ordinal);

        private static void AppendDirectionalSummary(
            ICollection<FoxRunTopicSummary> summaries,
            string declaringType,
            IReadOnlyList<FoxRunSchemaContractInfo> contracts,
            FoxRunEncoding declared,
            FoxRunFlow direction,
            FoxRunEncoding profileDefault)
        {
            var effective = FoxRunEncodingResolver.Resolve(declared, profileDefault);
            var protocolEncoding = FoxRunEncodingResolver.ToProtocolEncoding(effective);
            var contract = contracts.FirstOrDefault(candidate =>
                string.Equals(candidate.Encoding, protocolEncoding, StringComparison.Ordinal));
            var directionName = direction == FoxRunFlow.Publish ? "Publish" : "Subscribe";
            if (contract == null)
            {
                var first = contracts[0];
                summaries.Add(new FoxRunTopicSummary(
                    declaringType,
                    first.Topic,
                    directionName,
                    declared,
                    effective,
                    string.Empty,
                    first.LogicalSchemaName,
                    available: false,
                    unavailableDiagnosticId: "FOXRUN616",
                    unavailableReason: "The selected encoding variant is absent from generated contract metadata."));
                return;
            }

            var available = direction == FoxRunFlow.Publish
                ? contract.PublishAvailable
                : contract.SubscribeAvailable;
            var unavailableDiagnosticId = direction == FoxRunFlow.Publish
                ? contract.PublishUnavailableDiagnosticId
                : contract.SubscribeUnavailableDiagnosticId;
            var unavailableReason = direction == FoxRunFlow.Publish
                ? contract.PublishUnavailableReason
                : contract.SubscribeUnavailableReason;
            summaries.Add(new FoxRunTopicSummary(
                declaringType,
                contract.Topic,
                directionName,
                declared,
                effective,
                contract.WireSchemaName,
                contract.LogicalSchemaName,
                available,
                unavailableDiagnosticId,
                unavailableReason));
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

            var candidates = new List<GeneratedSchemaRegistrationCandidate>();
            var blockedKeys = new HashSet<GeneratedSchemaRegistrationKey>();
            foreach (var type in current.Types)
            {
                if (type == null)
                    continue;

                foreach (var contract in type.Contracts)
                {
                    if (contract == null
                        || string.IsNullOrWhiteSpace(contract.SchemaName)
                        || !FlowSupports(contract.Flow, FoxRunFlow.Publish))
                    {
                        continue;
                    }

                    if (!TryGetGeneratedSchemaRegistrationKey(
                            contract,
                            out var registrationKey))
                    {
                        continue;
                    }

                    try
                    {
                        SchemaEntry entry;
                        if (string.Equals(contract.Encoding, "json", StringComparison.Ordinal))
                        {
                            entry = new SchemaEntry
                            {
                                Name = registrationKey.Name,
                                Encoding = registrationKey.Encoding,
                                Content = GetOrBuildGeneratedSchema(contract)
                            };
                        }
                        else
                        {
                            entry = new SchemaEntry
                            {
                                Name = registrationKey.Name,
                                Encoding = registrationKey.Encoding,
                                Content = Convert.ToBase64String(contract.ProtobufDescriptorSet),
                                RawContent = contract.ProtobufDescriptorSet
                            };
                        }

                        candidates.Add(new GeneratedSchemaRegistrationCandidate(
                            contract,
                            entry,
                            registrationKey));
                    }
                    catch (Exception ex) when (IsRecoverableSchemaException(ex))
                    {
                        blockedKeys.Add(registrationKey);
                        ReportGeneratedSchemaRegistrationFailure(
                            "Failed to prepare generated FoxRun " + (contract.Encoding ?? string.Empty) + " schema for topic '"
                            + (contract.Topic ?? string.Empty)
                            + "' and schema '"
                            + (contract.SchemaName ?? string.Empty)
                            + "': "
                            + ex.Message,
                            ex);
                    }
                }
            }

            foreach (var group in candidates.GroupBy(
                         candidate => candidate.RegistrationKey))
            {
                var registrations = group.ToList();
                var first = registrations[0];
                if (blockedKeys.Contains(group.Key))
                    continue;

                if (registrations.Skip(1).Any(candidate =>
                        !SchemaEntriesEqual(first.Entry, candidate.Entry)))
                {
                    var message =
                        "Refused conflicting generated FoxRun publish schemas for schema '"
                        + first.Entry.Name
                        + "' and schema encoding '"
                        + first.Entry.Encoding
                        + "'. No entry for this key was registered.";
                    ReportGeneratedSchemaRegistrationFailure(
                        message,
                        new InvalidOperationException(message));
                    continue;
                }

                try
                {
                    registry.Register(first.Entry);
                }
                catch (Exception ex) when (IsRecoverableSchemaException(ex))
                {
                    ReportGeneratedSchemaRegistrationFailure(
                        "Failed to register generated FoxRun "
                        + (first.Contract.Encoding ?? string.Empty)
                        + " schema for topic '"
                        + (first.Contract.Topic ?? string.Empty)
                        + "' and schema '"
                        + (first.Contract.SchemaName ?? string.Empty)
                        + "': "
                        + ex.Message,
                        ex);
                }
            }
        }

        private static bool SchemaEntriesEqual(SchemaEntry left, SchemaEntry right)
            => string.Equals(left.Name, right.Name, StringComparison.Ordinal)
               && string.Equals(left.Encoding, right.Encoding, StringComparison.Ordinal)
               && string.Equals(left.Content, right.Content, StringComparison.Ordinal)
               && (left.RawContent ?? Array.Empty<byte>())
               .SequenceEqual(right.RawContent ?? Array.Empty<byte>());

        private static void ReportGeneratedSchemaRegistrationFailure(
            string message,
            Exception error)
        {
            GeneratedSchemaRegistrationFailed?.Invoke(message, error);
#if UNITY_5_3_OR_NEWER
            UnityEngine.Debug.LogWarning("[FoxRun] " + message);
#endif
        }

        private static bool IsRecoverableSchemaException(Exception ex)
        {
            return !(ex is OutOfMemoryException)
                   && !(ex is StackOverflowException)
                   && !(ex is AccessViolationException)
                   && !(ex is AppDomainUnloadedException);
        }

        private sealed class GeneratedSchemaRegistrationCandidate
        {
            public GeneratedSchemaRegistrationCandidate(
                FoxRunSchemaContractInfo contract,
                SchemaEntry entry,
                GeneratedSchemaRegistrationKey registrationKey)
            {
                Contract = contract;
                Entry = entry;
                RegistrationKey = registrationKey;
            }

            public FoxRunSchemaContractInfo Contract { get; }
            public SchemaEntry Entry { get; }
            public GeneratedSchemaRegistrationKey RegistrationKey { get; }
        }

        private readonly struct GeneratedSchemaRegistrationKey :
            IEquatable<GeneratedSchemaRegistrationKey>
        {
            public GeneratedSchemaRegistrationKey(string name, string encoding)
            {
                Name = name ?? string.Empty;
                Encoding = encoding ?? string.Empty;
            }

            public string Name { get; }
            public string Encoding { get; }

            public bool Equals(GeneratedSchemaRegistrationKey other)
                => string.Equals(Name, other.Name, StringComparison.Ordinal)
                   && string.Equals(Encoding, other.Encoding, StringComparison.Ordinal);

            public override bool Equals(object obj)
                => obj is GeneratedSchemaRegistrationKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    return (StringComparer.Ordinal.GetHashCode(Name) * 397)
                           ^ StringComparer.Ordinal.GetHashCode(Encoding);
                }
            }
        }

        private static bool TryGetGeneratedSchemaRegistrationKey(
            FoxRunSchemaContractInfo contract,
            out GeneratedSchemaRegistrationKey key)
        {
            key = default;
            if (string.Equals(contract.Encoding, "json", StringComparison.Ordinal))
            {
                if (!IsGeneratedAggregateContract(contract))
                    return false;

                key = new GeneratedSchemaRegistrationKey(
                    contract.SchemaName,
                    FoxgloveSchemaDefinitions.JsonSchemaEncoding);
                return true;
            }

            if (string.Equals(contract.Encoding, "protobuf", StringComparison.Ordinal)
                && contract.ProtobufDescriptorSet.Length > 0)
            {
                key = new GeneratedSchemaRegistrationKey(
                    contract.SchemaName,
                    "protobuf");
                return true;
            }

            return false;
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

        private static bool FlowSupports(string flow, FoxRunFlow direction)
        {
            if (direction == FoxRunFlow.Publish)
            {
                return string.Equals(flow, "Publish", StringComparison.Ordinal)
                       || string.Equals(flow, "PublishAndSubscribe", StringComparison.Ordinal);
            }

            if (direction == FoxRunFlow.Subscribe)
            {
                return string.Equals(flow, "Subscribe", StringComparison.Ordinal)
                       || string.Equals(flow, "PublishAndSubscribe", StringComparison.Ordinal);
            }

            return false;
        }

        private static string NormalizeDeclaringType(string declaringType)
            => (declaringType ?? string.Empty).Replace('+', '.');

        private static string DirectionName(FoxRunFlow direction)
            => direction == FoxRunFlow.Subscribe ? "Subscribe" : "Publish";

        private static string MissingSessionContractDiagnostic(
            FoxRunFlow direction,
            string topic,
            string reason)
            => "FOXRUN616: MessagePack "
               + DirectionName(direction)
               + " session for topic '"
               + (topic ?? string.Empty)
               + "' cannot start because "
               + reason
               + ".";

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
