// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 134-30 regression coverage for R2FU smoke/build script path hygiene.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase134_30Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 134-30: R2FU Smoke Build Scripts ===");
            _passed = 0;

            VerifyPhase138BBuildDefaults();
            VerifyPhase138BDefaultRegressionTest();

            Console.WriteLine($"Phase 134-30: {_passed} checks passed.");
        }

        private static void VerifyPhase138BBuildDefaults()
        {
            var source = ReadRepoText("Scripts/smoke/phase138b_r2fu_jazzy_windows_build.py");
            Check(source.Contains(@"DEFAULT_BUILD_ROOT = pathlib.Path(r""D:\ros2unity\.build\r2fu-jazzy-win64"")", StringComparison.Ordinal)
                  && source.Contains("DEFAULT_WORK_ROOT = DEFAULT_BUILD_ROOT / \"work\"", StringComparison.Ordinal)
                  && source.Contains("DEFAULT_TEMP_ROOT = DEFAULT_BUILD_ROOT / \"tmp\"", StringComparison.Ordinal),
                "134-30A-1: Phase138B build helper defaults to consolidated R2FU build root");
            Check(source.Contains("parser.add_argument(\"--work-root\", default=str(DEFAULT_WORK_ROOT))", StringComparison.Ordinal)
                  && source.Contains("parser.add_argument(\"--temp-root\", default=str(DEFAULT_TEMP_ROOT))", StringComparison.Ordinal),
                "134-30A-2: Phase138B build helper uses consolidated defaults for CLI arguments");
            Check(!source.Contains("default=r\"D:\\r\"", StringComparison.Ordinal)
                  && !source.Contains("default=r\"D:\\t\"", StringComparison.Ordinal),
                "134-30A-3: Phase138B build helper no longer recreates D:\\r or D:\\t by default");
        }

        private static void VerifyPhase138BDefaultRegressionTest()
        {
            var source = ReadRepoText("Scripts/tests/test_phase138b_build_defaults.py");
            Check(source.Contains(@"EXPECTED_ROOT = Path(r""D:\ros2unity\.build\r2fu-jazzy-win64"")", StringComparison.Ordinal)
                  && source.Contains("module.parse_args([])", StringComparison.Ordinal)
                  && source.Contains("assertNotEqual(Path(r\"D:\\r\"), work_root)", StringComparison.Ordinal)
                  && source.Contains("assertNotEqual(Path(r\"D:\\t\"), temp_root)", StringComparison.Ordinal),
                "134-30B-1: Phase138B default path regression test asserts consolidated defaults");
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
