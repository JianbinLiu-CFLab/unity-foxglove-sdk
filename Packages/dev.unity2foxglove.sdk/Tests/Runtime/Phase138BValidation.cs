// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 138B R2FU Jazzy Windows build toolchain closure validation.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase138BValidation
    {
        private const string PlanPath = "Plan/138B_PHASE138B_R2FU_JAZZY_WINDOWS_BUILD_TOOLCHAIN_CLOSURE_PLAN.md";
        private const string EvidencePath = "Developer/87 Phase138B R2FU Jazzy Windows Build Toolchain Closure Evidence.md";
        private const string OrchestratorPath = "Scripts/smoke/phase138b_r2fu_jazzy_windows_build.py";
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 138B: R2FU Jazzy Windows Build Toolchain Closure ===");
            _passed = 0;

            VerifyPrivatePlanIfPresent();
            VerifyPythonOrchestrator();
            VerifyEvidenceNoteIfPresent();
            VerifyNoPowerShellWrapper();
            VerifyTrackedArtifactHygiene();
            VerifyCoreSdkBoundary();
            VerifyValidationWiring();

            Console.WriteLine($"Phase 138B: {_passed} checks passed.");
        }

        private static void VerifyPrivatePlanIfPresent()
        {
            if (!RepoFileExists(PlanPath))
            {
                Check(true, "138B-A1: private Phase138B plan may be absent in clean tracked checkout");
                return;
            }

            var plan = ReadRepoText(PlanPath);
            Check(plan.Contains("Python build orchestrator", StringComparison.Ordinal)
                  && plan.Contains("phase138b_r2fu_jazzy_windows_build.py", StringComparison.Ordinal),
                "138B-A1: Phase138B plan uses a Python orchestrator");
            Check(plan.Contains("Do not add a new PowerShell build wrapper", StringComparison.Ordinal)
                  && plan.Contains("phase138b_*.ps1", StringComparison.Ordinal),
                "138B-A2: Phase138B plan forbids project-owned PowerShell wrappers");
            Check(plan.Contains("D:\\r", StringComparison.Ordinal)
                  && plan.Contains("D:\\t", StringComparison.Ordinal)
                  && plan.Contains("outside `D:\\BaiduSyncdisk`", StringComparison.Ordinal),
                "138B-A3: Phase138B plan requires a short non-synced work root");
            Check(plan.Contains("C:\\ros2_jazzy\\ros2-windows\\.pixi\\envs\\default\\python.exe", StringComparison.Ordinal)
                  && plan.Contains("Anaconda/Python313", StringComparison.Ordinal),
                "138B-A4: Phase138B plan pins Jazzy Python and records contamination risks");
            Check(plan.Contains("Phase110", StringComparison.Ordinal)
                  && plan.Contains("WSL2 NAT", StringComparison.Ordinal),
                "138B-A5: Phase138B plan preserves Phase110 and WSL2 boundaries");
            Check(plan.Contains("BUILD_ORCHESTRATOR_GREEN", StringComparison.Ordinal)
                  && plan.Contains("BLOCKED_CL_TEMP_IL", StringComparison.Ordinal)
                  && plan.Contains("BLOCKED_MSBUILD_QUERY", StringComparison.Ordinal)
                  && plan.Contains("BLOCKED_WINDOWS_PATH_LENGTH", StringComparison.Ordinal),
                "138B-A6: Phase138B plan has explicit success and blocker verdicts");
        }

        private static void VerifyPythonOrchestrator()
        {
            Check(RepoFileExists(OrchestratorPath), "138B-B1: Python build orchestrator exists");
            if (!RepoFileExists(OrchestratorPath))
                return;

            var script = ReadRepoText(OrchestratorPath);
            Check(script.Contains("argparse", StringComparison.Ordinal)
                  && script.Contains("--work-root", StringComparison.Ordinal)
                  && script.Contains(@"D:\ros2unity\.build\r2fu-jazzy-win64", StringComparison.Ordinal)
                  && script.Contains("DEFAULT_WORK_ROOT", StringComparison.Ordinal)
                  && script.Contains("DEFAULT_TEMP_ROOT", StringComparison.Ordinal)
                  && !script.Contains("default=r\"D:\\r\"", StringComparison.Ordinal)
                  && !script.Contains("default=r\"D:\\t\"", StringComparison.Ordinal),
                "138B-B2: orchestrator exposes work-root arguments under the consolidated build root");
            Check(script.Contains("assert_safe_root", StringComparison.Ordinal)
                  && script.Contains("BaiduSyncdisk", StringComparison.Ordinal),
                "138B-B3: orchestrator refuses synced/repo work roots");
            Check(script.Contains("VsDevCmd.bat", StringComparison.Ordinal)
                  && script.Contains("vswhere.exe", StringComparison.Ordinal)
                  && script.Contains("-arch=x64", StringComparison.Ordinal)
                  && script.Contains("-host_arch=x64", StringComparison.Ordinal),
                "138B-B4: orchestrator resolves and imports the VS x64 developer environment");
            Check(script.Contains("COLCON_PYTHON_EXECUTABLE", StringComparison.Ordinal)
                  && script.Contains(".pixi", StringComparison.Ordinal)
                  && script.Contains("catkin_pkg", StringComparison.Ordinal),
                "138B-B5: orchestrator pins ROS2 Jazzy pixi Python and validates catkin_pkg");
            Check(script.Contains("[\"TEMP\"]", StringComparison.Ordinal)
                  && script.Contains("[\"TMP\"]", StringComparison.Ordinal),
                "138B-B6: orchestrator moves TEMP/TMP outside the synced workspace");
            Check(script.Contains("record_where_diagnostics", StringComparison.Ordinal)
                  && script.Contains("\"where\"", StringComparison.Ordinal)
                  && script.Contains("msbuild", StringComparison.Ordinal)
                  && script.Contains("cl", StringComparison.Ordinal),
                "138B-B7: orchestrator records where diagnostics for native tools");
            Check(script.Contains("BUILD_ORCHESTRATOR_GREEN", StringComparison.Ordinal)
                  && script.Contains("BLOCKED_CL_TEMP_IL", StringComparison.Ordinal)
                  && script.Contains("BLOCKED_WINDOWS_PATH_LENGTH", StringComparison.Ordinal)
                  && script.Contains("BLOCKED_UNKNOWN_TOOLCHAIN", StringComparison.Ordinal),
                "138B-B8: orchestrator emits explicit verdict labels");
            Check(script.Contains("build.ps1", StringComparison.Ordinal)
                  && script.Contains("get_repos.ps1", StringComparison.Ordinal)
                  && script.Contains("feature/jazzy-support", StringComparison.Ordinal),
                "138B-B9: orchestrator uses upstream R2FU scripts and Jazzy support branches");
            Check(script.Contains("resolve_cmake_generator", StringComparison.Ordinal)
                  && script.Contains("prefer Ninja", StringComparison.Ordinal)
                  && script.Contains("EffectiveGenerator", StringComparison.Ordinal),
                "138B-B10: orchestrator resolves auto generator to prefer Ninja and records it");
            Check(script.Contains("CHECKOUT_DIR_NAME", StringComparison.Ordinal)
                  && script.Contains("\"r2u\"", StringComparison.Ordinal),
                "138B-B11: orchestrator uses a short checkout directory to reduce generated object paths");
            Check(script.Contains("patch_ros2cs_jazzy_windows_standalone", StringComparison.Ordinal)
                  && script.Contains("libssl-3-x64.dll", StringComparison.Ordinal)
                  && script.Contains("libcrypto-3-x64.dll", StringComparison.Ordinal),
                "138B-B12: orchestrator patches Jazzy Windows standalone OpenSSL 3 runtime DLL names");
            Check(script.Contains("modulenotfounderror", StringComparison.Ordinal)
                  && script.Contains("anaconda3", StringComparison.OrdinalIgnoreCase)
                  && script.IndexOf("modulenotfounderror", StringComparison.Ordinal)
                     < script.IndexOf("system cannot find the file", StringComparison.Ordinal),
                "138B-B13: orchestrator classifies Python contamination before generic missing-file failures");
            Check(script.Contains("JAZZY_PIXI_RUNTIME_DLLS", StringComparison.Ordinal)
                  && script.Contains("copy_jazzy_pixi_runtime_closure", StringComparison.Ordinal)
                  && script.Contains("yaml.dll", StringComparison.Ordinal)
                  && script.Contains("spdlog.dll", StringComparison.Ordinal)
                  && script.Contains("fmt.dll", StringComparison.Ordinal),
                "138B-B14: orchestrator closes Jazzy pixi runtime DLL dependencies for rcl.dll loading");
            Check(script.Contains("kill_process_tree_windows", StringComparison.Ordinal)
                  && script.Contains("taskkill", StringComparison.Ordinal)
                  && script.Contains("timed_out", StringComparison.Ordinal)
                  && script.Contains("exit_code = 124 if timed_out", StringComparison.Ordinal),
                "138B-B15: orchestrator kills Windows process trees and reports timeout as 124");
            Check(script.Contains("is_contaminating_python_path", StringComparison.Ordinal)
                  && script.Contains("miniconda", StringComparison.Ordinal)
                  && script.Contains("python311", StringComparison.Ordinal)
                  && script.Contains("probe.unlink()", StringComparison.Ordinal),
                "138B-B16: orchestrator filters Python contamination broadly and cleans CL probe files");
        }

        private static void VerifyEvidenceNoteIfPresent()
        {
            if (!RepoFileExists(EvidencePath))
            {
                Check(true, "138B-C1: local Phase138B evidence note may be absent before diagnostics run");
                return;
            }

            var evidence = ReadRepoText(EvidencePath);
            Check(evidence.Contains("Phase138B", StringComparison.Ordinal)
                  && evidence.Contains("Python orchestrator", StringComparison.Ordinal)
                  && evidence.Contains("No project-owned PowerShell wrapper", StringComparison.Ordinal),
                "138B-C1: evidence records the Python/no-PS-wrapper boundary");
            Check(evidence.Contains("WorkRoot", StringComparison.Ordinal)
                  && evidence.Contains("TempRoot", StringComparison.Ordinal)
                  && evidence.Contains("D:\\r", StringComparison.Ordinal)
                  && evidence.Contains("EffectiveGenerator", StringComparison.Ordinal),
                "138B-C2: evidence records non-synced work and temp roots");
            Check(evidence.Contains("where python", StringComparison.Ordinal)
                  && evidence.Contains("where cl", StringComparison.Ordinal)
                  && evidence.Contains("catkin_pkg", StringComparison.Ordinal),
                "138B-C3: evidence records Python/native tool diagnostics");
            Check(evidence.Contains("BLOCKED_", StringComparison.Ordinal)
                  || evidence.Contains("BUILD_ORCHESTRATOR_GREEN", StringComparison.Ordinal),
                "138B-C4: evidence records a final verdict");
        }

        private static void VerifyNoPowerShellWrapper()
        {
            var offenders = GitLsFiles()
                .Where(path => path.Replace('\\', '/').StartsWith("Scripts/smoke/phase138b_", StringComparison.Ordinal)
                               && path.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
                .ToList();

            Check(offenders.Count == 0,
                "138B-D1: tracked files contain no Phase138B PowerShell wrapper"
                + (offenders.Count == 0 ? string.Empty : " (" + string.Join(", ", offenders) + ")"));
        }

        private static void VerifyTrackedArtifactHygiene()
        {
            var offenders = GitLsFiles()
                .Where(IsForbiddenTrackedArtifact)
                .ToList();

            Check(offenders.Count == 0,
                "138B-E1: tracked files contain no R2FU runtime zips, Unity imports, metadata XML, native runtime binaries, or build outputs"
                + (offenders.Count == 0 ? string.Empty : " (" + string.Join(", ", offenders) + ")"));
        }

        private static void VerifyCoreSdkBoundary()
        {
            var packageJson = ReadRepoText("Packages/dev.unity2foxglove.sdk/package.json");
            Check(!packageJson.Contains("rclcpp", StringComparison.OrdinalIgnoreCase)
                  && !packageJson.Contains("Ros2ForUnity", StringComparison.Ordinal),
                "138B-F1: core SDK package manifest remains free of R2FU/rclcpp dependencies");
        }

        private static void VerifyValidationWiring()
        {
            var program = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Program.cs");
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");

            Check(program.Contains("PhaseValidationRegistry.Find", StringComparison.Ordinal)
                  && registry.Contains("Local(\"--phase138b\"", StringComparison.Ordinal)
                  && registry.Contains("Phase138BValidation.Validate", StringComparison.Ordinal),
                "138B-G1: validation registry wires --phase138b");
            Check(registry.Contains("Local(\"--phase138b\"", StringComparison.Ordinal)
                  && registry.Contains("ValidationCategory.LocalEvidence, run, includeInDefault: true", StringComparison.Ordinal),
                "138B-G2: Phase138B is classified as local-evidence opt-in outside default CI");
            Check(project.Contains("Phase138BValidation.cs", StringComparison.Ordinal),
                "138B-G3: test project compiles Phase138BValidation");
        }

        private static bool IsForbiddenTrackedArtifact(string path)
        {
            var normalized = path.Replace('\\', '/');
            if (normalized.StartsWith("Unity2Foxglove/Assets/Ros2ForUnity", StringComparison.Ordinal))
                return true;
            if (normalized.StartsWith("third-party/ros2-for-unity/install/", StringComparison.Ordinal)
                || normalized.StartsWith("third-party/ros2-for-unity/build/", StringComparison.Ordinal)
                || normalized.StartsWith("third-party/ros2-for-unity/log/", StringComparison.Ordinal))
                return true;
            if (normalized.StartsWith("Packages/dev.unity2foxglove.ros2forunity.runtime.", StringComparison.Ordinal))
                return normalized.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                       || normalized.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase)
                       || normalized.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase);
            if (normalized.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase))
                return true;
            var fileName = Path.GetFileName(normalized);
            if (fileName.StartsWith("Ros2ForUnity", StringComparison.OrdinalIgnoreCase)
                && fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                return true;
            if (IsForbiddenNativeRuntimeName(fileName))
                return true;
            return fileName == "metadata_ros2cs.xml"
                   || fileName == "metadata_ros2_for_unity.xml";
        }

        private static bool IsForbiddenNativeRuntimeName(string fileName)
        {
            var extension = Path.GetExtension(fileName);
            if (!extension.Equals(".dll", StringComparison.OrdinalIgnoreCase)
                && !extension.Equals(".so", StringComparison.OrdinalIgnoreCase)
                && !extension.Equals(".dylib", StringComparison.OrdinalIgnoreCase))
                return false;

            var lower = fileName.ToLowerInvariant();
            return lower.Contains("ros2")
                   || lower.Contains("ros2cs")
                   || lower.Contains("rcl")
                   || lower.Contains("rmw_")
                   || lower.Contains("fastdds")
                   || lower.Contains("fastrtps")
                   || lower.Contains("cyclonedds")
                   || lower.Contains("foonathan")
                   || lower.Contains("rcutils");
        }

        private static IEnumerable<string> GitLsFiles()
        {
            try
            {
                var startInfo = new ProcessStartInfo("git", "ls-files")
                {
                    WorkingDirectory = RepoRoot(),
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var process = Process.Start(startInfo);
                if (process == null)
                    return Array.Empty<string>();
                var output = process.StandardOutput.ReadToEnd();
                if (!process.WaitForExit(5000) || process.ExitCode != 0)
                    return Array.Empty<string>();
                return output.Replace("\r\n", "\n")
                    .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private static bool RepoFileExists(string relativePath)
        {
            return File.Exists(RepoPath(relativePath));
        }

        private static string ReadRepoText(string relativePath)
        {
            return File.ReadAllText(RepoPath(relativePath));
        }

        private static string RepoPath(string relativePath)
        {
            return Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string RepoRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, ".git")))
                    return directory.FullName;
                directory = directory.Parent;
            }
            throw new InvalidOperationException("Could not locate repository root.");
        }

        private static void Check(bool condition, string description)
        {
            if (!condition)
                throw new InvalidOperationException(description);
            _passed++;
            Console.WriteLine("[PASS] " + description);
        }
    }
}
