// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 140-96 source-shape validation for performance and conformance test optimizations.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase140_96Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 140-96: Current Regression Performance And Conformance Tests Optimization ===");
            _passed = 0;

            ValidateDeclaredRuntimeScopeIsEmpty();
            ValidateMcapConformanceReaderReadsOnce();
            ValidateMcapConformanceJsonAvoidsLinqRecordSort();
            ValidatePerformancePayloadOptimizationWasNotAppliedUnsafely();
            ValidateRegistration();

            Console.WriteLine($"Phase 140-96: {_passed} checks passed.");
        }

        private static void ValidateDeclaredRuntimeScopeIsEmpty()
        {
            var runtimeRoot = Path.Combine("Packages", "dev.unity2foxglove.sdk", "Tests", "Runtime");
            Check(Directory.GetFiles(runtimeRoot, "*Regression*.cs", SearchOption.AllDirectories).Length == 0
                  && Directory.GetFiles(runtimeRoot, "*Performance*.cs", SearchOption.AllDirectories).Length == 0
                  && Directory.GetFiles(runtimeRoot, "*Conformance*.cs", SearchOption.AllDirectories).Length == 0,
                "140-96A-1: declared Runtime regression/performance/conformance scope remains empty");
        }

        private static void ValidateMcapConformanceReaderReadsOnce()
        {
            var source = ReadConformance("McapConformanceReader.cs");
            var method = ExtractMethod(source, "public static List<SerializableMcapRecord> ReadStreamed(string filePath)");
            Check(method.Contains("var data = File.ReadAllBytes(filePath);", StringComparison.Ordinal)
                  && method.Contains("new MemoryStream(data", StringComparison.Ordinal)
                  && method.Contains("new Scanner(data)", StringComparison.Ordinal)
                  && Count(method, "File.ReadAllBytes(filePath)") == 1
                  && !method.Contains("File.OpenRead(filePath)", StringComparison.Ordinal),
                "140-96B-1: McapConformanceReader streams and scans from one file read");
        }

        private static void ValidateMcapConformanceJsonAvoidsLinqRecordSort()
        {
            var source = ReadConformance("McapConformanceJson.cs");
            var method = ExtractMethod(source, "public static SerializableMcapRecord Record(string type, params Field[] fields)");
            Check(method.Contains("Array.Sort(fields", StringComparison.Ordinal)
                  && method.Contains("new List<object[]>(fields.Length)", StringComparison.Ordinal)
                  && !method.Contains(".OrderBy(", StringComparison.Ordinal)
                  && !method.Contains(".Select(", StringComparison.Ordinal),
                "140-96C-1: McapConformanceJson.Record sorts fields without LINQ allocations");
        }

        private static void ValidatePerformancePayloadOptimizationWasNotAppliedUnsafely()
        {
            var source = ReadPerformance("PerformanceRunner.cs");
            var payloadMethod = ExtractMethod(source, "private static byte[] MakeJsonPayload(int topicIdx, int msgIdx)");
            var fanoutMethod = ExtractMethod(source, "private static PerformanceScenarioResult RunPublishJsonFanout(");
            Check(payloadMethod.Contains("seq = msgIdx", StringComparison.Ordinal)
                  && fanoutMethod.Contains("MakeJsonPayload(t, i)", StringComparison.Ordinal),
                "140-96D-1: performance payloads keep per-message seq data instead of topic-only prebuild");
        }

        private static void ValidateRegistration()
        {
            var registry = ReadRuntimeTest("PhaseValidationRegistry.cs");
            var project = ReadRuntimeTest("FoxgloveSdk.Tests.csproj");
            Check(registry.Contains("\"--phase140-96\"", StringComparison.Ordinal)
                  && registry.Contains("Phase140_96Validation.Validate", StringComparison.Ordinal),
                "140-96E-1: validation registry exposes --phase140-96");
            Check(project.Contains("Phase140_96Validation.cs", StringComparison.Ordinal),
                "140-96E-2: test project compiles Phase140_96Validation");
        }

        private static string ReadRuntimeTest(string fileName)
            => File.ReadAllText(Path.Combine("Packages", "dev.unity2foxglove.sdk", "Tests", "Runtime", fileName));

        private static string ReadConformance(string fileName)
            => File.ReadAllText(Path.Combine("Packages", "dev.unity2foxglove.sdk", "Tests", "McapConformance", fileName));

        private static string ReadPerformance(string fileName)
            => File.ReadAllText(Path.Combine("Packages", "dev.unity2foxglove.sdk", "Tests", "Performance", fileName));

        private static string ExtractMethod(string source, string signature)
        {
            var index = source.IndexOf(signature, StringComparison.Ordinal);
            if (index < 0)
                return string.Empty;

            var brace = source.IndexOf('{', index);
            if (brace < 0)
                return string.Empty;

            var depth = 0;
            for (var i = brace; i < source.Length; i++)
            {
                if (source[i] == '{')
                {
                    depth++;
                }
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return source.Substring(index, i - index + 1);
                }
            }

            return source.Substring(index);
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

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + label);

            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
