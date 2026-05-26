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
