// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 163-53 review follow-up guard for Phase 134/137 validation robustness.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Source-shape validation for Phase 163-53 review fixes.
    /// </summary>
    public static class Phase163_53Validation
    {
        private static int _passed;

        /// <summary>
        /// Validates that Phase 134/137 review fixes remain in place.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 163-53: Phase 134/137 Validation Robustness ===");
            _passed = 0;

            VerifyNegativeIndexGuards();
            VerifyRepositoryRootAndFileGuards();
            VerifyRuntimeTypeResolution();
            VerifyStringComparisonHardening();
            VerifyPhase137GExplicitOptIn();
            VerifyRegistryAndProjectWiring();

            Console.WriteLine($"Phase 163-53: {_passed} checks passed.");
        }

        private static void VerifyNegativeIndexGuards()
        {
            var phase1342 = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase134_2Validation.cs");
            Check(phase1342.Contains("var getWireIndex = parameterStore.IndexOf(\"GetWireParameters\"", StringComparison.Ordinal)
                  && phase1342.Contains("? parameterStore.IndexOf(\"lock (_lock)\", getWireIndex", StringComparison.Ordinal)
                  && phase1342.Contains("foreachIndex >= 0 && lockIndex >= 0", StringComparison.Ordinal),
                "163-53A-1: Phase134-2 guards nested IndexOf anchors before comparing ordering");

            var phase13414 = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase134_14Validation.cs");
            Check(phase13414.Contains("if (tryEncodeIndex >= 0)", StringComparison.Ordinal)
                  && phase13414.Contains("var validateIndex = -1;", StringComparison.Ordinal)
                  && phase13414.Contains("var buildIndex = int.MaxValue;", StringComparison.Ordinal),
                "163-53A-2: Phase134-14 avoids using a missing TryEncode anchor as a start index");

            var phase137f = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase137FValidation.cs");
            Check(phase137f.Contains("var detachRollback = catchIndex >= 0", StringComparison.Ordinal)
                  && phase137f.Contains(": -1;", StringComparison.Ordinal),
                "163-53A-3: Phase137F guards missing catch anchors before rollback search");
        }

        private static void VerifyRepositoryRootAndFileGuards()
        {
            var phase13435 = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase134_35Validation.cs");
            Check(phase13435.Contains("Phase16Validation.FindRepoRoot()", StringComparison.Ordinal)
                  && !phase13435.Contains("&& Directory.Exists(Path.Combine(dir.FullName, \"Scripts\"))", StringComparison.Ordinal),
                "163-53B-1: Phase134-35 uses the shared repository root helper");

            var phase137 = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase137Validation.cs");
            Check(phase137.Contains("Check(repoRoot != null, \"137-0: repository root located\")", StringComparison.Ordinal)
                  && phase137.Contains("Check(File.Exists(csprojPath), \"137-9: runtime validation csproj exists\")", StringComparison.Ordinal),
                "163-53B-2: Phase137 validates repo root and csproj existence before path-dependent reads");

            var phase137c = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase137CValidation.cs");
            Check(phase137c.Contains("private static string RepoPath(string relativePath)", StringComparison.Ordinal)
                  && phase137c.Contains("Phase16Validation.FindRepoRoot()", StringComparison.Ordinal)
                  && phase137c.Contains("Check(File.Exists(entryPath), \"137C-7: FoxgloveSourceEmitter entry source exists\")", StringComparison.Ordinal),
                "163-53B-3: Phase137C resolves repository paths and checks files before reading");

            var phase137g = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase137GValidation.cs");
            Check(phase137g.Contains("if (repoRoot == null)", StringComparison.Ordinal)
                  && phase137g.Contains("Could not find repository root for Phase137G validation", StringComparison.Ordinal),
                "163-53B-4: Phase137G fails clearly when the repository root cannot be found");
        }

        private static void VerifyRuntimeTypeResolution()
        {
            var phase137 = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase137Validation.cs");
            Check(phase137.Contains("AppDomain.CurrentDomain.GetAssemblies()", StringComparison.Ordinal)
                  && phase137.Contains("assembly.GetType(typeName, throwOnError: false, ignoreCase: false)", StringComparison.Ordinal)
                  && !phase137.Contains("Type.GetType(typeName)", StringComparison.Ordinal),
                "163-53C-1: Phase137 resolves DTO types from loaded assemblies instead of Type.GetType");
        }

        private static void VerifyStringComparisonHardening()
        {
            var phase137e = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase137EValidation.cs");
            Check(phase137e.Contains("Contains(\"public partial class FoxgloveManagerEditor\", StringComparison.Ordinal)", StringComparison.Ordinal)
                  && phase137e.Contains("Contains(\"[CustomEditor\", StringComparison.Ordinal)", StringComparison.Ordinal)
                  && phase137e.Contains("Contains(\"AssetRootDefinitionDrawer\", StringComparison.Ordinal)", StringComparison.Ordinal),
                "163-53D-1: Phase137E source checks use explicit ordinal string comparisons");
        }

        private static void VerifyPhase137GExplicitOptIn()
        {
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var entry = PhaseValidationRegistry.Find(new[] { "--phase137g" });

            Check(registry.Contains("Phase 137G is an explicit governance audit", StringComparison.Ordinal)
                  && entry != null
                  && entry.Category == ValidationCategory.CiSafe
                  && entry.Evidence == ValidationEvidence.Structural
                  && !entry.IncludeInDefault
                  && entry.Run == (Action)Phase137GValidation.Validate,
                "163-53E-1: Phase137G remains explicit opt-in with the baseline limitation documented");
        }

        private static void VerifyRegistryAndProjectWiring()
        {
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var entry = PhaseValidationRegistry.Find(new[] { "--phase163-53" });

            Check(project.Contains("Phase163_53Validation.cs", StringComparison.Ordinal)
                  && entry != null
                  && entry.Name == "Phase 163-53: review follow-up guard for Phase 134/137 validation robustness"
                  && entry.Category == ValidationCategory.CiSafe
                  && !entry.IncludeInDefault
                  && entry.Run == (Action)Validate,
                "163-53F-1: Phase163-53 validation is compiled and registered");
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot()
                ?? throw new InvalidOperationException("Could not find repository root.");
            var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            return File.ReadAllText(path);
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
