// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 129 generic PointCloud2 RViz2 acceptance kit validation.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase129Validation.
    /// </summary>
    public static partial class Phase129Validation
    {
        private static void VerifyReleaseValidatorAcceptsV1SampleSet()
        {
            var script = ReadRepoText("Scripts/ros2forunity/windows/jazzy/validate_ros2forunity_package.py");
            Check(script.Contains("RVIZ_POINTCLOUD2_SAMPLE", StringComparison.Ordinal)
                  && script.Contains("RViz2 PointCloud2 Acceptance", StringComparison.Ordinal)
                  && script.Contains("RVIZ_SAMPLE", StringComparison.Ordinal)
                  && script.Contains("RVIZ_MARKERARRAY_SAMPLE", StringComparison.Ordinal)
                  && script.Contains("External Adapter", StringComparison.Ordinal)
                  && script.Contains("RVIZ_V1_SAMPLE", StringComparison.Ordinal),
                "129H-1: release validator accepts External Adapter and RViz2 v1 sample set");
        }
        private static void VerifyValidationWiring()
        {
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");

            Check(registry.Contains("Ci(\"--phase129\"", StringComparison.Ordinal)
                  && registry.Contains("Phase129Validation.Validate", StringComparison.Ordinal),
                "129J-1: validation registry wires --phase129");
            Check(project.Contains("Phase129Validation.cs", StringComparison.Ordinal),
                "129J-2: test project compiles Phase129Validation");
        }

        private static IEnumerable<string> CoreProductionForbiddenTokens()
        {
            return new[]
            {
                "using ROS2;",
                "ROS2UnityComponent",
                "ROS2Node",
                "tf2_msgs.msg",
                "sensor_msgs.msg",
                "visualization_msgs.msg"
            };
        }

        private static IEnumerable<string> OptionalRuntimeForbiddenTokens()
        {
            return new[]
            {
                "using ROS2;",
                "namespace ROS2",
                "ROS2UnityComponent",
                "ROS2Node",
                "IPublisher<",
                "ISubscription<",
                "tf2_msgs",
                "sensor_msgs",
                "visualization_msgs"
            };
        }

        private static bool AllR2fuReferencesAreGuarded(string text)
        {
            return PhaseRos2ForUnityValidationHelpers.AllR2fuReferencesAreGuarded(
                text, Define, PhaseRos2ForUnityValidationHelpers.R2fuGuardTokens, out _);
        }

        private static bool ContainsAny(string text, IEnumerable<string> tokens)
        {
            return tokens.Any(token => text.Contains(token, StringComparison.OrdinalIgnoreCase));
        }

        private static IEnumerable<string> ExistingTextFilesOrSingleFile(string relativePath)
        {
            var path = RepoPath(relativePath);
            if (File.Exists(path))
                return new[] { path };
            if (!Directory.Exists(path))
                return Array.Empty<string>();

            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Where(IsTextFile)
                .ToArray();
        }

        private static bool IsTextFile(string path)
        {
            var extension = Path.GetExtension(path);
            return extension.Equals(".cs", StringComparison.OrdinalIgnoreCase)
                   || extension.Equals(".md", StringComparison.OrdinalIgnoreCase)
                   || extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
                   || extension.Equals(".asmdef", StringComparison.OrdinalIgnoreCase)
                   || extension.Equals(".txt", StringComparison.OrdinalIgnoreCase);
        }

        private static bool RepoFileExists(string relativePath)
        {
            return File.Exists(RepoPath(relativePath));
        }

        private static string ReadRepoText(string relativePath)
        {
            var path = RepoPath(relativePath);
            if (!File.Exists(path))
                throw new FileNotFoundException("Missing required Phase129 file: " + relativePath, path);
            return PhaseValidationSourceHelpers.ReadRequiredRepoText(relativePath);
        }

        private static string RepoPath(string relativePath)
        {
            return Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string RepoRoot()
        {
            if (_repoRoot != null)
                return _repoRoot;
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new InvalidOperationException("Could not find repository root.");
            _repoRoot = root;
            return _repoRoot;
        }

        private static string Rel(string path)
        {
            return path.Replace(RepoRoot() + Path.DirectorySeparatorChar, string.Empty)
                .Replace('\\', '/');
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
