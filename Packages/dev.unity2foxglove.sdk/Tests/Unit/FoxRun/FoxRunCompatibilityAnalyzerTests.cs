// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/FoxRun
// Purpose: Compatibility taxonomy, metadata version and policy vectors.

using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Editor;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.FoxRun
{
    public sealed class FoxRunCompatibilityAnalyzerTests
    {
        [Fact]
        public void ExactAndPolicyOnlyChangesAreDisjoint()
        {
            var exact = Analyze("g", "c", "b", "p", "g", "c", "b", "p");
            Assert.Equal(FoxRunCompatibilityClass.Exact, exact.Classification);

            var policy = Analyze("g", "c", "b", "p", "g2", "c", "b", "p2");
            Assert.Equal(FoxRunCompatibilityClass.PolicyOnlyChange, policy.Classification);
        }

        [Fact]
        public void BindingAndBreakingChangesAreReportedPerTopic()
        {
            var binding = Analyze("g", "c", "b", "p", "g2", "c", "b2", "p");
            Assert.Equal(FoxRunCompatibilityClass.BindingOnlyChange, binding.Classification);
            Assert.Contains("/state: contract same / binding changed / policy same", binding.Message, StringComparison.Ordinal);

            var breaking = Analyze("g", "c", "b", "p", "g2", "c2", "b", "p", "double");
            Assert.Equal(FoxRunCompatibilityClass.Breaking, breaking.Classification);
        }

        [Fact]
        public void VersionOneWithoutFieldDigestsIsUnknownAndCompatiblePolicyIsExplicit()
        {
            var recorded = Manifest("g", "c", "b", "p", "int32");
            var current = Manifest("g2", "c2", "b2", "p2", "int32", "string");
            var legacy = FoxRunSchemaMcapMetadata.CreateRecord(recorded);
            legacy.SchemaMetadataVersion = 1;
            legacy.Contracts[0].Fields = null;

            var result = FoxRunCompatibilityAnalyzer.Analyze(legacy, current);
            Assert.Equal(FoxRunCompatibilityClass.Unknown, result.Classification);
            Assert.Equal(FoxRunCompatibilityDecision.Block, result.Decide(SchemaIdentityMode.Strict));
            Assert.Equal(FoxRunCompatibilityDecision.Block, result.Decide(SchemaIdentityMode.Compatible));
            Assert.Equal(FoxRunCompatibilityDecision.Warn, result.Decide(SchemaIdentityMode.Warn));
        }

        [Fact]
        public void LegacyRecordingWithTheSameGlobalHashStaysExact()
        {
            var current = Manifest("g", "c", "b", "p", "int32");

            var legacyVersion = FoxRunSchemaMcapMetadata.CreateRecord(current);
            legacyVersion.SchemaMetadataVersion = 1;
            legacyVersion.Contracts[0].Fields = null;
            var versionResult = FoxRunCompatibilityAnalyzer.Analyze(legacyVersion, current);

            var missingDigests = FoxRunSchemaMcapMetadata.CreateRecord(current);
            missingDigests.Contracts[0].Fields = null;
            var digestResult = FoxRunCompatibilityAnalyzer.Analyze(missingDigests, current);

            // The global manifest hash already proves the contract universe is identical; field
            // digests only enable field-level reasoning, so their absence must not block a replay
            // of a recording that matches the current build exactly.
            Assert.Equal(FoxRunCompatibilityClass.Exact, versionResult.Classification);
            Assert.Equal(FoxRunCompatibilityClass.Exact, digestResult.Classification);
            Assert.Equal(FoxRunCompatibilityDecision.Proceed, versionResult.Decide(SchemaIdentityMode.Strict));
            Assert.Equal(FoxRunCompatibilityDecision.Proceed, digestResult.Decide(SchemaIdentityMode.Strict));

            var guard = FoxRunSchemaMcapMetadata.Evaluate(legacyVersion, current, SchemaIdentityMode.Strict);
            Assert.Equal(FoxRunReplaySchemaGuardState.Match, guard.State);
            Assert.False(guard.IsBlocking);
        }

        [Fact]
        public void FindingsQualifyRepeatedTopicsAndNameAddedOrRemovedContracts()
        {
            var recorded = ManifestWithContracts(
                "old",
                Contract("c-json", "b-json", "p-json", encoding: "json"),
                Contract("c-proto", "b-proto", "p-proto", encoding: "protobuf"));
            var current = ManifestWithContracts(
                "new",
                Contract("c-json", "b-json", "p-json", encoding: "json"),
                Contract("c-proto2", "b-proto", "p-proto", encoding: "protobuf"));

            var result = FoxRunCompatibilityAnalyzer.Analyze(
                FoxRunSchemaMcapMetadata.CreateRecord(recorded), current);

            // Two contracts share the topic, so each line has to carry the rest of the identity.
            Assert.Contains("/state [json/Publish]: contract same / binding same / policy same", result.Message, StringComparison.Ordinal);
            Assert.Contains("/state [protobuf/Publish]: contract changed / binding same / policy same", result.Message, StringComparison.Ordinal);

            var added = ManifestWithContracts(
                "newer",
                Contract("c-json", "b-json", "p-json", encoding: "json"),
                Contract("c-proto", "b-proto", "p-proto", encoding: "protobuf"),
                Contract("c-extra", "b-extra", "p-extra", topic: "/extra", encoding: "json"));
            var addedResult = FoxRunCompatibilityAnalyzer.Analyze(
                FoxRunSchemaMcapMetadata.CreateRecord(recorded), added);
            Assert.Contains("/extra: added", addedResult.Message, StringComparison.Ordinal);

            var removedResult = FoxRunCompatibilityAnalyzer.Analyze(
                FoxRunSchemaMcapMetadata.CreateRecord(added), recorded);
            Assert.Contains("/extra: removed", removedResult.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void AddedFieldIsBackwardCompatibleAndRemovedFieldIsForwardCompatible()
        {
            var recorded = Manifest("old", "c", "b", "p", "int32");
            var added = Manifest("new", "c2", "b2", "p2", "int32", "string");
            var addedResult = FoxRunCompatibilityAnalyzer.Analyze(FoxRunSchemaMcapMetadata.CreateRecord(recorded), added);
            Assert.Equal(FoxRunCompatibilityClass.BackwardCompatible, addedResult.Classification);

            var removed = Manifest("newer", "c3", "b3", "p3", "int32");
            var removedResult = FoxRunCompatibilityAnalyzer.Analyze(FoxRunSchemaMcapMetadata.CreateRecord(added), removed);
            Assert.Equal(FoxRunCompatibilityClass.ForwardCompatible, removedResult.Classification);
        }

        [Fact]
        public void ReplacingEveryContractIsNotBackwardCompatible()
        {
            var recorded = ManifestWithContracts(
                "recorded",
                Contract("old-contract", "old-binding", "old-policy", topic: "/old"));
            var current = ManifestWithContracts(
                "current",
                Contract("new-contract", "new-binding", "new-policy", topic: "/new"));

            var result = FoxRunCompatibilityAnalyzer.Analyze(
                FoxRunSchemaMcapMetadata.CreateRecord(recorded),
                current);

            Assert.Equal(FoxRunCompatibilityClass.Breaking, result.Classification);
        }

        [Fact]
        public void JsonFieldAddedBeforeExistingNamesRemainsBackwardCompatible()
        {
            var recorded = ManifestWithContracts(
                "recorded",
                ContractWithFields(
                    "old-contract", "old-binding", "old-policy", "json",
                    new FoxRunSchemaFieldInfo("b", "b", "field", "int32", false, false),
                    new FoxRunSchemaFieldInfo("c", "c", "field", "string", false, false)));
            var current = ManifestWithContracts(
                "current",
                ContractWithFields(
                    "new-contract", "new-binding", "new-policy", "json",
                    new FoxRunSchemaFieldInfo("a", "a", "field", "bool", false, false),
                    new FoxRunSchemaFieldInfo("b", "b", "field", "int32", false, false),
                    new FoxRunSchemaFieldInfo("c", "c", "field", "string", false, false)));

            var result = FoxRunCompatibilityAnalyzer.Analyze(
                FoxRunSchemaMcapMetadata.CreateRecord(recorded),
                current);

            Assert.Equal(FoxRunCompatibilityClass.BackwardCompatible, result.Classification);
        }

        [Fact]
        public void ProtobufFieldDigestWithoutFieldNumbersDoesNotClaimBackwardCompatibility()
        {
            var recorded = ManifestWithContracts(
                "recorded",
                ContractWithFields(
                    "old-contract", "old-binding", "old-policy", "protobuf",
                    new FoxRunSchemaFieldInfo("a", "a", "field", "int32", false, false)));
            var current = ManifestWithContracts(
                "current",
                ContractWithFields(
                    "new-contract", "new-binding", "new-policy", "protobuf",
                    new FoxRunSchemaFieldInfo("a", "a", "field", "int32", false, false),
                    new FoxRunSchemaFieldInfo("b", "b", "field", "string", false, false)));

            var result = FoxRunCompatibilityAnalyzer.Analyze(
                FoxRunSchemaMcapMetadata.CreateRecord(recorded),
                current);

            Assert.Equal(FoxRunCompatibilityClass.Unknown, result.Classification);
            Assert.Contains("protobuf field metadata", result.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void CompatiblePolicyAcceptsOnlyPolicyAndBackwardClasses()
        {
            var policy = Analyze("g", "c", "b", "p", "g2", "c", "b", "p2");
            Assert.Equal(FoxRunCompatibilityDecision.Proceed, policy.Decide(SchemaIdentityMode.Compatible));
            var binding = Analyze("g", "c", "b", "p", "g2", "c", "b2", "p");
            Assert.Equal(FoxRunCompatibilityDecision.Block, binding.Decide(SchemaIdentityMode.Compatible));
        }

        [Fact]
        public void MultipleEncodingsOnOneTopicUseTheFullContractIdentity()
        {
            var recorded = ManifestWithContracts(
                "same",
                Contract("c-json", "b-json", "p-json", encoding: "json"),
                Contract("c-proto", "b-proto", "p-proto", encoding: "protobuf"));
            var current = ManifestWithContracts(
                "same",
                Contract("c-json", "b-json", "p-json", encoding: "json"),
                Contract("c-proto", "b-proto", "p-proto", encoding: "protobuf"));

            var result = FoxRunCompatibilityAnalyzer.Analyze(
                FoxRunSchemaMcapMetadata.CreateRecord(recorded),
                current);

            Assert.Equal(FoxRunCompatibilityClass.Exact, result.Classification);
            Assert.Equal(2, result.Findings.Count);
        }

        [Fact]
        public void PolicyComparisonDoesNotCrossMatchDifferentContracts()
        {
            var recorded = ManifestWithContracts(
                "recorded",
                Contract("c-a", "b-a", "p-a", topic: "/a"),
                Contract("c-b", "b-b", "p-b", topic: "/b"));
            var current = ManifestWithContracts(
                "current",
                Contract("c-a", "b-a", "p-a", topic: "/a"),
                Contract("c-b", "b-b", "p-b", topic: "/b"));

            var result = FoxRunCompatibilityAnalyzer.Analyze(
                FoxRunSchemaMcapMetadata.CreateRecord(recorded),
                current);

            Assert.Equal(FoxRunCompatibilityClass.Breaking, result.Classification);
        }

        [Fact]
        public void BindingComparisonDoesNotCrossMatchDifferentContracts()
        {
            var recorded = ManifestWithContracts(
                "recorded",
                Contract("c-a", "b-a", "p-a", topic: "/a"),
                Contract("c-b", "b-b", "p-b", topic: "/b"));
            var current = ManifestWithContracts(
                "current",
                Contract("c-a", "b-a", "p-a", topic: "/a"),
                Contract("c-b", "b-b", "p-b", topic: "/b"));

            var result = FoxRunCompatibilityAnalyzer.Analyze(
                FoxRunSchemaMcapMetadata.CreateRecord(recorded),
                current);

            Assert.Equal(FoxRunCompatibilityClass.Breaking, result.Classification);
        }

        [Fact]
        public void DirectionScopedContractsPersistFlowAndRemainDistinct()
        {
            var recorded = ManifestWithContracts(
                "same",
                Contract("c-publish", "b-publish", "p-publish", flow: "Publish"),
                Contract("c-subscribe", "b-subscribe", "p-subscribe", flow: "Subscribe"));
            var current = ManifestWithContracts(
                "same",
                Contract("c-publish", "b-publish", "p-publish", flow: "Publish"),
                Contract("c-subscribe", "b-subscribe", "p-subscribe", flow: "Subscribe"));
            var record = FoxRunSchemaMcapMetadata.CreateRecord(recorded);

            Assert.All(record.Contracts, contract => Assert.False(string.IsNullOrEmpty(contract.Flow)));
            var result = FoxRunCompatibilityAnalyzer.Analyze(record, current);

            Assert.Equal(FoxRunCompatibilityClass.Exact, result.Classification);
            Assert.Equal(2, result.Findings.Count);
        }

        [Fact]
        public void ContractsThatDifferOnlyByFlowGetADeterministicCanonicalOrder()
        {
            // Topic, schema name and encoding are identical here, so Flow is the only tiebreaker the
            // comparer has left. Without it the comparison returns 0 and the canonical order follows
            // whatever order the manifest happened to enumerate, which would make the recorded
            // identity - and therefore every hash comparison built on it - non-deterministic.
            var manifest = ManifestWithContracts(
                "same",
                Contract("c-subscribe", "b-subscribe", "p-subscribe", flow: "Subscribe"),
                Contract("c-publish", "b-publish", "p-publish", flow: "Publish"));

            var record = FoxRunSchemaMcapMetadata.CreateRecord(manifest);

            Assert.Equal(2, record.Contracts.Count);
            Assert.Equal("Publish", record.Contracts[0].Flow);
            Assert.Equal("Subscribe", record.Contracts[1].Flow);
            Assert.Equal("c-publish", record.Contracts[0].ContractHash);
            Assert.Equal("c-subscribe", record.Contracts[1].ContractHash);
        }

        [Fact]
        public void LegacyVersionTwoWithoutFlowUsesAnUnambiguousCurrentContract()
        {
            var manifest = Manifest("same", "c", "b", "p");
            Assert.True(FoxRunSchemaMcapMetadata.TryCreateJson(manifest, out var json));
            var legacyJson = json.Replace(",\"flow\":\"Publish\"", string.Empty, StringComparison.Ordinal);
            Assert.True(FoxRunSchemaMcapMetadata.TryParseJson(legacyJson, out var record, out var error), error);

            var result = FoxRunCompatibilityAnalyzer.Analyze(record, manifest);

            Assert.Equal(FoxRunCompatibilityClass.Exact, result.Classification);
        }

        [Fact]
        public void DuplicateFullContractIdentityRemainsUnknown()
        {
            var recorded = ManifestWithContracts(
                "recorded",
                Contract("c-a", "b-a", "p-a"),
                Contract("c-b", "b-b", "p-b"));
            var current = Manifest("current", "c-a", "b-a", "p-a");

            var result = FoxRunCompatibilityAnalyzer.Analyze(
                FoxRunSchemaMcapMetadata.CreateRecord(recorded),
                current);

            Assert.Equal(FoxRunCompatibilityClass.Unknown, result.Classification);
            Assert.Contains("duplicate contract identities", result.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void WritersEmitVersionThreeAndReadersAcceptLegacyVersionOne()
        {
            var current = Manifest("g", "c", "b", "p", "int32");
            Assert.True(FoxRunSchemaMcapMetadata.TryCreateJson(current, out var json));
            Assert.Contains("\"schemaMetadataVersion\":3", json, StringComparison.Ordinal);
            Assert.Contains("\"fields\":[", json, StringComparison.Ordinal);

            var legacy = json.Replace("\"schemaMetadataVersion\":3", "\"schemaMetadataVersion\":1", StringComparison.Ordinal)
                .Replace(",\"fields\":[{\"name\":\"field0\",\"canonicalType\":\"int32\",\"ordinal\":0,\"encoding\":\"json\",\"nullable\":false,\"array\":false,\"aggregate\":false,\"protobufFieldNumber\":0,\"typeShapeDigest\":\"\"}]", string.Empty, StringComparison.Ordinal);
            Assert.True(FoxRunSchemaMcapMetadata.TryParseJson(legacy, out var record, out var error), error);
            Assert.Equal(1, record.SchemaMetadataVersion);
        }

        [Fact]
        public void VersionTwoRecordsUseLegacyFieldProjectionWhenMetadataGainsFields()
        {
            var recordedManifest = ManifestWithContracts(
                "recorded",
                ContractWithFields(
                    "contract", "binding", "policy", "json",
                    new FoxRunSchemaFieldInfo("field0", "field0", "field", "int32", true, true, aggregate: true)));
            var currentManifest = ManifestWithContracts(
                    "current",
                ContractWithFields(
                    "contract", "binding", "policy", "json",
                    new FoxRunSchemaFieldInfo("field0", "field0", "field", "int32", true, true, aggregate: true),
                    new FoxRunSchemaFieldInfo("field1", "field1", "field", "string", false, false)));
            Assert.True(FoxRunSchemaMcapMetadata.TryCreateJson(recordedManifest, out var json));
            var legacyJson = json.Replace("\"schemaMetadataVersion\":3", "\"schemaMetadataVersion\":2", StringComparison.Ordinal)
                .Replace(",\"nullable\":true,\"array\":true,\"aggregate\":true,\"protobufFieldNumber\":0,\"typeShapeDigest\":\"\"", string.Empty, StringComparison.Ordinal);
            Assert.True(FoxRunSchemaMcapMetadata.TryParseJson(legacyJson, out var record, out var error), error);

            var result = FoxRunCompatibilityAnalyzer.Analyze(record, currentManifest);

            Assert.Equal(FoxRunCompatibilityClass.BackwardCompatible, result.Classification);
        }

        [Fact]
        public void CurrentVersionRequiresFieldArraysDuringParsing()
        {
            var current = Manifest("g", "c", "b", "p", "int32");
            Assert.True(FoxRunSchemaMcapMetadata.TryCreateJson(current, out var json));
            var malformed = json.Replace(",\"fields\":[{\"name\":\"field0\",\"canonicalType\":\"int32\",\"ordinal\":0,\"encoding\":\"json\",\"nullable\":false,\"array\":false,\"aggregate\":false,\"protobufFieldNumber\":0,\"typeShapeDigest\":\"\"}]", string.Empty, StringComparison.Ordinal);

            Assert.False(FoxRunSchemaMcapMetadata.TryParseJson(malformed, out _, out var error));
            Assert.Contains("contract fields are missing", error, StringComparison.Ordinal);
        }

        [Fact]
        public void MemberDecompositionKeepsCanonicalValues()
        {
            var member = new FoxRunGenerationMember(
                "Demo", "State", "_value", "field", "System.Int32", true, false, "",
                "/state", 10f, "Demo.State", 1, 0f, "UnitTest", 0, "",
                encoding: "json");
            var decomposition = FoxRunGenerationMemberDecomposition.From(member);
            Assert.Equal(member.EmissionTypeName, decomposition.Type.EmissionTypeName);
            Assert.Equal(member.Hz, decomposition.Schedule.Hz);
            Assert.Equal(member.Encoding, decomposition.Encoding.Encoding);
            Assert.Equal(member.ConditionMemberKind, decomposition.Condition.MemberKind);
        }

        [Fact]
        public void CompatibilityVectorFixtureIsDeclarativeAndComplete()
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root != null && !Directory.Exists(Path.Combine(root.FullName, ".git")))
                root = root.Parent;
            Assert.NotNull(root);
            var path = Path.Combine(root.FullName, "Packages", "dev.unity2foxglove.sdk", "Tests", "Unit", "FoxRun", "Fixtures", "Phase191CompatibilityVectors.json");
            var vectors = JObject.Parse(File.ReadAllText(path))["vectors"] as JArray;
            Assert.NotNull(vectors);
            Assert.Equal(13, vectors.Count);
            foreach (var vector in vectors)
            {
                Assert.False(string.IsNullOrWhiteSpace((string)vector["id"]));
                Assert.True(Enum.TryParse((string)vector["expected"], out FoxRunCompatibilityClass _));
            }
        }

        [Fact]
        public void ConflictedGeneratedAuthorityBlocksReplayIdentityEvaluation()
        {
            var first = Manifest("first", "c", "b", "p");
            var second = Manifest("second", "c", "b", "p");
            FoxRunSchemaInfoRegistry.ClearForTests();
            try
            {
                FoxRunSchemaInfoRegistry.RegisterGenerated(first);
                FoxRunSchemaInfoRegistry.RegisterGenerated(second);

                var result = FoxRunSchemaMcapMetadata.Evaluate(
                    FoxRunSchemaMcapMetadata.CreateRecord(first),
                    first,
                    SchemaIdentityMode.Strict);

                Assert.True(FoxRunSchemaInfoRegistry.HasConflict);
                Assert.Equal(FoxRunReplaySchemaGuardState.Mismatch, result.State);
                Assert.True(result.IsBlocking);
                Assert.Contains("authority is conflicted", result.Message, StringComparison.Ordinal);
            }
            finally
            {
                FoxRunSchemaInfoRegistry.ClearForTests();
            }
        }

        [Fact]
        public void RecordingControllerSkipsSchemaEvidenceWhenAuthorityIsConflicted()
        {
            var path = Path.Combine(
                FindRepositoryRoot(),
                "Packages/dev.unity2foxglove.sdk/Runtime/Core/Recording/RecordingController.cs");
            var source = File.ReadAllText(path);
            var conflict = source.IndexOf("FoxRunSchemaInfoRegistry.HasConflict", StringComparison.Ordinal);
            var write = source.IndexOf("TryCreateJson(FoxRunSchemaInfoRegistry.Current", StringComparison.Ordinal);
            Assert.True(conflict >= 0);
            Assert.True(write > conflict);
        }

        private static string FindRepositoryRoot()
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root != null && !Directory.Exists(Path.Combine(root.FullName, ".git")))
                root = root.Parent;
            Assert.NotNull(root);
            return root.FullName;
        }

        private static FoxRunCompatibilityResult Analyze(
            string recordedGlobal,
            string recordedContract,
            string recordedBinding,
            string recordedPolicy,
            string currentGlobal,
            string currentContract,
            string currentBinding,
            string currentPolicy,
            string currentType = "int32")
        {
            var recorded = FoxRunSchemaMcapMetadata.CreateRecord(Manifest(
                recordedGlobal, recordedContract, recordedBinding, recordedPolicy, "int32"));
            var current = Manifest(currentGlobal, currentContract, currentBinding, currentPolicy, currentType);
            return FoxRunCompatibilityAnalyzer.Analyze(recorded, current);
        }

        private static FoxRunSchemaManifestInfo Manifest(
            string global,
            string contractHash,
            string bindingHash,
            string policyHash,
            params string[] fieldTypes)
        {
            return ManifestWithContracts(global, Contract(
                contractHash,
                bindingHash,
                policyHash,
                fieldTypes: fieldTypes));
        }

        private static FoxRunSchemaManifestInfo ManifestWithContracts(
            string global,
            params FoxRunSchemaContractInfo[] contracts)
        {
            return new FoxRunSchemaManifestInfo(
                4, "unit", "unit", 1, global, "manifest", new[] { new FoxRunSchemaTypeInfo("Demo.State", contracts) });
        }

        private static FoxRunSchemaContractInfo Contract(
            string contractHash,
            string bindingHash,
            string policyHash,
            string topic = "/state",
            string schemaName = "Demo.State",
            string encoding = "json",
            string flow = "Publish",
            params string[] fieldTypes)
        {
            var fields = new List<FoxRunSchemaFieldInfo>();
            var types = fieldTypes == null || fieldTypes.Length == 0 ? new[] { "int32" } : fieldTypes;
            for (var index = 0; index < types.Length; index++)
                fields.Add(new FoxRunSchemaFieldInfo("field" + index, "field" + index, "field", types[index], false, false));
            return new FoxRunSchemaContractInfo(
                "Demo.State", topic, schemaName, encoding, contractHash, bindingHash, policyHash,
                "Publish", 1f, 0f, fields, flow: flow);
        }

        private static FoxRunSchemaContractInfo ContractWithFields(
            string contractHash,
            string bindingHash,
            string policyHash,
            string encoding,
            params FoxRunSchemaFieldInfo[] fields)
        {
            return new FoxRunSchemaContractInfo(
                "Demo.State", "/state", "Demo.State", encoding,
                contractHash, bindingHash, policyHash,
                "Publish", 1f, 0f, fields, flow: "Publish");
        }
    }
}
