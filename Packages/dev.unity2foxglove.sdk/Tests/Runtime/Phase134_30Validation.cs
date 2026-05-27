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
            VerifyLegacyR2fuHelpersUseSharedRos2Environment();
            VerifyPhase128LauncherUsesSharedRvizPath();
            VerifyRvizAcceptancePayloadGuards();
            VerifyPhase132YamlNumberParsing();
            VerifyPhase138BBuildScriptHardening();

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

        private static void VerifyLegacyR2fuHelpersUseSharedRos2Environment()
        {
            foreach (var script in new[]
                     {
                         "Scripts/smoke/phase110_string_smoke_acceptance.py",
                         "Scripts/smoke/phase127_r2fu_real_project_acceptance.py",
                     })
            {
                var source = ReadRepoText(script);
                Check(source.Contains("import _ros2_windows_env as ros2env", StringComparison.Ordinal)
                      && source.Contains("ros2env.build_ros_env(ros2_root, args.rmw, args.discovery_range, args.domain_id)", StringComparison.Ordinal)
                      && source.Contains("ros2env.validate_ros2_root(ros2_root)", StringComparison.Ordinal)
                      && source.Contains("ros2env.run_ros2(", StringComparison.Ordinal),
                    $"134-30C shared ROS2 env: {script}");
                Check(!source.Contains("def build_ros_env(", StringComparison.Ordinal)
                      && !source.Contains("def validate_ros2_root(", StringComparison.Ordinal)
                      && !source.Contains("def run_ros2(", StringComparison.Ordinal)
                      && !source.Contains("ROS_AUTOMATIC_DISCOVERY_RANGE\"] = \"SUBNET\"", StringComparison.Ordinal),
                    $"134-30D no local env duplication or forced SUBNET: {script}");
                Check(source.Contains("has_positive_subscription_count", StringComparison.Ordinal)
                      && source.Contains("Subscription count:\\s*([1-9][0-9]*)", StringComparison.Ordinal)
                      && !source.Contains("Subscription count: 1", StringComparison.Ordinal),
                    $"134-30E accepts positive subscription counts: {script}");
            }
        }

        private static void VerifyPhase128LauncherUsesSharedRvizPath()
        {
            var source = ReadRepoText("Scripts/smoke/launch_phase128_rviz2.py");
            Check(source.Contains("import _ros2_windows_env as ros2env", StringComparison.Ordinal)
                  && source.Contains("ros2env.build_ros_env(ros2_root, args.rmw, args.discovery_range, args.domain_id)", StringComparison.Ordinal)
                  && source.Contains("ros2env.launch_rviz(", StringComparison.Ordinal),
                "134-30F: Phase128 launcher uses shared ROS2/RViz2 helpers");
            Check(!source.Contains("def build_rviz_env(", StringComparison.Ordinal)
                  && !source.Contains("subprocess.Popen", StringComparison.Ordinal),
                "134-30G: Phase128 launcher no longer duplicates direct RViz2 process setup");
        }

        private static void VerifyRvizAcceptancePayloadGuards()
        {
            var phase130 = ReadRepoText("Scripts/smoke/phase130_markerarray_acceptance.py");
            Check(phase130.Contains("echo_markerarray_until_add", StringComparison.Ordinal)
                  && phase130.Contains("\"action: 0\"", StringComparison.Ordinal)
                  && !phase130.Contains("if \"action: 3\" in output:", StringComparison.Ordinal),
                "134-30H: Phase130 MarkerArray helper rejects DELETEALL-only echoes");

            var phase131 = ReadRepoText("Scripts/smoke/phase131_standard_visualization_acceptance.py");
            Check(phase131.Contains("POSITIVE_YAML_FLOAT_RE", StringComparison.Ordinal)
                  && phase131.Contains("0\\.[0-9]*[1-9]", StringComparison.Ordinal),
                "134-30I: Phase131 LaserScan helper accepts positive fractional ranges");
            Check(phase131.Contains("[\"action: 0\", \"type: 1\", \"ns: unity2foxglove\"]", StringComparison.Ordinal)
                  && !phase131.Contains("if \"action: 3\" in output:", StringComparison.Ordinal),
                "134-30J: Phase131 MarkerArray helper requires an ADD/MODIFY marker");
        }

        private static void VerifyPhase132YamlNumberParsing()
        {
            var source = ReadRepoText("Scripts/smoke/phase132_standard_messages_acceptance.py");
            Check(source.Contains("YAML_NUMBER_RE", StringComparison.Ordinal)
                  && source.Contains("(?:[eE][+-]?[0-9]+)?", StringComparison.Ordinal),
                "134-30K: Phase132 YAML array parser supports scientific notation");
        }

        private static void VerifyPhase138BBuildScriptHardening()
        {
            var source = ReadRepoText("Scripts/smoke/phase138b_r2fu_jazzy_windows_build.py");
            Check(source.Contains("import re", StringComparison.Ordinal)
                  && source.Contains("reject_cmd_shell_unsafe_path(\"VsDevCmd.bat\", vs_dev_cmd)", StringComparison.Ordinal)
                  && source.Contains("if '\"' in str(path):", StringComparison.Ordinal),
                "134-30L: Phase138B rejects quoted VsDevCmd paths before cmd.exe interpolation");
            Check(source.Contains("re.search(r\"python3[0-9]{2,}\", lower)", StringComparison.Ordinal)
                  && !source.Contains("cleaned[\"Path\"] = merged_path", StringComparison.Ordinal),
                "134-30M: Phase138B future-proofs Python contamination filtering and keeps one canonical PATH key");
            Check(source.Contains("pattern = re.compile(", StringComparison.Ordinal)
                  && source.Contains("third_party_standalone_libs", StringComparison.Ordinal)
                  && !source.Contains("old = \"\"\"", StringComparison.Ordinal),
                "134-30N: Phase138B patches ros2cs standalone DLL block with a resilient matcher");
            Check(source.Contains("parser.add_argument(\n        \"--evidence-path\"", StringComparison.Ordinal)
                  && source.Contains("args.evidence_path", StringComparison.Ordinal)
                  && source.Contains("DEFAULT_EVIDENCE_PATH", StringComparison.Ordinal)
                  && !source.Contains("repo_root / \"Developer\"", StringComparison.Ordinal),
                "134-30O: Phase138B evidence path can be overridden for CI or local evidence runs");
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
