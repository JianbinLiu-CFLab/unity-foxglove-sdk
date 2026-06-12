// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 140-91 source-shape regression coverage for schema evidence and package test optimizations.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase140_91Validation.
    /// </summary>
    public static class Phase140_91Validation
    {
        private static int _passed;

        /// <summary>
        /// Validation method for Validate.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 140-91: Schema Evidence And Package Tests Optimization ===");
            _passed = 0;

            VerifyPhase110CachesRepoRootAndTokens();
            VerifyFixtureManifestCaching();
            VerifyPhase116CachesReflectionLookups();
            VerifyPhase122CachesOpcodeCounts();
            VerifyRegistration();

            Console.WriteLine($"Phase 140-91: {_passed} checks passed.");
        }

        private static void VerifyPhase110CachesRepoRootAndTokens()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase110Validation.cs");

            Check(source.Contains("private static string _repoRoot;", StringComparison.Ordinal)
                  && source.Contains("_repoRoot = root;", StringComparison.Ordinal),
                "140-91A-1: Phase110 caches repository root");
            Check(source.Contains("private static readonly string[] OptionalRuntimeForbiddenTokenList", StringComparison.Ordinal)
                  && source.Contains("private static readonly string[] CoreProductionForbiddenTokenList", StringComparison.Ordinal)
                  && source.Contains("private static readonly string[] R2fuReferenceTokens", StringComparison.Ordinal)
                  && !source.Contains("private static IEnumerable<string> OptionalRuntimeForbiddenTokens()", StringComparison.Ordinal),
                "140-91A-2: Phase110 reuses forbidden token arrays");
        }

        private static void VerifyFixtureManifestCaching()
        {
            var phase113 = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase113Validation.cs");
            var phase114 = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase114Validation.cs");

            Check(phase113.Contains("private static readonly Lazy<FoxRunCanonicalManifest> FixtureManifestCache", StringComparison.Ordinal)
                  && phase113.Contains("private static FoxRunCanonicalManifest FixtureManifest() => FixtureManifestCache.Value;", StringComparison.Ordinal),
                "140-91B-1: Phase113 reuses fixture manifest");
            Check(phase114.Contains("private static readonly Lazy<FoxRunCanonicalManifest> FixtureManifestCache", StringComparison.Ordinal)
                  && phase114.Contains("private static FoxRunCanonicalManifest FixtureManifest() => FixtureManifestCache.Value;", StringComparison.Ordinal),
                "140-91B-2: Phase114 reuses fixture manifest");
        }

        private static void VerifyPhase116CachesReflectionLookups()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase116Validation.cs");

            Check(source.Contains("private static readonly Dictionary<string, MethodInfo> MethodCache", StringComparison.Ordinal)
                  && source.Contains("private static readonly Dictionary<string, MemberInfo> MemberCache", StringComparison.Ordinal)
                  && source.Contains("MethodCache.TryGetValue", StringComparison.Ordinal)
                  && source.Contains("MemberCache.TryGetValue", StringComparison.Ordinal)
                  && !source.Contains(".GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)\r\n                .Where", StringComparison.Ordinal),
                "140-91C-1: Phase116 caches repeated reflection lookups");
        }

        private static void VerifyPhase122CachesOpcodeCounts()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase122Validation.cs");

            Check(source.Contains("private static Dictionary<byte, int> OpcodeCounts", StringComparison.Ordinal)
                  && source.Contains("counts.TryGetValue(opcode, out var count)", StringComparison.Ordinal)
                  && !source.Contains("=> records.Count(r => r.Opcode == opcode);", StringComparison.Ordinal),
                "140-91D-1: Phase122 reuses opcode counts per record array");
        }

        private static void VerifyRegistration()
        {
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(project.Contains("Phase140_91Validation.cs", StringComparison.Ordinal),
                "140-91E-1: test project compiles Phase140_91Validation");
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            Check(registry.Contains("\"--phase140-91\"", StringComparison.Ordinal)
                  && registry.Contains("Phase140_91Validation.Validate", StringComparison.Ordinal),
                "140-91E-2: validation registry exposes --phase140-91");
        }

        private static string Read(string relativePath)
            => File.ReadAllText(Path.Combine(RepoRoot(), relativePath));

        private static string RepoRoot()
        {
            var directory = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(directory))
            {
                if (Directory.Exists(Path.Combine(directory, ".git")))
                    return directory;
                directory = Directory.GetParent(directory)?.FullName;
            }

            throw new DirectoryNotFoundException("Could not locate repository root.");
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + label);
            Console.WriteLine("[PASS] " + label);
            _passed++;
        }
    }
}
