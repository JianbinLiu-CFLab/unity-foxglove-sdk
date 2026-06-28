// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase163-37 SDK sample and public package example review closure.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase163_37Validation
    {
        public static void Validate()
        {
            var repoRoot = Phase16Validation.FindRepoRoot()
                           ?? throw new DirectoryNotFoundException("Could not locate repository root.");

            VerifyPhase17SampleCoverage(repoRoot);
            VerifyPhase16RootAndBoundaryChecks(repoRoot);
            VerifyHarnessGuards(repoRoot);
            VerifyWiring(repoRoot);

            Console.WriteLine("Phase 163-37: SDK sample validation checks passed.");
        }

        private static void VerifyPhase17SampleCoverage(string repoRoot)
        {
            var phase17 = Read(repoRoot, "Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase17Validation.cs");

            Check(phase17.Contains("var lidarMazeDir = Path.Combine(samplesDir, \"Virtual LiDAR Maze Demo\");", StringComparison.Ordinal)
                  && phase17.Contains("Virtual LiDAR Maze Demo bootstrap exists", StringComparison.Ordinal)
                  && phase17.Contains("var lidarForbidden = new[] { \"Generated\", \"TutorialInfo\", \"Plugins\", \"Library\", \"Logs\", \"Recordings\" };", StringComparison.Ordinal)
                  && phase17.Contains("new[] { basicDir, fullDir, ros2Dir, lidarMazeDir }", StringComparison.Ordinal),
                "163-37A-1: Phase17 scans the Virtual LiDAR Maze Demo sample for hygiene and local paths");

            Check(phase17.Contains("Assembly-CSharp::FoxRunTriggerTelemetrySmoke", StringComparison.Ordinal)
                  && phase17.Contains("Assembly-CSharp::Phase53FoxRunTriggerSmoke", StringComparison.Ordinal),
                "163-37A-2: Phase17 checks current FullDemo FoxRun trigger positively and old trigger negatively");

            Check(phase17.Contains("NormalizeNewlines(configContent) == NormalizeNewlines(sampleContent)", StringComparison.Ordinal)
                  && phase17.Contains("static string NormalizeNewlines", StringComparison.Ordinal),
                "163-37A-3: Phase17 layout comparison ignores CRLF/LF-only drift");
        }

        private static void VerifyPhase16RootAndBoundaryChecks(string repoRoot)
        {
            var phase16 = Read(repoRoot, "Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase16Validation.cs");

            Check(!phase16.Contains("閳光偓", StringComparison.Ordinal)
                  && phase16.Contains("// --- 16A: Package metadata ---", StringComparison.Ordinal),
                "163-37B-1: Phase16 section comments are ASCII-readable");

            Check(phase16.Contains("File.Exists(Path.Combine(dir, \"README.md\"))", StringComparison.Ordinal)
                  && phase16.Contains("Directory.Exists(Path.Combine(dir, \"Unity2Foxglove\"))", StringComparison.Ordinal)
                  && phase16.Contains("Directory.Exists(Path.Combine(dir, \"Packages\"))", StringComparison.Ordinal)
                  && !phase16.Contains("File.Exists(Path.Combine(dir, \".gitignore\"))", StringComparison.Ordinal),
                "163-37B-2: Phase16 repo root finder uses project landmarks instead of .gitignore only");

            Check(phase16.Contains("Assert(false, \"git is available for tracked private workspace boundary checks\")", StringComparison.Ordinal)
                  && !phase16.Contains("skipping tracked private workspace boundary checks", StringComparison.Ordinal),
                "163-37B-3: private workspace boundary validation fails closed when git is unavailable");
        }

        private static void VerifyHarnessGuards(string repoRoot)
        {
            var testSources = Read(repoRoot, "Packages/dev.unity2foxglove.sdk/Tests/Unit/Harness/RuntimeValidationOptimizationTests.cs");
            Check(testSources.Contains("Assert.True(start >= 0, \"Could not locate source slice start: \" + startText);", StringComparison.Ordinal),
                "163-37C-1: TestSources.Slice fails when a start marker is missing");

            var samplesTests = Read(repoRoot, "Packages/dev.unity2foxglove.sdk/Tests/Unit/Architecture/UnityDemoSamplesAssetsTests.cs");
            Check(samplesTests.Contains("Assert.Contains(\".GroupBy(\", source", StringComparison.Ordinal)
                  && samplesTests.Contains("Assert.Contains(\".AsmName\", source", StringComparison.Ordinal)
                  && !samplesTests.Contains(".GroupBy(t => t.AsmName", StringComparison.Ordinal),
                "163-37C-2: FoxRun link XML grouping test avoids lambda variable-name coupling");
        }

        private static void VerifyWiring(string repoRoot)
        {
            var project = Read(repoRoot, "Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var registry = Read(repoRoot, "Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            Check(project.Contains("Phase163_37Validation.cs", StringComparison.Ordinal),
                "163-37D-1: runtime test project compiles Phase163_37Validation");
            Check(registry.Contains("Ci(\"--phase163-37\", \"Phase 163-37\", Phase163_37Validation.Validate", StringComparison.Ordinal),
                "163-37D-2: validation registry exposes --phase163-37");
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
