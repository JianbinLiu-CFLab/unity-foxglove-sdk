// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 164-57 optimization guards for unit, conformance, and performance tests.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>Source-shape validation for Phase 164-57 test-harness optimizations.</summary>
    public static class Phase164_57Validation
    {
        private static int _passed;

        /// <summary>Runs Phase 164-57 validation checks.</summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("--- Phase 164-57 Tests ---");
            _passed = 0;

            VerifyPerformanceRunnerOptimizations();
            VerifyFoxRunAggregationReferencesAreCached();
            VerifyMcapConformanceSingleRecordSerialization();
            VerifyPhaseValidationCaseAvoidsLinqMatches();
            VerifyRegistryAndProjectWiring();

            Console.WriteLine($"Phase 164-57: {_passed} checks passed.");
        }

        private static void VerifyPerformanceRunnerOptimizations()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Performance/PerformanceRunner.cs");
            var timedScenario = ExtractMethodBody(source, "private static PerformanceScenarioResult TimedScenario(");
            var indexedFixture = ExtractMethodBody(source, "private static DataLoaderFixture CreateDataLoaderIndexedFixture(");
            var directFixture = ExtractMethodBody(source, "private static DataLoaderFixture CreateDataLoaderDirectFixture(");
            var recordScenario = ExtractMethodBody(source, "private static PerformanceScenarioResult RunMcapRecord(");

            Check(!timedScenario.Contains("// GC before warmup", StringComparison.Ordinal)
                  && Count(timedScenario, "GC.Collect();") == 0,
                "164-57A-1: performance scenarios avoid forced GC before warmup");
            Check(indexedFixture.Contains("phase118_dataloader_indexed_v1_", StringComparison.Ordinal)
                  && indexedFixture.Contains("if (!File.Exists(path) || new FileInfo(path).Length == 0)", StringComparison.Ordinal)
                  && indexedFixture.Contains("FileMode.Create", StringComparison.Ordinal),
                "164-57A-2: indexed DataLoader performance fixture is reused on warm runs");
            Check(directFixture.Contains("phase118_dataloader_direct_v1_", StringComparison.Ordinal)
                  && directFixture.Contains("if (!File.Exists(path) || new FileInfo(path).Length == 0)", StringComparison.Ordinal)
                  && directFixture.Contains("FileMode.Create", StringComparison.Ordinal),
                "164-57A-3: direct DataLoader performance fixture is reused on warm runs");
            Check(recordScenario.Contains("using var ms = new MemoryStream(EstimateMcapRecordCapacity(topics, outer));", StringComparison.Ordinal)
                  && source.Contains("private static int EstimateMcapRecordCapacity(int topics, int messages)", StringComparison.Ordinal),
                "164-57A-4: measured MCAP record scenario uses a bounded MemoryStream capacity estimate");
        }

        private static void VerifyFoxRunAggregationReferencesAreCached()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Unit/FoxRun/FoxRunAggregationEmitterTests.cs");

            Check(source.Contains("private static readonly MetadataReference[] BasicReferences", StringComparison.Ordinal)
                  && source.Contains("BasicReferences,", StringComparison.Ordinal)
                  && !source.Contains("private static MetadataReference[] BasicReferences()", StringComparison.Ordinal),
                "164-57B-1: FoxRun aggregation generator tests reuse immutable metadata references");
        }

        private static void VerifyMcapConformanceSingleRecordSerialization()
        {
            var json = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/McapConformance/McapConformanceJson.cs");
            var reader = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/McapConformance/McapConformanceReader.cs");
            var serializeRecords = ExtractMethodBody(reader, "private static List<string> SerializeRecords(");

            Check(json.Contains("public static string WriteSingle(SerializableMcapRecord record)", StringComparison.Ordinal)
                  && serializeRecords.Contains(".Select(McapConformanceJson.WriteSingle)", StringComparison.Ordinal)
                  && !serializeRecords.Contains("new List<SerializableMcapRecord>", StringComparison.Ordinal),
                "164-57C-1: conformance reader serializes single records without per-record list wrappers");
        }

        private static void VerifyPhaseValidationCaseAvoidsLinqMatches()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationCase.cs");
            var matches = ExtractMethodBody(source, "public bool Matches(IReadOnlyCollection<string> args)");

            Check(!source.Contains("using System.Linq;", StringComparison.Ordinal)
                  && matches.Contains("ContainsFlag(args, Flag)", StringComparison.Ordinal)
                  && matches.Contains("for (var i = 0; i < Aliases.Count; i++)", StringComparison.Ordinal)
                  && !matches.Contains("AllFlags().Any", StringComparison.Ordinal),
                "164-57D-1: validation case matching avoids LINQ delegate allocation");
        }

        private static void VerifyRegistryAndProjectWiring()
        {
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");

            Check(registry.Contains("Ci(\"--phase164-57\", \"Phase 164-57\", Phase164_57Validation.Validate, includeInDefault: false)", StringComparison.Ordinal)
                  && project.Contains("Phase164_57Validation.cs", StringComparison.Ordinal),
                "164-57E-1: validation registry and project compile Phase164-57");
        }

        private static string ExtractMethodBody(string source, string signature)
        {
            var start = source.IndexOf(signature, StringComparison.Ordinal);
            if (start < 0)
                return string.Empty;
            var brace = source.IndexOf('{', start);
            if (brace < 0)
                return string.Empty;

            var depth = 0;
            for (var i = brace; i < source.Length; i++)
            {
                if (source[i] == '{')
                    depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return source.Substring(brace, i - brace + 1);
                }
            }

            return string.Empty;
        }

        private static int Count(string source, string value)
        {
            var count = 0;
            var index = 0;
            while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += value.Length;
            }

            return count;
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot()
                ?? throw new InvalidOperationException("Could not find repository root.");
            return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + label);

            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
