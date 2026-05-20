// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 137 R2FU Jazzy standalone rebuild evidence validation.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase137Validation
    {
        private const string PlanPath = "Plan/137_PHASE137_R2FU_JAZZY_STANDALONE_REBUILD_PLAN.md";
        private const string EvidencePath = "Developer/86 Phase137 R2FU Jazzy Standalone Rebuild Evidence.md";
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 137: R2FU Jazzy Standalone Rebuild ===");
            _passed = 0;

            VerifyPrivatePlanIfPresent();
            VerifyEvidenceNoteIfPresent();
            VerifyTrackedArtifactHygiene();
            VerifyCoreSdkBoundary();
            VerifyValidationWiring();

            Console.WriteLine($"Phase 137: {_passed} checks passed.");
        }

        private static void VerifyPrivatePlanIfPresent()
        {
            if (!RepoFileExists(PlanPath))
            {
                Check(true, "137A-1: private Phase137 plan may be absent in clean tracked checkout");
                return;
            }

            var plan = ReadRepoText(PlanPath);
            Check(plan.Contains("Phase 137 is Jazzy-only", StringComparison.Ordinal)
                  && plan.Contains("Lyrical / Ubuntu 26.04 remains a later rung", StringComparison.Ordinal),
                "137A-1: Phase137 is scoped to Jazzy only");
            Check(plan.Contains("Phase 106's Humble standalone result as the frozen baseline", StringComparison.Ordinal)
                  && plan.Contains("does not re-prove the Humble asset", StringComparison.Ordinal),
                "137A-2: Phase137 consumes Phase106 as baseline instead of re-proving Humble");
            Check(plan.Contains("WSL2 NAT is diagnostic-only", StringComparison.Ordinal)
                  && (plan.Contains("Do not treat WSL2 NAT failure as a product blocker", StringComparison.Ordinal)
                      || plan.Contains("WSL2 NAT as the only remote Linux evidence", StringComparison.Ordinal)),
                "137A-3: Phase137 keeps WSL2 NAT diagnostic-only");
            Check(plan.Contains("Do not use the Phase110 sample as the Phase 137 live gate", StringComparison.Ordinal)
                  && plan.Contains("Phase 137 must use the exact Phase106 acceptance component", StringComparison.Ordinal),
                "137A-4: Phase137 live gate uses Phase106, not Phase110");
            Check(plan.Contains("Visual Studio developer shell", StringComparison.Ordinal)
                  && plan.Contains("C:\\ros2_jazzy\\ros2-windows", StringComparison.Ordinal)
                  && plan.Contains("package Python invocation", StringComparison.Ordinal),
                "137A-5: Phase137 records the Windows Jazzy build/CLI environment");
            Check(plan.Contains("Ros2ForUnity*.zip", StringComparison.Ordinal)
                  && plan.Contains("Unity2Foxglove/Assets/Ros2ForUnity/", StringComparison.Ordinal)
                  && plan.Contains("third-party/ros2-for-unity/install/", StringComparison.Ordinal)
                  && plan.Contains("metadata_ros2cs.xml", StringComparison.Ordinal),
                "137A-6: Phase137 lists forbidden runtime artifacts");
            Check(plan.Contains("BLOCKED_JAZZY_ENVIRONMENT", StringComparison.Ordinal)
                  && plan.Contains("BLOCKED_NATIVE_DEPENDENCY", StringComparison.Ordinal)
                  && plan.Contains("PROMOTE_JAZZY_RUNTIME_CANDIDATE", StringComparison.Ordinal),
                "137A-7: Phase137 verdict vocabulary covers Jazzy build blockers and promotion");
        }

        private static void VerifyEvidenceNoteIfPresent()
        {
            if (!RepoFileExists(EvidencePath))
            {
                Check(true, "137B-1: local Phase137 evidence note may be absent before build evidence is recorded");
                return;
            }

            var evidence = ReadRepoText(EvidencePath);
            Check(evidence.Contains("BLOCKED_JAZZY_ENVIRONMENT", StringComparison.Ordinal)
                  && evidence.Contains("feature/jazzy-support", StringComparison.Ordinal)
                  && evidence.Contains("ros2_jazzy.repos", StringComparison.Ordinal),
                "137B-1: evidence records the Jazzy-support branch and final blocker verdict");
            Check(evidence.Contains("phase137_build_jazzy_vctargets.log", StringComparison.Ordinal)
                  && evidence.Contains("phase137_colcon_jazzy_ninja_pixi_temp.log", StringComparison.Ordinal)
                  && evidence.Contains("VisualStudioVersion", StringComparison.Ordinal)
                  && evidence.Contains("VCTargetsPath", StringComparison.Ordinal),
                "137B-2: evidence records build attempts and native toolchain details");
            Check(evidence.Contains("Phase106", StringComparison.Ordinal)
                  && evidence.Contains("Phase110", StringComparison.Ordinal)
                  && evidence.Contains("WSL2 NAT", StringComparison.Ordinal),
                "137B-3: evidence preserves Phase106/Phase110/WSL acceptance boundaries");
            Check(evidence.Contains("No Unity load was attempted", StringComparison.Ordinal)
                  && evidence.Contains("No Windows ROS2 pub/sub smoke was attempted", StringComparison.Ordinal),
                "137B-4: evidence does not over-claim past the failed build gate");
        }

        private static void VerifyTrackedArtifactHygiene()
        {
            var offenders = GitLsFiles()
                .Where(IsForbiddenTrackedArtifact)
                .ToList();

            Check(offenders.Count == 0,
                "137C-1: tracked files contain no R2FU zips, Unity imports, metadata XML, native plugin artifacts, or build outputs"
                + (offenders.Count == 0 ? string.Empty : " (" + string.Join(", ", offenders) + ")"));
        }

        private static void VerifyCoreSdkBoundary()
        {
            var packageJson = ReadRepoText("Packages/dev.unity2foxglove.sdk/package.json");
            Check(!packageJson.Contains("rclcpp", StringComparison.OrdinalIgnoreCase)
                  && !packageJson.Contains("Ros2ForUnity", StringComparison.Ordinal),
                "137D-1: core SDK package manifest remains free of R2FU/rclcpp dependencies");

            var productionRoots = new[]
            {
                "Packages/dev.unity2foxglove.sdk/Runtime",
                "Packages/dev.unity2foxglove.sdk/Editor",
                "Packages/dev.unity2foxglove.sdk/Samples~"
            };

            var offenders = productionRoots
                .SelectMany(TextFiles)
                .SelectMany(path => CoreForbiddenTokens()
                    .Where(token => File.ReadAllText(path).Contains(token, StringComparison.Ordinal))
                    .Select(token => Rel(path) + " -> " + token))
                .ToList();

            Check(offenders.Count == 0,
                "137D-2: core SDK production surface has no hard ROS2 For Unity dependency"
                + (offenders.Count == 0 ? string.Empty : " (" + string.Join(", ", offenders) + ")"));
        }

        private static void VerifyValidationWiring()
        {
            var program = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Program.cs");
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");

            Check(program.Contains("--phase137", StringComparison.Ordinal)
                  && program.Contains("RunPhase137Only", StringComparison.Ordinal)
                  && program.Contains("Phase137Validation.Validate()", StringComparison.Ordinal),
                "137E-1: Program.cs wires --phase137");
            Check(program.IndexOf("Phase136Validation.Validate()", StringComparison.Ordinal)
                  < program.IndexOf("Phase137Validation.Validate()", StringComparison.Ordinal),
                "137E-2: full runtime validation calls Phase137 after Phase136");
            Check(project.Contains("Phase137Validation.cs", StringComparison.Ordinal),
                "137E-3: test project compiles Phase137Validation");
        }

        private static bool IsForbiddenTrackedArtifact(string path)
        {
            var normalized = path.Replace('\\', '/');
            var inRuntimePackage = normalized.StartsWith(
                "Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64/",
                StringComparison.Ordinal);
            if (normalized.StartsWith("Unity2Foxglove/Assets/Ros2ForUnity", StringComparison.Ordinal))
                return true;
            if (normalized.StartsWith("third-party/ros2-for-unity/install/", StringComparison.Ordinal)
                || normalized.StartsWith("third-party/ros2-for-unity/build/", StringComparison.Ordinal)
                || normalized.StartsWith("third-party/ros2-for-unity/log/", StringComparison.Ordinal))
                return true;
            if (normalized.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase))
                return true;
            var fileName = Path.GetFileName(normalized);
            if (fileName.StartsWith("Ros2ForUnity", StringComparison.OrdinalIgnoreCase)
                && fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                return true;
            if (inRuntimePackage)
                return false;
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

        private static IEnumerable<string> CoreForbiddenTokens()
        {
            return new[]
            {
                "using ROS2;",
                "ROS2UnityComponent",
                "Ros2ForUnity",
                "RobotecAI ROS2 For Unity",
                "metadata_ros2cs.xml",
                "metadata_ros2_for_unity.xml"
            };
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

        private static IEnumerable<string> TextFiles(string relativePath)
        {
            var path = Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(path))
                return Array.Empty<string>();
            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Where(HasTextExtension);
        }

        private static bool HasTextExtension(string path)
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();
            return extension == ".cs"
                   || extension == ".json"
                   || extension == ".md"
                   || extension == ".asmdef"
                   || extension == ".txt"
                   || extension == ".xml"
                   || extension == ".uxml"
                   || extension == ".uss";
        }

        private static bool RepoFileExists(string relativePath)
        {
            return File.Exists(RepoPath(relativePath));
        }

        private static string ReadRepoText(string relativePath)
        {
            var path = RepoPath(relativePath);
            if (!File.Exists(path))
                throw new FileNotFoundException("Missing required Phase137 file: " + relativePath, path);
            return File.ReadAllText(path);
        }

        private static string RepoPath(string relativePath)
        {
            return Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string RepoRoot()
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new InvalidOperationException("Could not find repository root.");
            return root;
        }

        private static string Rel(string fullPath)
        {
            var root = RepoRoot();
            var relative = Path.GetRelativePath(root, fullPath);
            return relative.Replace('\\', '/');
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
            _passed++;
            Console.WriteLine("[PASS] " + message);
        }
    }
}
