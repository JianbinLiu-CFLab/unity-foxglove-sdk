// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Shared ROS2 For Unity validation helpers for artifact and guard checks.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class PhaseRos2ForUnityValidationHelpers
    {
        public const string CurrentJazzyArtifactName = "Ros2ForUnity_jazzy_standalone_windows_x86_64.zip";
        public const string LegacyJazzyArtifactName = "Ros2ForUnity_Jazzy_standalone_windows10.zip";
        public const string LegacyHumbleArtifactName = "Ros2ForUnity_humble_standalone_windows11.zip";

        /// <summary>
        /// Union set of forbidden R2FU reference tokens. All Phase128-132 boundary validators
        /// should use this shared list to keep guard coverage consistent.
        /// </summary>
        public static readonly string[] R2fuGuardTokens = new[]
        {
            "using ROS2;",
            "namespace ROS2",
            "ROS2UnityComponent",
            "ROS2Node",
            "IPublisher<",
            "ISubscription<",
            "tf2_msgs",
            "sensor_msgs",
            "std_msgs",
            "geometry_msgs",
            "nav_msgs",
            "visualization_msgs",
            "builtin_interfaces"
        };

        public static bool IsForbiddenR2fuArtifact(string path, string allowedRuntimePackagePrefix = null)
        {
            if (!string.IsNullOrEmpty(allowedRuntimePackagePrefix)
                && path.StartsWith(allowedRuntimePackagePrefix.TrimEnd('/') + "/", StringComparison.Ordinal))
            {
                return false;
            }

            return path.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase)
                   || path.EndsWith(CurrentJazzyArtifactName, StringComparison.OrdinalIgnoreCase)
                   || path.EndsWith(LegacyJazzyArtifactName, StringComparison.OrdinalIgnoreCase)
                   || path.EndsWith(LegacyHumbleArtifactName, StringComparison.OrdinalIgnoreCase)
                   || path.EndsWith("metadata_ros2cs.xml", StringComparison.OrdinalIgnoreCase)
                   || path.EndsWith("metadata_ros2_for_unity.xml", StringComparison.OrdinalIgnoreCase);
        }

        public static bool AllR2fuReferencesAreGuarded(
            string text,
            string define,
            IEnumerable<string> tokens,
            out string error)
        {
            error = string.Empty;
            var tokenList = tokens.ToArray();
            var stack = new Stack<GuardFrame>();
            var lines = text.Replace("\r\n", "\n").Split('\n');

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var trimmed = line.TrimStart();

                if (trimmed.StartsWith("#if ", StringComparison.Ordinal))
                {
                    var condition = ClassifyCondition(trimmed.Substring(4), define);
                    var parentGuarded = stack.Any(frame => frame.CurrentGuarded);
                    stack.Push(new GuardFrame(
                        parentGuarded,
                        CurrentBranchGuarded(parentGuarded, condition, priorRequiresNotDefine: false),
                        condition == GuardCondition.RequiresNotDefine));
                    continue;
                }

                if (trimmed.StartsWith("#elif ", StringComparison.Ordinal))
                {
                    if (stack.Count == 0)
                        continue;

                    var previous = stack.Pop();
                    var condition = ClassifyCondition(trimmed.Substring(6), define);
                    var priorRequiresNotDefine = previous.PriorRequiresNotDefine
                                                 || condition == GuardCondition.RequiresNotDefine;
                    stack.Push(new GuardFrame(
                        previous.ParentGuarded,
                        CurrentBranchGuarded(previous.ParentGuarded, condition, previous.PriorRequiresNotDefine),
                        priorRequiresNotDefine));
                    continue;
                }

                if (trimmed.StartsWith("#else", StringComparison.Ordinal))
                {
                    if (stack.Count == 0)
                        continue;

                    var previous = stack.Pop();
                    stack.Push(new GuardFrame(
                        previous.ParentGuarded,
                        previous.ParentGuarded || previous.PriorRequiresNotDefine,
                        previous.PriorRequiresNotDefine));
                    continue;
                }

                if (trimmed.StartsWith("#endif", StringComparison.Ordinal))
                {
                    if (stack.Count > 0)
                        stack.Pop();
                    continue;
                }

                if (trimmed.StartsWith("//", StringComparison.Ordinal))
                    continue;

                var token = tokenList.FirstOrDefault(candidate => line.Contains(candidate, StringComparison.Ordinal));
                if (token != null && !stack.Any(frame => frame.CurrentGuarded))
                {
                    error = " Unguarded R2FU reference on line " + (i + 1) + ": " + trimmed;
                    return false;
                }
            }

            if (stack.Count != 0)
            {
                error = "Unbalanced preprocessor directives: " + stack.Count + " unclosed block(s) at end of file.";
                return false;
            }

            return true;
        }

        private static bool CurrentBranchGuarded(
            bool parentGuarded,
            GuardCondition condition,
            bool priorRequiresNotDefine)
        {
            if (parentGuarded)
                return true;
            if (condition == GuardCondition.RequiresDefine)
                return true;
            if (condition == GuardCondition.RequiresNotDefine)
                return false;
            return priorRequiresNotDefine;
        }

        private static GuardCondition ClassifyCondition(string condition, string define)
        {
            var normalized = condition.Replace(" ", string.Empty);
            if (normalized.Contains("!" + define, StringComparison.Ordinal)
                || normalized.Contains("!defined(" + define + ")", StringComparison.Ordinal))
            {
                return GuardCondition.RequiresNotDefine;
            }

            // Split by C preprocessor operators for exact identifier matching;
            // avoids false match on superstring defines (e.g. DEFINE vs DEFINE_TEST).
            var parts = normalized.Split(new[] { "&&", "||", "!", "(", ")" },
                StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                if (part == define || part == "defined(" + define + ")")
                    return GuardCondition.RequiresDefine;
            }

            return GuardCondition.Unknown;
        }

        private enum GuardCondition
        {
            Unknown,
            RequiresDefine,
            RequiresNotDefine
        }

        private readonly struct GuardFrame
        {
            public GuardFrame(bool parentGuarded, bool currentGuarded, bool priorRequiresNotDefine)
            {
                ParentGuarded = parentGuarded;
                CurrentGuarded = currentGuarded;
                PriorRequiresNotDefine = priorRequiresNotDefine;
            }

            public bool ParentGuarded { get; }
            public bool CurrentGuarded { get; }
            public bool PriorRequiresNotDefine { get; }
        }
    }
}
