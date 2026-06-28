// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase163-45 early protocol and runtime validation review closure.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase163_45Validation
    {
        public static void Validate()
        {
            var repoRoot = Phase16Validation.FindRepoRoot()
                           ?? throw new DirectoryNotFoundException("Could not locate repository root.");

            VerifyPhase16(repoRoot);
            VerifyEarlyProtocolValidations(repoRoot);
            VerifySampleValidation(repoRoot);
            VerifyWiring(repoRoot);

            Console.WriteLine("Phase 163-45: early protocol/runtime validation checks passed.");
        }

        private static void VerifyPhase16(string repoRoot)
        {
            var phase16 = Read(repoRoot, "Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase16Validation.cs");
            Check(!phase16.Contains("鈥?", StringComparison.Ordinal)
                  && !phase16.Contains("閳", StringComparison.Ordinal)
                  && !phase16.Contains("闁", StringComparison.Ordinal),
                "163-45A-1: Phase16 user-facing validation text is free of known mojibake tokens");
            Check(phase16.Contains("Could not find repo root - skipping path-based checks.", StringComparison.Ordinal),
                "163-45A-2: Phase16 repo-root warning is readable ASCII");
            Check(phase16.Contains("Packages\", \"dev.unity2foxglove.sdk\", \"package.json", StringComparison.Ordinal)
                  && phase16.Contains("Directory.Exists(Path.Combine(dir, \"Unity2Foxglove\"))", StringComparison.Ordinal),
                "163-45A-3: Phase16 repo root detection uses repository-specific sentinels");
            Check(!phase16.Contains("Phase32Validation.cs has module header", StringComparison.Ordinal),
                "163-45A-4: Phase16 no longer owns Phase32-specific header checks");
            Check(phase16.Contains("HasInlinePythonDocstring", StringComparison.Ordinal),
                "163-45A-5: Phase16 accepts inline Python docstrings");

            var registry = Read(repoRoot, "Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            Check(registry.Contains("Phase 15 had no standalone validation file", StringComparison.Ordinal),
                "163-45A-6: Phase15 validation gap is documented in the registry");
        }

        private static void VerifyEarlyProtocolValidations(string repoRoot)
        {
            var phase2 = Read(repoRoot, "Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase2Validation.cs");
            Check(phase2.Contains("Publish after re-register preserves original subscriptionId", StringComparison.Ordinal)
                  && phase2.Contains("BitConverter.ToUInt32(binaries[0], 1)", StringComparison.Ordinal),
                "163-45B-1: Phase2 re-register test verifies the binary subscription id");

            var phase5 = Read(repoRoot, "Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase5Validation.cs");
            Check(phase5.Contains("SubscriberCount", StringComparison.Ordinal)
                  && phase5.Contains("Session dispose unbinds transport event handlers", StringComparison.Ordinal),
                "163-45B-2: Phase5 dispose test observes event-handler unbinding");

            var phase9 = Read(repoRoot, "Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase9Validation.cs");
            Check(phase9.Contains("Playing clock advances before pause", StringComparison.Ordinal),
                "163-45B-3: Phase9 playback clock test proves time advanced before pause");
            Check(phase9.Contains("http://somewhere/file", StringComparison.Ordinal)
                  && !phase9.Contains("Contains(\"Asset\", StringComparison.OrdinalIgnoreCase)", StringComparison.Ordinal),
                "163-45B-4: Phase9 fetchAsset error assertion is specific");
        }

        private static void VerifySampleValidation(string repoRoot)
        {
            var phase17 = Read(repoRoot, "Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase17Validation.cs");
            Check(phase17.Contains("NormalizeNewlines(configContent) == NormalizeNewlines(sampleContent)", StringComparison.Ordinal),
                "163-45C-1: Phase17 layout comparison normalizes line endings");
            Check(phase17.Contains("TryReadTextFile", StringComparison.Ordinal)
                  && phase17.Contains("catch (IOException ex)", StringComparison.Ordinal)
                  && !phase17.Contains("catch {", StringComparison.Ordinal),
                "163-45C-2: Phase17 absolute-path scans avoid broad catch-all handlers");
        }

        private static void VerifyWiring(string repoRoot)
        {
            var project = Read(repoRoot, "Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var registry = Read(repoRoot, "Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");

            Check(project.Contains("Phase163_45Validation.cs", StringComparison.Ordinal),
                "163-45D-1: runtime test project compiles Phase163_45Validation");
            Check(registry.Contains("Ci(\"--phase163-45\", \"Phase 163-45\", Phase163_45Validation.Validate", StringComparison.Ordinal),
                "163-45D-2: validation registry exposes --phase163-45");
        }

        private static string Read(string repoRoot, string relativePath)
            => File.ReadAllText(Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        private static void Check(bool condition, string description)
        {
            if (!condition)
                throw new Exception("[FAIL] " + description);

            Console.WriteLine("[PASS] " + description);
        }
    }
}
