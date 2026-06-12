// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 140-95 source-shape validation for end-to-end remote timeline test optimizations.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase140_95Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 140-95: End-to-End Remote Timeline Tests Optimization ===");
            _passed = 0;

            ValidatePhase139ReadersUseCaches();
            ValidatePhase139DReadsServerSourceOnce();
            ValidateExtensionDomOptimizationRemainsApplied();
            ValidateRegistration();

            Console.WriteLine($"Phase 140-95: {_passed} checks passed.");
        }

        private static void ValidatePhase139ReadersUseCaches()
        {
            foreach (var file in new[]
            {
                "Phase139Validation.cs",
                "Phase139BValidation.cs",
                "Phase139CValidation.cs",
                "Phase139DValidation.cs"
            })
            {
                var source = ReadRuntimeTest(file);
                Check(source.Contains("private static readonly string CachedRepoRoot", StringComparison.Ordinal)
                      && source.Contains("private static readonly Dictionary<string, string> SourceCache", StringComparison.Ordinal)
                      && source.Contains("SourceCache.TryGetValue", StringComparison.Ordinal),
                    "140-95A-1: " + file + " caches repo root and repeated source reads");
            }
        }

        private static void ValidatePhase139DReadsServerSourceOnce()
        {
            var source = ReadRuntimeTest("Phase139DValidation.cs");
            Check(Count(source, "FoxgloveManager.Server.cs") == 1,
                "140-95B-1: Phase139D reads FoxgloveManager.Server.cs through one cached path");
        }

        private static void ValidateExtensionDomOptimizationRemainsApplied()
        {
            var source = File.ReadAllText(Path.Combine(
                "Tools", "foxglove-extensions", "unity-cursor-bridge", "src", "index.ts"));
            var render = ExtractFunction(source, "context.onRender =");
            Check(source.Contains("const MIN_INTERVAL_MS = 1000 / DEFAULT_MAX_HZ;", StringComparison.Ordinal)
                  && source.Contains("function buildPanelDom", StringComparison.Ordinal)
                  && render.Contains("panel.replayTime.textContent", StringComparison.Ordinal)
                  && !render.Contains("replaceChildren", StringComparison.Ordinal),
                "140-95C-1: cursor bridge keeps one-time DOM construction out of onRender");
        }

        private static void ValidateRegistration()
        {
            var registry = ReadRuntimeTest("PhaseValidationRegistry.cs");
            var project = ReadRuntimeTest("FoxgloveSdk.Tests.csproj");
            Check(registry.Contains("\"--phase140-95\"", StringComparison.Ordinal)
                  && registry.Contains("Phase140_95Validation.Validate", StringComparison.Ordinal),
                "140-95D-1: validation registry exposes --phase140-95");
            Check(project.Contains("Phase140_95Validation.cs", StringComparison.Ordinal),
                "140-95D-2: test project compiles Phase140_95Validation");
        }

        private static string ReadRuntimeTest(string fileName)
            => File.ReadAllText(Path.Combine("Packages", "dev.unity2foxglove.sdk", "Tests", "Runtime", fileName));

        private static string ExtractFunction(string source, string signature)
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
