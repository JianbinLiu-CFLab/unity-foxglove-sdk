// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase163-58 regression coverage for isolated local CI dotnet build roots.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase163_58Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 163-58 Tests ---");
            _passed = 0;

            VerifyRunCiUsesUniqueDotnetRunRoot();
            VerifyRestoreAndNoRestoreCommandsShareMsbuildPaths();
            VerifyAnalyzerFreshnessUsesIsolatedOutput();
            VerifyXunitResultsAreRunScoped();
            VerifyRegistryAndCompileEntry();

            Console.WriteLine("Phase 163-58: " + _passed + " checks passed.\n");
        }

        private static void VerifyRunCiUsesUniqueDotnetRunRoot()
        {
            var runCi = ReadRepoText("Scripts/release/run_ci.py");

            Check(runCi.Contains("UNITY2FOXGLOVE_CI_RUN_ID", StringComparison.Ordinal)
                  && runCi.Contains("uuid.uuid4", StringComparison.Ordinal)
                  && runCi.Contains("ISOLATED_DOTNET_ROOT", StringComparison.Ordinal),
                "163-58A-1: local CI creates a unique per-process dotnet build root");
            Check(runCi.Contains("def dotnet_msbuild_props", StringComparison.Ordinal)
                  && runCi.Contains("BaseOutputPath", StringComparison.Ordinal)
                  && runCi.Contains("BaseIntermediateOutputPath", StringComparison.Ordinal)
                  && runCi.Contains("MSBuildProjectExtensionsPath", StringComparison.Ordinal)
                  && runCi.Contains("RestoreOutputPath", StringComparison.Ordinal),
                "163-58A-2: local CI passes the complete restore/run MSBuild path set");
        }

        private static void VerifyRestoreAndNoRestoreCommandsShareMsbuildPaths()
        {
            var runCi = ReadRepoText("Scripts/release/run_ci.py");

            Check(runCi.Contains("restore_with_ignoring_failed_sources(", StringComparison.Ordinal)
                  && runCi.Contains("msbuild_props: list[str] | None = None", StringComparison.Ordinal)
                  && runCi.Contains("*msbuild_props", StringComparison.Ordinal),
                "163-58B-1: restore helper accepts and forwards suite-specific MSBuild paths");
            Check(runCi.Contains("ANALYZER_PROPS = dotnet_msbuild_props(\"analyzer\")", StringComparison.Ordinal)
                  && runCi.Contains("RUNTIME_TEST_PROPS = dotnet_msbuild_props(\"runtime-tests\")", StringComparison.Ordinal)
                  && runCi.Contains("UNIT_TEST_PROPS = dotnet_msbuild_props(\"unit-tests\")", StringComparison.Ordinal),
                "163-58B-2: analyzer, runtime validation, and xUnit suites have separate build roots");
            Check(runCi.Contains("SOURCE_GENERATOR_PROJ, \"Restore Roslyn analyzer project\", ANALYZER_PROPS", StringComparison.Ordinal)
                  && runCi.Contains("RUNTIME_TESTS_PROJ, \"Restore runtime test project\", RUNTIME_TEST_PROPS", StringComparison.Ordinal)
                  && runCi.Contains("UNIT_TESTS_PROJ, \"Restore xUnit unit test project\", UNIT_TEST_PROPS", StringComparison.Ordinal),
                "163-58B-3: restore commands use the same suite props as their no-restore commands");
        }

        private static void VerifyAnalyzerFreshnessUsesIsolatedOutput()
        {
            var runCi = ReadRepoText("Scripts/release/run_ci.py");
            var validator = ReadRepoText("Scripts/package/validate_source_generator_dll.py");

            Check(runCi.Contains("ANALYZER_OUTPUT_DIR", StringComparison.Ordinal)
                  && runCi.Contains("--build-output-dir", StringComparison.Ordinal)
                  && !runCi.Contains("\"build/SourceGenerators/Release/netstandard2.0\"", StringComparison.Ordinal),
                "163-58C-1: run_ci.py builds analyzer DLLs into a run-scoped output directory");
            Check(validator.Contains("--build-output-dir", StringComparison.Ordinal)
                  && validator.Contains("build_output_dir: Path", StringComparison.Ordinal)
                  && validator.Contains("built_dll = build_output_dir / \"FoxgloveLogSourceGenerator.dll\"", StringComparison.Ordinal),
                "163-58C-2: source generator freshness validator accepts an explicit isolated output directory");
        }

        private static void VerifyXunitResultsAreRunScoped()
        {
            var runCi = ReadRepoText("Scripts/release/run_ci.py");

            Check(runCi.Contains("UNIT_TEST_RESULTS_DIR", StringComparison.Ordinal)
                  && runCi.Contains("build/ci", StringComparison.Ordinal)
                  && !runCi.Contains("\"build/TestResults/Unit\"", StringComparison.Ordinal),
                "163-58D-1: xUnit trx output is scoped to the same run root as dotnet build outputs");
        }

        private static void VerifyRegistryAndCompileEntry()
        {
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");

            Check(registry.Contains("Ci(\"--phase163-58\", \"Phase 163-58\", Phase163_58Validation.Validate", StringComparison.Ordinal),
                "163-58E-1: validation registry exposes Phase163-58");
            Check(project.Contains("<Compile Include=\"Phase163_58Validation.cs\" />", StringComparison.Ordinal),
                "163-58E-2: runtime validation project compiles Phase163-58");
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
