// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase163-57 review regression coverage for unit, conformance, and performance test hygiene.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase163_57Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 163-57 Tests ---");
            _passed = 0;

            VerifyServeModeIsCiGuarded();
            VerifyMcapConformanceIsVisibleInCi();
            VerifyPerformanceThresholdsDeclareScope();
            VerifyDualTrackValidationRemainsInCi();
            VerifyRegistryAndCompileEntry();

            Console.WriteLine("Phase 163-57: " + _passed + " checks passed.\n");
        }

        private static void VerifyServeModeIsCiGuarded()
        {
            var program = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Program.cs");
            var harnessTests = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Unit/Harness/RuntimeHarnessTests.cs");

            Check(program.Contains("IsCiEnvironment()", StringComparison.Ordinal)
                  && program.Contains("--serve is manual-only", StringComparison.Ordinal)
                  && program.Contains("\"GITHUB_ACTIONS\"", StringComparison.Ordinal)
                  && program.Contains("\"TF_BUILD\"", StringComparison.Ordinal),
                "163-57A-1: --serve is blocked in CI-like environments before starting the manual server");
            Check(harnessTests.Contains("ServeIsDisabledInCiEnvironment", StringComparison.Ordinal)
                  && harnessTests.Contains("[\"CI\"] = \"true\"", StringComparison.Ordinal),
                "163-57A-2: xUnit harness covers the CI --serve guard");
        }

        private static void VerifyMcapConformanceIsVisibleInCi()
        {
            var workflow = ReadRepoText(".github/workflows/dotnet-tests.yml");
            var runCi = ReadRepoText("Scripts/release/run_ci.py");

            Check(workflow.Contains("run_phase121_conformance.py", StringComparison.Ordinal)
                  && workflow.Contains("--release-blocking", StringComparison.Ordinal)
                  && workflow.Contains("Run official MCAP differential conformance", StringComparison.Ordinal),
                "163-57B-1: GitHub dotnet workflow runs release-blocking official MCAP differential conformance");
            Check(runCi.Contains("run_phase121_conformance.py", StringComparison.Ordinal)
                  && runCi.Contains("--release-blocking", StringComparison.Ordinal)
                  && runCi.Contains("mcap-conformance-differential", StringComparison.Ordinal),
                "163-57B-2: local run_ci.py runs release-blocking official MCAP differential conformance");
        }

        private static void VerifyPerformanceThresholdsDeclareScope()
        {
            var result = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Performance/PerformanceResult.cs");
            var runner = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Performance/PerformanceRunner.cs");
            var program = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Performance/Program.cs");
            var json = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Performance/performance-thresholds.json");
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Performance/FoxgloveSdk.Performance.csproj");

            Check(result.Contains("public string transportScope", StringComparison.Ordinal)
                  && result.Contains("public string calibratedOn", StringComparison.Ordinal),
                "163-57C-1: performance threshold config carries transport and calibration metadata");
            Check(runner.Contains("DefaultTransportScope", StringComparison.Ordinal)
                  && runner.Contains("FakePerformanceTransport serialization/dispatch path only", StringComparison.Ordinal),
                "163-57C-2: built-in performance thresholds identify the fake transport scope");
            Check(program.Contains("Performance transport:", StringComparison.Ordinal)
                  && program.Contains("thresholdCalibratedOn", StringComparison.Ordinal),
                "163-57C-3: performance runs print and serialize threshold scope metadata");
            Check(json.Contains("\"transportScope\"", StringComparison.Ordinal)
                  && json.Contains("\"calibratedOn\"", StringComparison.Ordinal)
                  && json.Contains("excludes ManagedWsBackend sockets and TLS", StringComparison.Ordinal),
                "163-57C-4: checked-in performance thresholds document their transport boundary");
            Check(project.Contains("UnityEngineMathTypes.cs", StringComparison.Ordinal),
                "163-57C-5: performance project carries UnityEngine math stubs required by its shared test surface");
        }

        private static void VerifyDualTrackValidationRemainsInCi()
        {
            var workflow = ReadRepoText(".github/workflows/dotnet-tests.yml");
            var runCi = ReadRepoText("Scripts/release/run_ci.py");

            Check(workflow.Contains("Run validation suite", StringComparison.Ordinal)
                  && workflow.Contains("Run xUnit unit tests", StringComparison.Ordinal),
                "163-57D-1: GitHub workflow keeps both runtime validation and xUnit tracks");
            Check(runCi.Contains("\"dotnet\"", StringComparison.Ordinal)
                  && runCi.Contains("\"xunit\"", StringComparison.Ordinal)
                  && runCi.Contains("Dotnet validation suite (default CI)", StringComparison.Ordinal)
                  && runCi.Contains("xUnit unit tests", StringComparison.Ordinal),
                "163-57D-2: local CI keeps both runtime validation and xUnit tracks");
        }

        private static void VerifyRegistryAndCompileEntry()
        {
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");

            Check(registry.Contains("Ci(\"--phase163-57\"", StringComparison.Ordinal)
                  && registry.Contains("Phase163_57Validation.Validate", StringComparison.Ordinal),
                "163-57E-1: validation registry exposes Phase163-57");
            Check(project.Contains("<Compile Include=\"Phase163_57Validation.cs\" />", StringComparison.Ordinal),
                "163-57E-2: runtime validation project compiles Phase163-57");
        }

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
