// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase163-56 review regression coverage for runtime validation hygiene.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase163_56Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 163-56 Tests ---");
            _passed = 0;

            VerifyRuntimeProjectChecksAreRuntimeAgnostic();
            VerifyRepoRootAnchoredReads();
            VerifyOrphanedPhase140MetasAreAbsent();
            VerifyLifecycleAndReflectionDiagnostics();
            VerifyLabelsAndRegistryOrdering();

            Console.WriteLine("Phase 163-56: " + _passed + " checks passed.\n");
        }

        private static void VerifyRuntimeProjectChecksAreRuntimeAgnostic()
        {
            var humble = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/R2fuHumbleRuntimePackageValidation.cs");
            var jazzy = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/R2fuJazzyRuntimeRefreshValidation.cs");

            Check(!humble.Contains("manifestRuntimes[0] == \"dev.unity2foxglove.ros2forunity.runtime.humble.win64\"", StringComparison.Ordinal)
                  && !humble.Contains("lockRuntimes[0] == \"dev.unity2foxglove.ros2forunity.runtime.humble.win64\"", StringComparison.Ordinal)
                  && humble.Contains("ExpectedRuntimeId(activeRuntimePackage)", StringComparison.Ordinal),
                "163-56A-1: Phase160 active Unity project runtime check is runtime-agnostic");
            Check(!jazzy.Contains("manifestRuntimes[0] == \"dev.unity2foxglove.ros2forunity.runtime.jazzy.win64\"", StringComparison.Ordinal)
                  && !jazzy.Contains("lockRuntimes[0] == \"dev.unity2foxglove.ros2forunity.runtime.jazzy.win64\"", StringComparison.Ordinal)
                  && jazzy.Contains("ExpectedRuntimeId(activeRuntimePackage)", StringComparison.Ordinal),
                "163-56A-2: Phase161 active Unity project runtime check is runtime-agnostic");
        }

        private static void VerifyRepoRootAnchoredReads()
        {
            var phase14017 = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase140_17Validation.cs");
            var phase140J = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase140JValidation.cs");

            Check(phase14017.Contains("Phase16Validation.FindRepoRoot()", StringComparison.Ordinal)
                  && !phase14017.Contains("=> File.ReadAllText(path);", StringComparison.Ordinal),
                "163-56B-1: Phase140-17 source reads are anchored at the repository root");
            Check(phase140J.Contains("Phase16Validation.FindRepoRoot()", StringComparison.Ordinal)
                  && !phase140J.Contains("=> File.ReadAllText(path);", StringComparison.Ordinal),
                "163-56B-2: Phase140J source reads are anchored at the repository root");
        }

        private static void VerifyOrphanedPhase140MetasAreAbsent()
        {
            foreach (var phase in new[] { "21", "22", "23", "24", "27" })
            {
                var relativePath = "Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase140_" + phase + "Validation.cs.meta";
                Check(!RepoFileExists(relativePath),
                    "163-56C: orphaned " + relativePath + " is absent");
            }
        }

        private static void VerifyLifecycleAndReflectionDiagnostics()
        {
            var phase148 = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase148Validation.cs");
            var phase149B = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase149BValidation.cs");
            var phase156 = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase156Validation.cs");
            var phase157 = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase157Validation.cs");

            Check(phase148.Contains("using var runtime = new FoxgloveRuntime", StringComparison.Ordinal),
                "163-56D-1: Phase148 runtime lifecycle check disposes the runtime on failure");
            Check(phase149B.Contains("field.FieldType", StringComparison.Ordinal)
                  && phase149B.Contains("field.GetValue(writer) is not IDisposable sourceStream", StringComparison.Ordinal),
                "163-56D-2: Phase149B reflection seam reports field type and null-state drift explicitly");
            Check(phase156.Contains("VerifyOptionalPackagePresent()", StringComparison.Ordinal)
                  && phase157.Contains("VerifyOptionalPackagePresent()", StringComparison.Ordinal),
                "163-56D-3: Phase156 and Phase157 report optional package absence through an explicit guard");
        }

        private static void VerifyLabelsAndRegistryOrdering()
        {
            var phase143 = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase143Validation.cs");
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");

            Check(phase143.Contains("included in default runs when local evidence is enabled", StringComparison.Ordinal),
                "163-56E-1: Phase143 registry label matches LocalEvidence default-run semantics");
            Check(phase143.Contains("\"Packages/dev.unity2foxglove.ros2forunity.runtime.\"", StringComparison.Ordinal)
                  && !phase143.Contains("\"Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64/\"", StringComparison.Ordinal),
                "163-56E-2: Phase143 artifact hygiene allows managed runtime packages by naming convention");

            var phase137F = registry.IndexOf("Ci(\"--phase137f\"", StringComparison.Ordinal);
            var phase137G = registry.IndexOf("Ci(\"--phase137g\"", StringComparison.Ordinal);
            var phase137 = registry.IndexOf("Ci(\"--phase137\", \"Phase 137\"", StringComparison.Ordinal);
            Check(phase137F >= 0 && phase137G > phase137F && phase137 > phase137G,
                "163-56E-3: Phase137G registry entry stays grouped with the Phase137 series");
            Check(registry.Contains("Ci(\"--phase163-56\", \"Phase 163-56\", Phase163_56Validation.Validate", StringComparison.Ordinal),
                "163-56E-4: validation registry exposes Phase163-56");
        }

        private static bool RepoFileExists(string relativePath)
            => File.Exists(RepoPath(relativePath));

        private static string ReadRepoText(string relativePath)
            => File.ReadAllText(RepoPath(relativePath));

        private static string RepoPath(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new DirectoryNotFoundException("Could not locate repository root.");
            return Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
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
