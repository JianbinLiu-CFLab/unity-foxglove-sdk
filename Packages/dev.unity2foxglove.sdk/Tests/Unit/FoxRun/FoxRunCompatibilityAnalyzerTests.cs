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
            var current = Manifest("g", "c", "b", "p", "int32");
            var legacy = FoxRunSchemaMcapMetadata.CreateRecord(current);
            legacy.SchemaMetadataVersion = 1;
            legacy.Contracts[0].Fields = null;

            var result = FoxRunCompatibilityAnalyzer.Analyze(legacy, current);
            Assert.Equal(FoxRunCompatibilityClass.Unknown, result.Classification);
            Assert.Equal(FoxRunCompatibilityDecision.Block, result.Decide(SchemaIdentityMode.Strict));
            Assert.Equal(FoxRunCompatibilityDecision.Block, result.Decide(SchemaIdentityMode.Compatible));
            Assert.Equal(FoxRunCompatibilityDecision.Warn, result.Decide(SchemaIdentityMode.Warn));
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
        public void CompatiblePolicyAcceptsOnlyPolicyAndBackwardClasses()
        {
            var policy = Analyze("g", "c", "b", "p", "g2", "c", "b", "p2");
            Assert.Equal(FoxRunCompatibilityDecision.Proceed, policy.Decide(SchemaIdentityMode.Compatible));
            var binding = Analyze("g", "c", "b", "p", "g2", "c", "b2", "p");
            Assert.Equal(FoxRunCompatibilityDecision.Block, binding.Decide(SchemaIdentityMode.Compatible));
        }

        [Fact]
        public void WritersEmitVersionTwoAndReadersAcceptLegacyVersionOne()
        {
            var current = Manifest("g", "c", "b", "p", "int32");
            Assert.True(FoxRunSchemaMcapMetadata.TryCreateJson(current, out var json));
            Assert.Contains("\"schemaMetadataVersion\":2", json, StringComparison.Ordinal);
            Assert.Contains("\"fields\":[", json, StringComparison.Ordinal);

            var legacy = json.Replace("\"schemaMetadataVersion\":2", "\"schemaMetadataVersion\":1", StringComparison.Ordinal)
                .Replace(",\"fields\":[{\"name\":\"field0\",\"canonicalType\":\"int32\",\"ordinal\":0,\"encoding\":\"json\"}]", string.Empty, StringComparison.Ordinal);
            Assert.True(FoxRunSchemaMcapMetadata.TryParseJson(legacy, out var record, out var error), error);
            Assert.Equal(1, record.SchemaMetadataVersion);
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
        public void CompatibilityVectorFixtureIsExecutableAndComplete()
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root != null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md")))
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
            var fields = new List<FoxRunSchemaFieldInfo>();
            for (var index = 0; index < fieldTypes.Length; index++)
                fields.Add(new FoxRunSchemaFieldInfo("field" + index, "field" + index, "field", fieldTypes[index], false, false));
            var contract = new FoxRunSchemaContractInfo(
                "Demo.State", "/state", "Demo.State", "json", contractHash, bindingHash, policyHash,
                "Publish", 1f, 0f, fields);
            return new FoxRunSchemaManifestInfo(
                4, "unit", "unit", 1, global, "manifest", new[] { new FoxRunSchemaTypeInfo("Demo.State", new[] { contract }) });
        }
    }
}
