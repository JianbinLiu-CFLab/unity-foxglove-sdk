// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 164-56 optimization guards for latest runtime validations.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>Source-shape validation for Phase 164-56 validation-path optimizations.</summary>
    public static class Phase164_56Validation
    {
        private static int _passed;

        /// <summary>Runs Phase 164-56 validation checks.</summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("--- Phase 164-56 Tests ---");
            _passed = 0;

            VerifyPhase151MarkerValidationReadsEachFileOnce();
            VerifyPhase149IndexedFixturesAreCachedAsBytes();
            VerifyPhase140GCovarianceReaderUsesDirectArrays();
            VerifyR2fuLifecycleValidationCachesBridgeSources();
            VerifyRegistryAlreadyUsesFlagIndex();
            VerifyRegistryAndProjectWiring();

            Console.WriteLine($"Phase 164-56: {_passed} checks passed.");
        }

        private static void VerifyPhase151MarkerValidationReadsEachFileOnce()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase151Validation.cs");
            var method = ExtractMethodBody(source, "private static void VerifyPhase151BMarkerInstrumentation()");

            Check(method.Contains("checks.GroupBy(item => item.relativePath)", StringComparison.Ordinal)
                  && method.Contains("ReadRepoText(group.Key)", StringComparison.Ordinal)
                  && Count(method, "ReadRepoText(") == 1,
                "164-56A-1: Phase151 marker validation groups checks by file before reading source");
        }

        private static void VerifyPhase149IndexedFixturesAreCachedAsBytes()
        {
            var phase149A = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase149AValidation.cs");
            var phase149B = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase149BValidation.cs");

            Check(phase149A.Contains("private static byte[] _indexedFixtureBytes;", StringComparison.Ordinal)
                  && phase149A.Contains("_indexedFixtureBytes ??= CreateIndexedFixtureBytes();", StringComparison.Ordinal)
                  && phase149A.Contains("return new MemoryStream(_indexedFixtureBytes, writable: false);", StringComparison.Ordinal)
                  && phase149A.Contains("private static byte[] CreateIndexedFixtureBytes()", StringComparison.Ordinal)
                  && phase149A.Contains("return ms.ToArray();", StringComparison.Ordinal),
                "164-56B-1: Phase149A reuses indexed MCAP fixture bytes while preserving per-test streams");
            Check(phase149B.Contains("private static byte[] _indexedFixtureBytes;", StringComparison.Ordinal)
                  && phase149B.Contains("_indexedFixtureBytes ??= CreateIndexedFixtureBytes();", StringComparison.Ordinal)
                  && phase149B.Contains("File.WriteAllBytes(path, _indexedFixtureBytes);", StringComparison.Ordinal)
                  && phase149B.Contains("private static byte[] CreateIndexedFixtureBytes()", StringComparison.Ordinal)
                  && phase149B.Contains("return ms.ToArray();", StringComparison.Ordinal),
                "164-56B-2: Phase149B reuses indexed MCAP fixture bytes while preserving per-test files");
        }

        private static void VerifyPhase140GCovarianceReaderUsesDirectArrays()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase140GValidation.cs");
            var method = ExtractMethodBody(source, "private static Dictionary<int, double[]> ReadCovariances(byte[] payload)");

            Check(method.Contains("new Dictionary<int, double[]>(capacity: 3)", StringComparison.Ordinal)
                  && method.Contains("var values = new double[count];", StringComparison.Ordinal)
                  && method.Contains("values[i] = input.ReadDouble();", StringComparison.Ordinal)
                  && !method.Contains("new List<double>", StringComparison.Ordinal)
                  && !method.Contains("values.ToArray()", StringComparison.Ordinal),
                "164-56C-1: Phase140G covariance reader avoids List growth and array copies");
        }

        private static void VerifyR2fuLifecycleValidationCachesBridgeSources()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/R2fuJazzyRuntimeRefreshValidation.cs");
            var lifecycle = ExtractMethodBody(source, "public static int ValidateNativeBridgeLifecycleGuards(string labelPrefix)");

            Check(source.Contains("NativeBridgeLifecycleSourceCache", StringComparison.Ordinal)
                  && source.Contains("private static string ReadLifecycleSource(string path)", StringComparison.Ordinal)
                  && lifecycle.Contains("ReadLifecycleSource(path)", StringComparison.Ordinal)
                  && lifecycle.Contains("ReadLifecycleSource(sharedGatePath)", StringComparison.Ordinal)
                  && !lifecycle.Contains("File.ReadAllText(path)", StringComparison.Ordinal)
                  && !lifecycle.Contains("File.ReadAllText(sharedGatePath)", StringComparison.Ordinal),
                "164-56D-1: R2FU lifecycle validation caches bridge source reads across phase checks");
        }

        private static void VerifyRegistryAlreadyUsesFlagIndex()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");

            Check(source.Contains("IReadOnlyDictionary<string, PhaseValidationCase> FlagIndex", StringComparison.Ordinal)
                  && Count(source, "FlagIndex.TryGetValue(arg, out var validation)") >= 2,
                "164-56E-1: validation registry dispatch remains indexed instead of linear scanned");
        }

        private static void VerifyRegistryAndProjectWiring()
        {
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");

            Check(registry.Contains("Ci(\"--phase164-56\", \"Phase 164-56\", Phase164_56Validation.Validate, includeInDefault: false)", StringComparison.Ordinal)
                  && project.Contains("Phase164_56Validation.cs", StringComparison.Ordinal),
                "164-56F-1: validation registry and project compile Phase164-56");
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
