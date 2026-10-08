// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 132 ROS2 standard message expansion validation.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase132Validation.
    /// </summary>
    public static partial class Phase132Validation
    {
        private const string R2fuDefine = "UNITY2FOXGLOVE_ROS2_FOR_UNITY";

        private static bool AllR2fuReferencesAreGuarded(string text)
        {
            return PhaseRos2ForUnityValidationHelpers.AllR2fuReferencesAreGuarded(
                text, R2fuDefine, PhaseRos2ForUnityValidationHelpers.R2fuGuardTokens, out _);
        }

        private static IEnumerable<string> CoreProductionForbiddenTokens()
        {
            return new[]
            {
                "using ROS2;",
                "ROS2UnityComponent",
                "ROS2Node",
                "IPublisher<",
                "sensor_msgs.msg",
                "nav_msgs.msg",
                "geometry_msgs.msg"
            };
        }

        private static IEnumerable<string> OptionalRuntimeForbiddenTokens()
        {
            return new[]
            {
                "using ROS2;",
                "namespace ROS2",
                "ISubscription<",
                "ROS2UnityComponent",
                "ROS2Node",
                "IPublisher<",
                "tf2_msgs",
                "sensor_msgs.msg",
                "sensor_msgs",
                "nav_msgs.msg",
                "nav_msgs",
                "geometry_msgs.msg",
                "geometry_msgs",
                "std_msgs",
                "visualization_msgs",
                "builtin_interfaces",
                "ros2cs"
            };
        }

        private static IEnumerable<string> ExistingTextFilesOrSingleFile(string relativePath)
        {
            var path = RepoPath(relativePath);
            if (File.Exists(path))
                return new[] { path };
            if (!Directory.Exists(path))
                return Array.Empty<string>();
            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Where(file => file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                               || file.EndsWith(".asmdef", StringComparison.OrdinalIgnoreCase)
                               || file.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                               || file.EndsWith(".md", StringComparison.OrdinalIgnoreCase));
        }

        private static bool AllTokens(string text, params string[] tokens)
        {
            return tokens.All(token => text.Contains(token, StringComparison.Ordinal));
        }

        private static bool AllTokens(string text, IEnumerable<string> tokens)
        {
            return tokens.All(token => text.Contains(token, StringComparison.Ordinal));
        }

        private static bool ContainsAny(string text, IEnumerable<string> tokens)
        {
            return tokens.Any(token => text.Contains(token, StringComparison.OrdinalIgnoreCase));
        }

        private static bool RepoFileExists(string relativePath)
        {
            return File.Exists(RepoPath(relativePath));
        }

        private static string ReadRepoText(string relativePath)
        {
            var path = RepoPath(relativePath);
            if (!File.Exists(path))
                throw new FileNotFoundException("Missing required Phase132 file: " + relativePath, path);
            return File.ReadAllText(path);
        }

        private static string RepoPath(string relativePath)
        {
            return Path.Combine(FindRepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string Rel(string absolutePath)
        {
            var root = FindRepoRoot().TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return absolutePath.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                ? absolutePath.Substring(root.Length).Replace(Path.DirectorySeparatorChar, '/')
                : absolutePath;
        }

        private static string FindRepoRoot()
        {
            if (_repoRoot != null)
                return _repoRoot;
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new InvalidOperationException("Could not find repository root.");
            _repoRoot = root;
            return _repoRoot;
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] Phase 132: " + label);
            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
