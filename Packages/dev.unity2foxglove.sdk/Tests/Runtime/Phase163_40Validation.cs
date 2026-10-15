// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase163-40 release, packaging, and CI script review closure.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase163_40Validation
    {
        public static void Validate()
        {
            var repoRoot = Phase16Validation.FindRepoRoot()
                           ?? throw new DirectoryNotFoundException("Could not locate repository root.");

            VerifyRunCi(repoRoot);
            VerifyVersionBump(repoRoot);
            VerifyPackageValidator(repoRoot);
            VerifyCiWorkflows(repoRoot);
            VerifyRegressionTests(repoRoot);
            VerifyWiring(repoRoot);

            Console.WriteLine("Phase 163-40: release and packaging checks passed.");
        }

        private static void VerifyRunCi(string repoRoot)
        {
            var source = Read(repoRoot, "Scripts/release/run_ci.py");

            Check(source.Contains("import sys", StringComparison.Ordinal),
                "163-40A-1: run_ci imports sys for current-interpreter subprocess calls");
            Check(source.Contains("[sys.executable, \"Scripts/package/validate_unity_package.py\"]", StringComparison.Ordinal)
                  && source.Contains("[sys.executable, \"Scripts/package/validate_local_entrypoints.py\"]", StringComparison.Ordinal)
                  && source.Contains("[sys.executable, SCHEMA_GENERATED_OUTPUT_VALIDATOR]", StringComparison.Ordinal)
                  && source.Contains("[sys.executable, SOURCE_GENERATOR_VALIDATOR]", StringComparison.Ordinal),
                "163-40A-2: run_ci package/analyzer validators use the current Python executable");
            Check(source.Contains("def run(cmd: list[str], label: str, *, fatal: bool = False) -> bool:", StringComparison.Ordinal)
                  && source.Contains("if fatal:\n            raise SystemExit(result.returncode)", StringComparison.Ordinal)
                  && source.Contains("run(project_cmd, label, fatal=False)", StringComparison.Ordinal),
                "163-40A-3: run_ci fatal mode aborts only intentional fatal commands");
        }

        private static void VerifyVersionBump(string repoRoot)
        {
            var source = Read(repoRoot, "Scripts/release/bump_version.py");

            Check(source.Contains("def resolve_repo_root() -> Path:", StringComparison.Ordinal)
                  && source.Contains("Unexpected repository root for bump_version.py", StringComparison.Ordinal),
                "163-40B-1: bump_version validates its computed repository root");
            Check(source.Contains("README release badge", StringComparison.Ordinal)
                  && source.Contains("README verified Windows version note", StringComparison.Ordinal)
                  && !source.Contains("text.replace(f\"release-v{old_version}\"", StringComparison.Ordinal),
                "163-40B-2: README version updates are anchored single replacements");
            Check(source.Contains("package README verified Windows version note", StringComparison.Ordinal)
                  && !source.Contains("text.replace(f\"verified for v{old_version}\"", StringComparison.Ordinal),
                "163-40B-3: package README version update is anchored");
            Check(source.Contains("re.search(r\"(?m)^---\\n\\n(?=## \\d+\\.\\d+\\.\\d+ - )\"", StringComparison.Ordinal)
                  && source.Contains("text[: insertion.end()] + entry + text[insertion.end() :]", StringComparison.Ordinal),
                "163-40B-4: changelog insertion is anchored to the version heading boundary");
        }

        private static void VerifyPackageValidator(string repoRoot)
        {
            var source = Read(repoRoot, "Scripts/package/validate_unity_package.py");
            var sourceGenerator = Read(repoRoot, "Scripts/package/validate_source_generator_dll.py");
            var localEntrypoints = Read(repoRoot, "Scripts/package/validate_local_entrypoints.py");

            Check(source.Contains("\".prefab\"", StringComparison.Ordinal)
                  && source.Contains("\".mat\"", StringComparison.Ordinal)
                  && source.Contains("\".png\"", StringComparison.Ordinal)
                  && source.Contains("\".rendertexture\"", StringComparison.Ordinal),
                "163-40C-1: package validator checks common Unity sample asset meta sidecars");
            Check(source.Contains("VERSION_RE = re.compile(r\"^\\d+\\.\\d+\\.\\d+$\")", StringComparison.Ordinal)
                  && source.Contains("VERSION_RE.match(version) is not None", StringComparison.Ordinal),
                "163-40C-2: package validator enforces semver package versions");
            Check(source.Contains("K4os.Compression.LZ4.dll", StringComparison.Ordinal)
                  && source.Contains("K4os.Compression.LZ4.Streams.dll", StringComparison.Ordinal)
                  && source.Contains("StbImageWriteSharp.dll", StringComparison.Ordinal)
                  && source.Contains("Unity2FoxgloveDracoNative.dll", StringComparison.Ordinal),
                "163-40C-3: package validator gates bundled runtime plugin notices");
            Check(sourceGenerator.Contains("except subprocess.CalledProcessError as exc:", StringComparison.Ordinal)
                  && sourceGenerator.Contains("[FAIL] Source generator Release build failed", StringComparison.Ordinal),
                "163-40C-4: source generator validator reports build failures without traceback");
            Check(localEntrypoints.Contains("THIS_SCRIPT = Path(__file__).resolve()", StringComparison.Ordinal)
                  && localEntrypoints.Contains("if path.resolve() == THIS_SCRIPT:", StringComparison.Ordinal),
                "163-40C-5: local entrypoint validator self-excludes by resolved path");
        }

        private static void VerifyCiWorkflows(string repoRoot)
        {
            var packageCheck = Read(repoRoot, ".github/workflows/package-check.yml");
            var dotnetTests = Read(repoRoot, ".github/workflows/dotnet-tests.yml");
            var docsCheck = Read(repoRoot, ".github/workflows/docs-check.yml");

            Check(packageCheck.Contains("Validate local ROS2/R2FU entrypoints", StringComparison.Ordinal)
                  && packageCheck.Contains("python3 Scripts/package/validate_local_entrypoints.py", StringComparison.Ordinal),
                "163-40D-1: package workflow runs local entrypoint validation");
            Check(dotnetTests.Contains("Upload xUnit test results", StringComparison.Ordinal)
                  && dotnetTests.Contains("actions/upload-artifact@v4", StringComparison.Ordinal)
                  && dotnetTests.Contains("path: build/TestResults/Unit", StringComparison.Ordinal),
                "163-40D-2: dotnet workflow uploads xUnit TRX artifacts");
            Check(docsCheck.Contains("if \"\\ufffd\" in text:", StringComparison.Ordinal)
                  && !docsCheck.Contains("\\u9201", StringComparison.Ordinal),
                "163-40D-3: docs mojibake check uses the standard replacement character");
        }

        private static void VerifyRegressionTests(string repoRoot)
        {
            var releaseTests = Read(repoRoot, "Scripts/release/regression_checks/test_release_tooling.py");
            var packageTests = Read(repoRoot, "Scripts/package/regression_checks/test_validate_unity_package.py");

            Check(releaseTests.Contains("test_package_validators_use_current_python_executable", StringComparison.Ordinal)
                  && releaseTests.Contains("test_fatal_run_raises_after_printing_failure", StringComparison.Ordinal)
                  && releaseTests.Contains("test_update_changelog_inserts_at_version_heading_not_first_rule", StringComparison.Ordinal),
                "163-40E-1: release tooling regressions cover Python executable, fatal mode, and changelog anchoring");
            Check(packageTests.Contains("test_sample_meta_checks_prefab_files", StringComparison.Ordinal)
                  && packageTests.Contains("test_package_version_must_be_semver", StringComparison.Ordinal)
                  && packageTests.Contains("test_third_party_notice_requirements_cover_runtime_plugin_dlls", StringComparison.Ordinal)
                  && packageTests.Contains("test_build_failure_returns_structured_failure", StringComparison.Ordinal),
                "163-40E-2: package validator regressions cover sample meta, semver, notices, and build diagnostics");
        }

        private static void VerifyWiring(string repoRoot)
        {
            var project = Read(repoRoot, "Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var registry = Read(repoRoot, "Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");

            Check(project.Contains("Phase163_40Validation.cs", StringComparison.Ordinal),
                "163-40F-1: runtime test project compiles Phase163_40Validation");
            Check(registry.Contains("Ci(\"--phase163-40\", \"Phase 163-40\", Phase163_40Validation.Validate", StringComparison.Ordinal),
                "163-40F-2: validation registry exposes --phase163-40");
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
