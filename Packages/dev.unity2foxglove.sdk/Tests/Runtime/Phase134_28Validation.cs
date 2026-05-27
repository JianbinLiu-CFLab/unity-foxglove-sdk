// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 134-28 regression coverage for runtime package builder extraction safety.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase134_28Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 134-28: Release Validators and Package Builders ===");
            _passed = 0;

            VerifyRuntimePackageBuilderZipSafety();
            VerifyRuntimePackageBuilderRegressionTest();
            VerifyReleaseValidatorRobustness();
            VerifyReleaseBuilderCompatibility();
            VerifyCiCoversR2fuValidators();

            Console.WriteLine($"Phase 134-28: {_passed} checks passed.");
        }

        private static void VerifyRuntimePackageBuilderZipSafety()
        {
            var source = ReadRepoText("Scripts/release/build_r2fu_runtime_package.py");
            Check(source.Contains("PurePosixPath", StringComparison.Ordinal)
                  && source.Contains("safe_runtime_zip_relative_path", StringComparison.Ordinal),
                "134-28A-1: runtime package builder normalizes zip entry paths as POSIX paths");
            Check(source.Contains("zip_path.is_absolute()", StringComparison.Ordinal)
                  && source.Contains("\"..\"", StringComparison.Ordinal)
                  && source.Contains("Rejected unsafe runtime zip entry", StringComparison.Ordinal),
                "134-28A-2: runtime package builder rejects absolute and parent-traversal zip entries");
            Check(source.Contains("runtime_root_resolved", StringComparison.Ordinal)
                  && source.Contains("target.relative_to(runtime_root_resolved)", StringComparison.Ordinal)
                  && source.Contains("outside package root", StringComparison.Ordinal),
                "134-28A-3: runtime package builder verifies resolved extraction targets stay under runtime root");
        }

        private static void VerifyRuntimePackageBuilderRegressionTest()
        {
            var source = ReadRepoText("Scripts/tests/test_build_r2fu_runtime_package.py");
            Check(source.Contains("Ros2ForUnity/../escape.txt", StringComparison.Ordinal)
                  && source.Contains("assertRaises(ValueError)", StringComparison.Ordinal)
                  && source.Contains("assertFalse((root / \"escape.txt\").exists())", StringComparison.Ordinal),
                "134-28B-1: runtime package builder test covers zip-slip rejection without escape writes");
            Check(source.Contains("Ros2ForUnity/Scripts/ROS2ForUnity.cs", StringComparison.Ordinal)
                  && source.Contains("extract_runtime(paths)", StringComparison.Ordinal)
                  && source.Contains("Runtime\" / \"Ros2ForUnity\"", StringComparison.Ordinal),
                "134-28B-2: runtime package builder test keeps valid entries under Runtime/Ros2ForUnity");
        }

        private static void VerifyReleaseValidatorRobustness()
        {
            var runtimeValidator = ReadRepoText("Scripts/release/validate_r2fu_runtime_package.py");
            Check(runtimeValidator.Contains("def read_optional_text(path: Path) -> str:", StringComparison.Ordinal)
                  && runtimeValidator.Contains("node = read_optional_text(scripts / \"ROS2Node.cs\")", StringComparison.Ordinal)
                  && runtimeValidator.Contains("sensor = read_optional_text(scripts / \"Sensor.cs\")", StringComparison.Ordinal),
                "134-28C-1: runtime validator uses optional text reads for patched source checks");
            Check(runtimeValidator.Contains("readings_guard_index = sensor.find(\"if (readings != null)\")", StringComparison.Ordinal)
                  && runtimeValidator.Contains("readings_deref_index = sensor.find(\"readings.SetHeaderFrame\")", StringComparison.Ordinal)
                  && !runtimeValidator.Contains("sensor.index(\"readings.SetHeaderFrame\")", StringComparison.Ordinal),
                "134-28C-2: runtime validator avoids unsafe Sensor.index null-guard check");

            var optionalValidator = ReadRepoText("Scripts/release/validate_ros2forunity_package.py");
            Check(optionalValidator.Contains("manifest_text = MANIFEST.read_text", StringComparison.Ordinal)
                  && optionalValidator.Contains("if MANIFEST.exists() else \"\"", StringComparison.Ordinal)
                  && !optionalValidator.Contains("(PACKAGE / \"Compliance\" / \"ros2-for-unity-adoption-manifest.json\").read_text", StringComparison.Ordinal),
                "134-28C-3: optional package validator guards missing adoption manifest reads");
        }

        private static void VerifyReleaseBuilderCompatibility()
        {
            var builder = ReadRepoText("Scripts/release/build_r2fu_runtime_package.py");
            Check(builder.Contains("hashlib.md5(seed.encode(\"utf-8\"), usedforsecurity=False)", StringComparison.Ordinal),
                "134-28D-1: deterministic Unity GUID MD5 marks non-security use");
            Check(builder.Contains("def rmtree_with_writable_retry(path: Path) -> None:", StringComparison.Ordinal)
                  && builder.Contains("\"onexc\" in inspect.signature(shutil.rmtree).parameters", StringComparison.Ordinal)
                  && builder.Contains("shutil.rmtree(raw_path, onexc=make_writable_onexc)", StringComparison.Ordinal)
                  && builder.Contains("shutil.rmtree(raw_path, onerror=make_writable_onerror)", StringComparison.Ordinal),
                "134-28D-2: runtime package builder uses onexc-aware rmtree compatibility helper");
            Check(builder.Contains("write_text(source, text.replace(UPSTREAM_PATH_BLOCK, PACKAGE_PATH_BLOCK))", StringComparison.Ordinal)
                  && !builder.Contains("source.write_text(text.replace(UPSTREAM_PATH_BLOCK, PACKAGE_PATH_BLOCK)", StringComparison.Ordinal),
                "134-28D-3: ROS2ForUnity patch write path uses stable LF helper");
            Check(builder.Contains("\"includePlatforms\": [\"Editor\", \"WindowsStandalone64\"]", StringComparison.Ordinal),
                "134-28D-4: runtime package builder emits the checked-in Windows runtime asmdef platform set");
        }

        private static void VerifyCiCoversR2fuValidators()
        {
            var workflow = ReadRepoText(".github/workflows/package-check.yml");
            Check(workflow.Contains("python3 Scripts/release/validate_r2fu_runtime_package.py", StringComparison.Ordinal)
                  && workflow.Contains("python3 Scripts/release/validate_ros2forunity_package.py", StringComparison.Ordinal),
                "134-28E-1: package CI runs R2FU runtime and adapter package validators");
        }

        private static string ReadRepoText(string relativePath)
        {
            var fullPath = Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
                throw new FileNotFoundException("Missing repository file: " + relativePath, fullPath);

            return File.ReadAllText(fullPath);
        }

        private static string RepoRoot
            => Phase16Validation.FindRepoRoot()
               ?? throw new DirectoryNotFoundException("Could not locate repository root from " + AppContext.BaseDirectory);

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new Exception("[FAIL] " + label);

            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
