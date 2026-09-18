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

    public sealed class FoxRunCompatibilityTopicFinding
    {
        public string Topic { get; }
        public bool ContractChanged { get; }
        public bool BindingChanged { get; }
        public bool PolicyChanged { get; }

        public FoxRunCompatibilityTopicFinding(
            string topic,
            bool contractChanged,
            bool bindingChanged,
            bool policyChanged)
        {
            Topic = topic ?? string.Empty;
            ContractChanged = contractChanged;
            BindingChanged = bindingChanged;
            PolicyChanged = policyChanged;
        }

        public string Describe()
            => Topic + ": contract " + (ContractChanged ? "changed" : "same")
               + " / binding " + (BindingChanged ? "changed" : "same")
               + " / policy " + (PolicyChanged ? "changed" : "same");
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
            if (recorded.Contracts.GroupBy(contract => contract.Topic, StringComparer.Ordinal).Any(group => group.Count() != 1))
                return Unknown("metadata contains duplicate topic identities");
            if (recorded.SchemaMetadataVersion == 1)
                return Unknown("schema metadata version 1 has no field digests");
            if (recorded.Contracts.Any(contract => contract.Fields == null))
                return Unknown("schema metadata field digest is missing");

            var currentContracts = current.Types
                .Where(type => type != null)
                .SelectMany(type => type.Contracts ?? Array.Empty<FoxRunSchemaContractInfo>())
                .Where(contract => contract != null)
                .Select(ToRecordedContract)
                .ToList();
            if (currentContracts.GroupBy(contract => contract.Topic, StringComparer.Ordinal).Any(group => group.Count() != 1))
                return Unknown("current metadata contains duplicate topic identities");

            var recordedByTopic = recorded.Contracts.ToDictionary(contract => contract.Topic, StringComparer.Ordinal);
            var currentByTopic = currentContracts.ToDictionary(contract => contract.Topic, StringComparer.Ordinal);
            var topics = recordedByTopic.Keys.Concat(currentByTopic.Keys)
                .Distinct(StringComparer.Ordinal).OrderBy(topic => topic, StringComparer.Ordinal).ToArray();
            var findings = new List<FoxRunCompatibilityTopicFinding>();
            foreach (var topic in topics)
            {
                recordedByTopic.TryGetValue(topic, out var oldContract);
                currentByTopic.TryGetValue(topic, out var newContract);
                findings.Add(new FoxRunCompatibilityTopicFinding(
                    topic,
                    oldContract == null || newContract == null || !StringEquals(oldContract.ContractHash, newContract.ContractHash),
                    oldContract == null || newContract == null || !StringEquals(oldContract.BindingHash, newContract.BindingHash),
                    oldContract == null || newContract == null || !StringEquals(oldContract.PolicyHash, newContract.PolicyHash)));
            }

            if (StringEquals(recorded.GlobalManifestHash, current.GlobalManifestHash))
                return Result(FoxRunCompatibilityClass.Exact, findings);

            if (recorded.Contracts.All(oldContract => currentContracts.Any(newContract =>
                    StringEquals(oldContract.ContractHash, newContract.ContractHash)
                    && StringEquals(oldContract.BindingHash, newContract.BindingHash)))
                && recorded.Contracts.Any(oldContract => currentContracts.Any(newContract =>
                    StringEquals(oldContract.PolicyHash, newContract.PolicyHash) == false)))
                return Result(FoxRunCompatibilityClass.PolicyOnlyChange, findings);

            if (recorded.Contracts.All(oldContract => currentContracts.Any(newContract =>
                    StringEquals(oldContract.ContractHash, newContract.ContractHash)))
                && recorded.Contracts.Any(oldContract => currentContracts.Any(newContract =>
                    StringEquals(oldContract.BindingHash, newContract.BindingHash) == false)))
                return Result(FoxRunCompatibilityClass.BindingOnlyChange, findings);

            var shared = recordedByTopic.Keys.Intersect(currentByTopic.Keys, StringComparer.Ordinal).ToArray();
            var allSharedFieldsPreserved = shared.All(topic =>
                FieldsPreserved(recordedByTopic[topic].Fields, currentByTopic[topic].Fields)
                || FieldsPreserved(currentByTopic[topic].Fields, recordedByTopic[topic].Fields));
            var backwardFields = shared.All(topic => FieldSetSubset(recordedByTopic[topic].Fields, currentByTopic[topic].Fields));
            var forwardFields = shared.All(topic => FieldSetSubset(currentByTopic[topic].Fields, recordedByTopic[topic].Fields));
            if (allSharedFieldsPreserved && backwardFields
                && (currentByTopic.Keys.Any(topic => !recordedByTopic.ContainsKey(topic))
                    || shared.Any(topic => currentByTopic[topic].Fields.Count > recordedByTopic[topic].Fields.Count)))
                return Result(FoxRunCompatibilityClass.BackwardCompatible, findings);
            if (allSharedFieldsPreserved && forwardFields
                && (recordedByTopic.Keys.Any(topic => !currentByTopic.ContainsKey(topic))
                    || shared.Any(topic => recordedByTopic[topic].Fields.Count > currentByTopic[topic].Fields.Count)))
                return Result(FoxRunCompatibilityClass.ForwardCompatible, findings);

            return Result(FoxRunCompatibilityClass.Breaking, findings);
        }

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
            foreach (var finding in findings.OrderBy(item => item.Topic, StringComparer.Ordinal))
                builder.Append('\n').Append(finding.Describe());
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
    }
}
