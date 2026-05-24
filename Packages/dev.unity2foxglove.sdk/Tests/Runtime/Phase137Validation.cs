// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 137 R2FU standalone distro upgrade ladder strategy validation.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase137Validation
    {
        private const string PlanPath = "Plan/137_PHASE137_R2FU_STANDALONE_DISTRO_UPGRADE_LADDER_PLAN.md";
        private const string Phase206PlanPath = "Plan/206_R2FU_JAZZY_STANDALONE_REBUILD_SPIKE_PLAN.md";
        private const string EvidencePath = "Developer/85 Phase137 R2FU Standalone Distro Upgrade Ladder.md";
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 137: R2FU Standalone Distro Upgrade Ladder ===");
            _passed = 0;

            VerifyPrivatePlanIfPresent();
            VerifyPhase206SupersededIfPresent();
            VerifyEvidenceNoteIfPresent();
            VerifyTrackedArtifactHygiene();
            VerifyCoreSdkStaysRosFree();
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
            Check(plan.Contains("Phase 137 is the umbrella strategy gate", StringComparison.Ordinal)
                  && plan.Contains("delegates Jazzy runtime execution to Phase 138", StringComparison.Ordinal)
                  && plan.Contains("delegates Lyrical / Ubuntu 26.04 feasibility to Phase 139", StringComparison.Ordinal)
                  && plan.Contains("feeds the compatibility matrix in Phase 140", StringComparison.Ordinal),
                "137A-1: Phase137 is scoped as the umbrella strategy gate");
            Check(plan.Contains("must not re-prove Phase 106", StringComparison.Ordinal)
                  && plan.Contains("Phase106-only Unity publishing did not crash", StringComparison.Ordinal)
                  && plan.Contains("ROS graph visibility", StringComparison.Ordinal)
                  && plan.Contains("Phase 110 sample crashes must not be used", StringComparison.Ordinal),
                "137A-2: Phase137 preserves the Phase106/110 caveat instead of over-claiming Humble");
            Check(plan.Contains("empirical interop evidence", StringComparison.Ordinal)
                  && plan.Contains("not an official ROS2 cross-distribution support guarantee", StringComparison.Ordinal)
                  && plan.Contains("cross-distribution communication", StringComparison.Ordinal),
                "137A-3: Phase137 records the Humble-to-Jazzy cross-distro boundary");
            Check(plan.Contains("Jazzy maps to Ubuntu 24.04", StringComparison.Ordinal)
                  && plan.Contains("Lyrical maps to Ubuntu 26.04", StringComparison.Ordinal)
                  && plan.Contains("Lyrical supported platforms", StringComparison.Ordinal)
                  && plan.Contains("pre-release binaries", StringComparison.Ordinal),
                "137A-4: Phase137 records Jazzy/Lyrical platform and release-state boundaries");
            Check(plan.Contains("Phase 138 is the sole execution owner", StringComparison.Ordinal)
                  && plan.Contains("Phase 139 is the sole execution owner", StringComparison.Ordinal)
                  && plan.Contains("Phase 140 is the sole owner", StringComparison.Ordinal),
                "137A-5: Phase137 delegates execution to 138/139 and matrix work to 140");
            Check(plan.Contains("LYRICAL_PRERELEASE_ONLY", StringComparison.Ordinal)
                  && plan.Contains("BLOCKED_LYRICAL_NOT_RELEASED_OR_PACKAGED", StringComparison.Ordinal)
                  && plan.Contains("STRATEGY_GREEN_PENDING_RUNG_EVIDENCE", StringComparison.Ordinal),
                "137A-6: Phase137 verdict vocabulary covers pre-release and umbrella states");
        }

        private static void VerifyPhase206SupersededIfPresent()
        {
            if (!RepoFileExists(Phase206PlanPath))
            {
                Check(true, "137-P206-1: private Phase206 plan may be absent in clean tracked checkout");
                return;
            }

            var plan = ReadRepoText(Phase206PlanPath);
            Check(plan.Contains("status: superseded", StringComparison.Ordinal)
                  && plan.Contains("Superseded by [[137_PHASE137_R2FU_STANDALONE_DISTRO_UPGRADE_LADDER_PLAN]]", StringComparison.Ordinal),
                "137-P206-1: Phase206 Jazzy rebuild note is superseded by Phase137");
        }

        private static void VerifyEvidenceNoteIfPresent()
        {
            if (!RepoFileExists(EvidencePath))
            {
                Check(true, "137C-1: local Phase137 evidence note may be absent before manual evidence is recorded");
                return;
            }

            var evidence = ReadRepoText(EvidencePath);
            Check(evidence.Contains("STRATEGY_GREEN_PENDING_RUNG_EVIDENCE", StringComparison.Ordinal)
                  && evidence.Contains("Phase 137", StringComparison.Ordinal)
                  && evidence.Contains("Phase 138", StringComparison.Ordinal)
                  && evidence.Contains("Phase 139", StringComparison.Ordinal),
                "137C-1: local Phase137 evidence note records the umbrella verdict and downstream owners");
            Check(evidence.Contains("Phase106", StringComparison.Ordinal)
                  && evidence.Contains("graph visibility", StringComparison.Ordinal)
                  && evidence.Contains("cross-distro", StringComparison.Ordinal),
                "137C-2: local Phase137 evidence note records current Humble caveats");
        }

        private static void VerifyTrackedArtifactHygiene()
        {
            var offenders = GitLsFiles()
                .Where(IsForbiddenTrackedArtifact)
                .ToList();

            Check(offenders.Count == 0,
                "137D-1: tracked files contain no R2FU zips, Unity imports, metadata XML, native plugin artifacts, or build outputs"
                + (offenders.Count == 0 ? string.Empty : " (" + string.Join(", ", offenders) + ")"));
        }

        private static void VerifyCoreSdkStaysRosFree()
        {
            var packageJson = ReadRepoText("Packages/dev.unity2foxglove.sdk/package.json");
            Check(!packageJson.Contains("rclcpp", StringComparison.OrdinalIgnoreCase)
                  && !packageJson.Contains("Ros2ForUnity", StringComparison.Ordinal),
                "137E-1: core SDK package manifest remains free of R2FU/rclcpp dependencies");

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
                "137E-2: core SDK production surface has no hard ROS2 For Unity dependency"
                + (offenders.Count == 0 ? string.Empty : " (" + string.Join(", ", offenders) + ")"));
        }

        private static void VerifyValidationWiring()
        {
            var program = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Program.cs");
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");

            Check(program.Contains("PhaseValidationRegistry.Find", StringComparison.Ordinal)
                  && registry.Contains("Local(\"--phase137\"", StringComparison.Ordinal)
                  && registry.Contains("Phase137Validation.Validate", StringComparison.Ordinal),
                "137F-1: validation registry wires --phase137");
            Check(registry.Contains("Local(\"--phase137\"", StringComparison.Ordinal)
                  && registry.Contains("ValidationCategory.LocalEvidence, run, includeInDefault: true", StringComparison.Ordinal),
                "137F-2: Phase137 is classified as local-evidence opt-in outside default CI");
            Check(project.Contains("Phase137Validation.cs", StringComparison.Ordinal),
                "137F-3: test project compiles Phase137Validation");
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
