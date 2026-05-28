// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 143 R2FU standalone distro upgrade ladder strategy validation.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase143Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 143: R2FU Standalone Distro Upgrade Ladder ===");
            _passed = 0;

            VerifyTrackedStrategyArtifacts();
            VerifyTrackedArtifactHygiene();
            VerifyCoreSdkStaysRosFree();
            VerifyValidationWiring();

            Console.WriteLine($"Phase 143: {_passed} checks passed.");
        }

        private static void VerifyTrackedStrategyArtifacts()
        {
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            Check(registry.Contains("--phase138", StringComparison.Ordinal)
                  && registry.Contains("--phase138b", StringComparison.Ordinal),
                "143A-1: tracked validation registry keeps downstream Jazzy gates explicit");
            Check(RepoFileExists("Scripts/smoke/phase138b_r2fu_jazzy_windows_build.py"),
                "143A-2: tracked Jazzy Windows build orchestrator is present");
            Check(RepoFileExists("Packages/dev.unity2foxglove.ros2forunity/package.json"),
                "143A-3: tracked optional ROS2 package manifest is present");
        }

        private static void VerifyTrackedArtifactHygiene()
        {
            var offenders = GitLsFiles()
                .Where(IsForbiddenTrackedArtifact)
                .ToList();

            Check(offenders.Count == 0,
                "143D-1: tracked files contain no R2FU zips, Unity imports, metadata XML, native plugin artifacts, or build outputs"
                + (offenders.Count == 0 ? string.Empty : " (" + string.Join(", ", offenders) + ")"));
        }

        private static void VerifyCoreSdkStaysRosFree()
        {
            var packageJson = ReadRepoText("Packages/dev.unity2foxglove.sdk/package.json");
            Check(!packageJson.Contains("rclcpp", StringComparison.OrdinalIgnoreCase)
                  && !packageJson.Contains("Ros2ForUnity", StringComparison.Ordinal),
                "143E-1: core SDK package manifest remains free of R2FU/rclcpp dependencies");

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
                "143E-2: core SDK production surface has no hard ROS2 For Unity dependency"
                + (offenders.Count == 0 ? string.Empty : " (" + string.Join(", ", offenders) + ")"));
        }

        private static void VerifyValidationWiring()
        {
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var entry = PhaseValidationRegistry.Find(new[] { "--phase143" });
            var phase142Entry = PhaseValidationRegistry.Find(new[] { "--phase142" });

            Check(entry != null
                  && entry.Run == (Action)Validate
                  && phase142Entry != null
                  && phase142Entry != entry,
                "143F-1: validation registry wires --phase143 and --phase142 as distinct entries");
            Check(entry != null
                  && entry.Category == ValidationCategory.LocalEvidence
                  && entry.IncludeInDefault,
                "143F-2: Phase143 is classified as local-evidence opt-in outside default CI");
            Check(project.Contains("Phase143Validation.cs", StringComparison.Ordinal),
                "143F-3: test project compiles Phase143Validation");
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
                throw new FileNotFoundException("Missing required Phase143 file: " + relativePath, path);
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
