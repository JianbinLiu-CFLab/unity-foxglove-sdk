// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase164-1 optimization regression coverage for repository validation paths.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase164_1Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 164-1 Tests ---");
            _passed = 0;

            VerifyUnityPackageSampleScanIsShared();
            VerifyArchitectureTestsCacheRepoRoot();
            VerifyPhase16AvoidsReleaseVersionCoupling();
            VerifyPhase16BuildOutputScanIsTargeted();
            VerifySyncFullDemoValidatesTextBeforeEncoding();
            VerifyPhase17AvoidsRepeatedAbsolutePathNormalization();
            VerifyRegistryAndCompileEntry();

            Console.WriteLine("Phase 164-1: " + _passed + " checks passed.\n");
        }

        private static void VerifyUnityPackageSampleScanIsShared()
        {
            var validator = ReadRepoText("Scripts/package/validate_unity_package.py");

            Check(validator.Contains("samples_entries = list(SAMPLES.rglob(\"*\"))", StringComparison.Ordinal)
                  && validator.Contains("samples_files = [path for path in samples_entries if path.is_file()]", StringComparison.Ordinal)
                  && validator.Contains("check_sample_meta(results, samples_files)", StringComparison.Ordinal)
                  && validator.Contains("check_forbidden_public_content(results, samples_files)", StringComparison.Ordinal)
                  && validator.Contains("check_forbidden_sample_artifacts(results, samples_entries)", StringComparison.Ordinal),
                "164-1A-1: validate_unity_package.py materializes Samples~ entries once and shares derived lists");
            Check(validator.Contains("def check_sample_meta(results: list[CheckResult], samples_files: list[Path])", StringComparison.Ordinal)
                  && validator.Contains("def check_forbidden_public_content(results: list[CheckResult], samples_files: list[Path])", StringComparison.Ordinal)
                  && validator.Contains("def check_forbidden_sample_artifacts(results: list[CheckResult], samples_entries: list[Path])", StringComparison.Ordinal)
                  && !validator.Contains("for path in SAMPLES.rglob(\"*\")", StringComparison.Ordinal)
                  && !validator.Contains("list(iter_files(SAMPLES))", StringComparison.Ordinal),
                "164-1A-2: sample hygiene helpers consume the shared file list instead of walking again");
        }

        private static void VerifyArchitectureTestsCacheRepoRoot()
        {
            var tests = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Unit/Architecture/UnityDemoSamplesAssetsTests.cs");

            Check(tests.Contains("private static readonly Lazy<string> RepoRoot", StringComparison.Ordinal)
                  && tests.Contains("private static string FindRepoRoot()", StringComparison.Ordinal)
                  && tests.Contains("RepoRoot.Value", StringComparison.Ordinal)
                  && !tests.Contains("private static string RepoRoot\n", StringComparison.Ordinal),
                "164-1B-1: Unity demo sample architecture tests cache repository-root discovery");
        }

        private static void VerifyPhase16AvoidsReleaseVersionCoupling()
        {
            var phase16 = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase16Validation.cs");
            var bumpVersion = ReadRepoText("Scripts/release/bump_version.py");

            Check(phase16.Contains("PackageVersionRegex", StringComparison.Ordinal)
                  && phase16.Contains("ExtractPackageJsonString(json, \"version\")", StringComparison.Ordinal)
                  && phase16.Contains("package.json version is valid semver", StringComparison.Ordinal)
                  && !phase16.Contains("\"\\\"version\\\": \\\"1.9.5\\\"\"", StringComparison.Ordinal),
                "164-1C-1: Phase16 validates the package version shape instead of pinning the current release number");
            Check(!bumpVersion.Contains("update_phase16_assertion", StringComparison.Ordinal)
                  && !bumpVersion.Contains("Phase16 package.json version assertion literal", StringComparison.Ordinal),
                "164-1C-2: bump_version.py no longer rewrites a Phase16 version assertion");
        }

        private static void VerifyPhase16BuildOutputScanIsTargeted()
        {
            var phase16 = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase16Validation.cs");

            Check(phase16.Contains("EnumeratePackageBuildOutputDirectories(packagesDir)", StringComparison.Ordinal)
                  && phase16.Contains("Directory.EnumerateDirectories(packagesDir, \"bin\", SearchOption.AllDirectories)", StringComparison.Ordinal)
                  && phase16.Contains("Directory.EnumerateDirectories(packagesDir, \"obj\", SearchOption.AllDirectories)", StringComparison.Ordinal)
                  && !phase16.Contains("Directory.EnumerateDirectories(packagesDir, \"*\", SearchOption.AllDirectories)", StringComparison.Ordinal),
                "164-1D-1: Phase16 scans package build-output directories with targeted bin/obj enumerations");
        }

        private static void VerifySyncFullDemoValidatesTextBeforeEncoding()
        {
            var sync = ReadRepoText("Scripts/samples/sync_full_demo.py");

            Check(sync.Contains("validate_portable_full_demo_scene_payload(text)", StringComparison.Ordinal)
                  && sync.Contains("return text.encode(\"utf-8\")", StringComparison.Ordinal)
                  && sync.Contains("def validate_portable_full_demo_scene_payload(text: str) -> None", StringComparison.Ordinal)
                  && !sync.Contains("payload.decode(\"utf-8\", errors=\"replace\")", StringComparison.Ordinal),
                "164-1E-1: sync_full_demo validates portable scene text without an encode/decode round trip");
        }

        private static void VerifyPhase17AvoidsRepeatedAbsolutePathNormalization()
        {
            var phase17 = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase17Validation.cs");
            var helperStart = phase17.IndexOf("static void ScanNoAbsolutePaths", StringComparison.Ordinal);
            var helperBody = helperStart >= 0 ? phase17.Substring(helperStart) : string.Empty;

            Check(phase17.Contains("ScanNoAbsolutePaths(Path.Combine(demoDir, \"README.md\"), windowsAbsPath, unixAbsPath", StringComparison.Ordinal)
                  && phase17.Contains("static void ScanNoAbsolutePaths(string path, string windowsAbsPath, string unixAbsPath, string label)", StringComparison.Ordinal)
                  && !helperBody.Contains("repoRoot.Replace", StringComparison.Ordinal),
                "164-1F-1: Phase17 computes absolute path forms once and passes them into scan helpers");
        }

        private static void VerifyRegistryAndCompileEntry()
        {
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");

            Check(registry.Contains("Ci(\"--phase164-1\", \"Phase 164-1\", Phase164_1Validation.Validate", StringComparison.Ordinal),
                "164-1G-1: validation registry exposes Phase164-1");
            Check(project.Contains("<Compile Include=\"Phase164_1Validation.cs\" />", StringComparison.Ordinal),
                "164-1G-2: runtime validation project compiles Phase164-1");
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
