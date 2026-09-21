// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Pure compatibility classification for recorded and current FoxRun metadata.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.FoxgloveSDK.Core;

namespace Unity.FoxgloveSDK.Components
{
    public enum FoxRunCompatibilityClass
    {
        Unknown,
        Exact,
        PolicyOnlyChange,
        BindingOnlyChange,
        BackwardCompatible,
        ForwardCompatible,
        Breaking
    }

    public enum FoxRunCompatibilityDecision
    {
        Proceed,
        Warn,
        Block,
        Silent
    }

    /// <summary>Presence of a contract identity on each side of the comparison.</summary>
    public enum FoxRunCompatibilityPresence
    {
        Both,
        AddedInCurrent,
        RemovedFromCurrent
    }

    public sealed class FoxRunCompatibilityTopicFinding
    {
        public string Topic { get; }
        public string SchemaName { get; }
        public string Encoding { get; }
        public string Flow { get; }
        public FoxRunCompatibilityPresence Presence { get; }
        public bool ContractChanged { get; }
        public bool BindingChanged { get; }
        public bool PolicyChanged { get; }

        public FoxRunCompatibilityTopicFinding(
            string topic,
            bool contractChanged,
            bool bindingChanged,
            bool policyChanged)
            : this(topic, string.Empty, string.Empty, string.Empty,
                   FoxRunCompatibilityPresence.Both, contractChanged, bindingChanged, policyChanged)
        {
        }

        public FoxRunCompatibilityTopicFinding(
            string topic,
            string schemaName,
            string encoding,
            string flow,
            FoxRunCompatibilityPresence presence,
            bool contractChanged,
            bool bindingChanged,
            bool policyChanged)
        {
            Topic = topic ?? string.Empty;
            SchemaName = schemaName ?? string.Empty;
            Encoding = encoding ?? string.Empty;
            Flow = flow ?? string.Empty;
            Presence = presence;
            ContractChanged = contractChanged;
            BindingChanged = bindingChanged;
            PolicyChanged = policyChanged;
        }

        public string Describe() => Describe(qualifyIdentity: false);

        /// <summary>
        /// Describes one contract. A topic can carry several contracts, one per encoding and flow,
        /// so the caller qualifies the line with the rest of the identity whenever the topic alone
        /// would name more than one row.
        /// </summary>
        public string Describe(bool qualifyIdentity)
        {
            var label = qualifyIdentity
                ? Topic + " [" + Encoding + "/" + Flow + "]"
                : Topic;
            if (Presence == FoxRunCompatibilityPresence.AddedInCurrent)
                return label + ": added";
            if (Presence == FoxRunCompatibilityPresence.RemovedFromCurrent)
                return label + ": removed";
            return label + ": contract " + (ContractChanged ? "changed" : "same")
                   + " / binding " + (BindingChanged ? "changed" : "same")
                   + " / policy " + (PolicyChanged ? "changed" : "same");
        }
    }

    public sealed class FoxRunCompatibilityResult
    {
        public FoxRunCompatibilityClass Classification { get; }
        public IReadOnlyList<FoxRunCompatibilityTopicFinding> Findings { get; }
        public string Message { get; }

        public FoxRunCompatibilityResult(
            FoxRunCompatibilityClass classification,
            IReadOnlyList<FoxRunCompatibilityTopicFinding> findings,
            string message)
        {
            Classification = classification;
            Findings = findings ?? Array.Empty<FoxRunCompatibilityTopicFinding>();
            Message = message ?? string.Empty;
        }

        public FoxRunCompatibilityDecision Decide(SchemaIdentityMode mode)
        {
            if (mode == SchemaIdentityMode.Off)
                return FoxRunCompatibilityDecision.Silent;
            if (mode == SchemaIdentityMode.Warn)
                return Classification == FoxRunCompatibilityClass.Exact
                    ? FoxRunCompatibilityDecision.Proceed
                    : FoxRunCompatibilityDecision.Warn;
            if (mode == SchemaIdentityMode.Compatible)
            {
                return Classification == FoxRunCompatibilityClass.Exact
                    || Classification == FoxRunCompatibilityClass.PolicyOnlyChange
                    || Classification == FoxRunCompatibilityClass.BackwardCompatible
                    ? FoxRunCompatibilityDecision.Proceed
                    : FoxRunCompatibilityDecision.Block;
            }
            return Classification == FoxRunCompatibilityClass.Exact
                ? FoxRunCompatibilityDecision.Proceed
                : FoxRunCompatibilityDecision.Block;
        }
    }

    public static class FoxRunCompatibilityAnalyzer
    {
        public static FoxRunCompatibilityResult Analyze(
            FoxRunSchemaMcapMetadataRecord recorded,
            FoxRunSchemaManifestInfo current)
        {
            if (recorded == null || current == null || recorded.Contracts == null)
                return Unknown("metadata is missing or malformed");
            if (recorded.SchemaMetadataVersion != 1 && recorded.SchemaMetadataVersion != 2)
                return Unknown("metadata version is unsupported");
            if (recorded.Contracts.Any(contract => contract == null || string.IsNullOrWhiteSpace(contract.Topic)))
                return Unknown("metadata contains an invalid contract");
            // Field digests only enable field-level reasoning. An equal global manifest hash already
            // proves the contract universe is identical, so a recording made before metadata v2 is
            // still Exact; it is Unknown only when the hashes differ and no digests exist to explain
            // the difference.
            var globalHashesEqual = !string.IsNullOrWhiteSpace(recorded.GlobalManifestHash)
                && StringEquals(recorded.GlobalManifestHash, current.GlobalManifestHash);
            if (recorded.SchemaMetadataVersion == 1)
                return globalHashesEqual
                    ? Result(FoxRunCompatibilityClass.Exact, Array.Empty<FoxRunCompatibilityTopicFinding>())
                    : Unknown("schema metadata version 1 has no field digests");
            if (recorded.Contracts.Any(contract => contract.Fields == null))
                return globalHashesEqual
                    ? Result(FoxRunCompatibilityClass.Exact, Array.Empty<FoxRunCompatibilityTopicFinding>())
                    : Unknown("schema metadata field digest is missing");

            var currentContracts = current.Types
                .Where(type => type != null)
                .SelectMany(type => type.Contracts ?? Array.Empty<FoxRunSchemaContractInfo>())
                .Where(contract => contract != null)
                .Select(ToRecordedContract)
                .ToList();
            if (!TryBuildCurrentMap(currentContracts, out var currentByKey, out var currentError))
                return Unknown(currentError);
            if (!TryBuildRecordedMap(recorded.Contracts, currentByKey, out var recordedByKey, out var recordedError))
                return Unknown(recordedError);

            var keys = recordedByKey.Keys.Concat(currentByKey.Keys)
                .Distinct().OrderBy(key => key, ContractKeyComparer.Instance).ToArray();
            var findings = new List<FoxRunCompatibilityTopicFinding>();
            foreach (var key in keys)
            {
                recordedByKey.TryGetValue(key, out var oldContract);
                currentByKey.TryGetValue(key, out var newContract);
                var presence = oldContract == null
                    ? FoxRunCompatibilityPresence.AddedInCurrent
                    : newContract == null
                        ? FoxRunCompatibilityPresence.RemovedFromCurrent
                        : FoxRunCompatibilityPresence.Both;
                findings.Add(new FoxRunCompatibilityTopicFinding(
                    key.Topic,
                    key.SchemaName,
                    key.Encoding,
                    key.Flow,
                    presence,
                    oldContract == null || newContract == null || !StringEquals(oldContract.ContractHash, newContract.ContractHash),
                    oldContract == null || newContract == null || !StringEquals(oldContract.BindingHash, newContract.BindingHash),
                    oldContract == null || newContract == null || !StringEquals(oldContract.PolicyHash, newContract.PolicyHash)));
            }

            if (StringEquals(recorded.GlobalManifestHash, current.GlobalManifestHash))
                return Result(FoxRunCompatibilityClass.Exact, findings);

            if (KeysEqual(recordedByKey, currentByKey)
                && recordedByKey.Keys.All(key =>
                    StringEquals(recordedByKey[key].ContractHash, currentByKey[key].ContractHash)
                    && StringEquals(recordedByKey[key].BindingHash, currentByKey[key].BindingHash))
                && recordedByKey.Keys.Any(key =>
                    !StringEquals(recordedByKey[key].PolicyHash, currentByKey[key].PolicyHash)))
                return Result(FoxRunCompatibilityClass.PolicyOnlyChange, findings);

            if (KeysEqual(recordedByKey, currentByKey)
                && recordedByKey.Keys.All(key =>
                    StringEquals(recordedByKey[key].ContractHash, currentByKey[key].ContractHash))
                && recordedByKey.Keys.Any(key =>
                    !StringEquals(recordedByKey[key].BindingHash, currentByKey[key].BindingHash)))
                return Result(FoxRunCompatibilityClass.BindingOnlyChange, findings);

            var shared = recordedByKey.Keys.Intersect(currentByKey.Keys).ToArray();
            var allSharedFieldsPreserved = shared.All(key =>
                FieldsPreserved(recordedByKey[key].Fields, currentByKey[key].Fields)
                || FieldsPreserved(currentByKey[key].Fields, recordedByKey[key].Fields));
            var backwardFields = shared.All(key => FieldSetSubset(recordedByKey[key].Fields, currentByKey[key].Fields));
            var forwardFields = shared.All(key => FieldSetSubset(currentByKey[key].Fields, recordedByKey[key].Fields));
            if (allSharedFieldsPreserved && backwardFields
                && (currentByKey.Keys.Any(key => !recordedByKey.ContainsKey(key))
                    || shared.Any(key => currentByKey[key].Fields.Count > recordedByKey[key].Fields.Count)))
                return Result(FoxRunCompatibilityClass.BackwardCompatible, findings);
            if (allSharedFieldsPreserved && forwardFields
                && (recordedByKey.Keys.Any(key => !currentByKey.ContainsKey(key))
                    || shared.Any(key => recordedByKey[key].Fields.Count > currentByKey[key].Fields.Count)))
                return Result(FoxRunCompatibilityClass.ForwardCompatible, findings);

            return Result(FoxRunCompatibilityClass.Breaking, findings);
        }

        private static bool TryBuildCurrentMap(
            IReadOnlyList<FoxRunSchemaMcapContractMetadata> contracts,
            out Dictionary<ContractKey, FoxRunSchemaMcapContractMetadata> map,
            out string error)
        {
            map = new Dictionary<ContractKey, FoxRunSchemaMcapContractMetadata>();
            foreach (var contract in contracts)
            {
                var key = ContractKey.From(contract);
                if (!map.TryAdd(key, contract))
                {
                    error = "current metadata contains duplicate contract identities";
                    return false;
                }
            }

            error = string.Empty;
            return true;
        }

        private static bool TryBuildRecordedMap(
            IReadOnlyList<FoxRunSchemaMcapContractMetadata> contracts,
            IReadOnlyDictionary<ContractKey, FoxRunSchemaMcapContractMetadata> currentByKey,
            out Dictionary<ContractKey, FoxRunSchemaMcapContractMetadata> map,
            out string error)
        {
            map = new Dictionary<ContractKey, FoxRunSchemaMcapContractMetadata>();
            foreach (var contract in contracts)
            {
                var key = ContractKey.From(contract);
                if (string.IsNullOrEmpty(key.Flow))
                {
                    var candidates = currentByKey.Keys
                        .Where(candidate => candidate.SameBase(key))
                        .ToArray();
                    if (candidates.Length > 1)
                    {
                        error = "recorded metadata has ambiguous legacy contract identity";
                        return false;
                    }
                    if (candidates.Length == 1)
                        key = candidates[0];
                }

                if (!map.TryAdd(key, contract))
                {
                    error = "metadata contains duplicate contract identities";
                    return false;
                }
            }

            error = string.Empty;
            return true;
        }

        private static bool KeysEqual<TKey, TValue>(
            IReadOnlyDictionary<TKey, TValue> left,
            IReadOnlyDictionary<TKey, TValue> right)
            => left.Count == right.Count && left.Keys.All(right.ContainsKey);

        private static FoxRunCompatibilityResult Unknown(string reason)
            => Result(FoxRunCompatibilityClass.Unknown, Array.Empty<FoxRunCompatibilityTopicFinding>(), reason);

        private static FoxRunCompatibilityResult Result(
            FoxRunCompatibilityClass classification,
            IReadOnlyList<FoxRunCompatibilityTopicFinding> findings,
            string reason = null)
        {
            var builder = new StringBuilder(classification.ToString());
            if (!string.IsNullOrWhiteSpace(reason))
                builder.Append(": ").Append(reason);
            var ambiguousTopics = findings
                .GroupBy(item => item.Topic, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToList();
            foreach (var finding in findings
                         .OrderBy(item => item.Topic, StringComparer.Ordinal)
                         .ThenBy(item => item.Encoding, StringComparer.Ordinal)
                         .ThenBy(item => item.Flow, StringComparer.Ordinal))
            {
                builder.Append('\n').Append(finding.Describe(ambiguousTopics.Contains(finding.Topic, StringComparer.Ordinal)));
            }
            return new FoxRunCompatibilityResult(classification, findings, builder.ToString());
        }

        private static bool FieldsPreserved(
            IReadOnlyList<FoxRunSchemaMcapFieldMetadata> recorded,
            IReadOnlyList<FoxRunSchemaMcapFieldMetadata> current)
        {
            if (recorded == null || current == null)
                return false;
            return recorded.All(oldField => current.Any(newField =>
                oldField.Ordinal == newField.Ordinal
                && StringEquals(oldField.Name, newField.Name)
                && StringEquals(oldField.CanonicalType, newField.CanonicalType)
                && StringEquals(oldField.Encoding, newField.Encoding)));
        }

        private static bool FieldSetSubset(
            IReadOnlyList<FoxRunSchemaMcapFieldMetadata> subset,
            IReadOnlyList<FoxRunSchemaMcapFieldMetadata> superset)
        {
            if (subset == null || superset == null)
                return false;
            return subset.All(oldField => superset.Any(newField =>
                oldField.Ordinal == newField.Ordinal
                && StringEquals(oldField.Name, newField.Name)
                && StringEquals(oldField.CanonicalType, newField.CanonicalType)
                && StringEquals(oldField.Encoding, newField.Encoding)));
        }

        private static FoxRunSchemaMcapContractMetadata ToRecordedContract(FoxRunSchemaContractInfo contract)
        {
            return new FoxRunSchemaMcapContractMetadata
            {
                Topic = contract.Topic,
                SchemaName = contract.SchemaName,
                Encoding = contract.Encoding,
                Flow = contract.Flow,
                ContractHash = contract.ContractHash,
                BindingHash = contract.BindingHash,
                PolicyHash = contract.PolicyHash,
                Fields = (contract.Fields ?? Array.Empty<FoxRunSchemaFieldInfo>())
                    .Select((field, ordinal) => new FoxRunSchemaMcapFieldMetadata
                    {
                        Name = field?.JsonName ?? string.Empty,
                        CanonicalType = field?.Type ?? string.Empty,
                        Ordinal = ordinal,
                        Encoding = contract.Encoding ?? string.Empty
                    }).ToList()
            };
        }

        private static bool StringEquals(string left, string right)
            => string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.Ordinal);

        private readonly struct ContractKey : IEquatable<ContractKey>
        {
            public readonly string Topic;
            public readonly string SchemaName;
            public readonly string Encoding;
            public readonly string Flow;

            private ContractKey(string topic, string schemaName, string encoding, string flow)
            {
                Topic = topic ?? string.Empty;
                SchemaName = schemaName ?? string.Empty;
                Encoding = encoding ?? string.Empty;
                Flow = flow ?? string.Empty;
            }

            public static ContractKey From(FoxRunSchemaMcapContractMetadata contract)
                => new ContractKey(contract.Topic, contract.SchemaName, contract.Encoding, contract.Flow);

            public bool SameBase(ContractKey other)
                => StringEquals(Topic, other.Topic)
                   && StringEquals(SchemaName, other.SchemaName)
                   && StringEquals(Encoding, other.Encoding);

            public bool Equals(ContractKey other)
                => SameBase(other) && StringEquals(Flow, other.Flow);

            public override bool Equals(object obj)
                => obj is ContractKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = StringComparer.Ordinal.GetHashCode(Topic);
                    hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(SchemaName);
                    hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(Encoding);
                    return (hash * 397) ^ StringComparer.Ordinal.GetHashCode(Flow);
                }
            }
        }

        private sealed class ContractKeyComparer : IComparer<ContractKey>
        {
            public static readonly ContractKeyComparer Instance = new ContractKeyComparer();

            public int Compare(ContractKey left, ContractKey right)
            {
                var compare = string.Compare(left.Topic, right.Topic, StringComparison.Ordinal);
                if (compare != 0)
                    return compare;
                compare = string.Compare(left.SchemaName, right.SchemaName, StringComparison.Ordinal);
                if (compare != 0)
                    return compare;
                compare = string.Compare(left.Encoding, right.Encoding, StringComparison.Ordinal);
                return compare != 0
                    ? compare
                    : string.Compare(left.Flow, right.Flow, StringComparison.Ordinal);
            }
        }
    }
}
