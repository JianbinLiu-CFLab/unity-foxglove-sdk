// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Shared ROS2 For Unity validation helpers for artifact and guard checks.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
            if (IsExplicitRuntimePackagePath(path))
                return false;

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

        public static bool IsExplicitRuntimePackagePath(string path)
            => path.StartsWith("Packages/dev.unity2foxglove.ros2forunity.runtime.", StringComparison.Ordinal);

        public static IReadOnlyList<string> GitLsFiles(string repoRoot)
        {
            if (string.IsNullOrWhiteSpace(repoRoot))
                throw new ArgumentException("Repository root is required.", nameof(repoRoot));

            var start = new ProcessStartInfo("git", "ls-files")
            {
                WorkingDirectory = repoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            Process process;
            try
            {
                process = Process.Start(start);
            }
            catch (FileNotFoundException ex)
            {
                throw new InvalidOperationException("git is not in PATH; ROS2 For Unity validation requires git to enumerate tracked files.", ex);
            }

            using (process)
            {
                if (process == null)
                    throw new InvalidOperationException("Could not start git ls-files.");

                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0)
                    throw new InvalidOperationException("git ls-files failed: " + error);

                return output.Replace("\r\n", "\n")
                    .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(path => path.Replace('\\', '/'))
                    .ToList();
            }
        }

        public static bool AllR2fuReferencesAreGuarded(
            string text,
            string define,
            IReadOnlyList<string> tokens,
            out string error)
        {
            error = string.Empty;
            var stack = new Stack<GuardFrame>();
            var lines = text.Replace("\r\n", "\n").Split('\n');

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var trimmed = line.TrimStart();

                if (trimmed.StartsWith("#if ", StringComparison.Ordinal))
                {
                    var condition = ClassifyCondition(trimmed.Substring(4), define);
                    var parentGuarded = CurrentGuarded(stack);
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
                    // `#else` is define-guarded only after an earlier branch required !define.
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

                var token = FindToken(line, tokens);
                if (token != null && !CurrentGuarded(stack))
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

        private static bool CurrentGuarded(Stack<GuardFrame> stack)
            => stack.Count > 0 && stack.Peek().CurrentGuarded;

        private static string FindToken(string line, IReadOnlyList<string> tokens)
        {
            for (var i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i];
                if (line.Contains(token, StringComparison.Ordinal))
                    return token;
            }

            return null;
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
            var normalized = StripOuterParens(condition.Replace(" ", string.Empty));
            if (string.IsNullOrEmpty(normalized))
                return GuardCondition.Unknown;

            // Split on top-level `||` first (OR is lowest-precedence among the
            // operators we handle). A condition `A || B` is RequiresDefine ONLY
            // if every disjunct independently guarantees the define — otherwise
            // a branch like `UNITY_EDITOR || DEFINE` would be wrongly treated as
            // guarded when it can compile in the editor without DEFINE.
            var disjuncts = SplitTopLevel(normalized, "||");

            var allRequireDefine = true;
            var allRequireNotDefine = true;
            foreach (var disjunct in disjuncts)
            {
                var implies = ClassifyConjunction(StripOuterParens(disjunct), define);
                if (implies != GuardCondition.RequiresDefine)
                    allRequireDefine = false;
                if (implies != GuardCondition.RequiresNotDefine)
                    allRequireNotDefine = false;
                if (!allRequireDefine && !allRequireNotDefine)
                    return GuardCondition.Unknown;
            }

            if (allRequireDefine)
                return GuardCondition.RequiresDefine;
            if (allRequireNotDefine)
                return GuardCondition.RequiresNotDefine;
            return GuardCondition.Unknown;
        }

        private static GuardCondition ClassifyConjunction(string conjunction, string define)
        {
            if (string.IsNullOrEmpty(conjunction))
                return GuardCondition.Unknown;

            // A conjunction `A && B && ...` implies the define if ANY conjunct
            // is the bare positive define; implies !define if any is negated.
            var conjuncts = SplitTopLevel(conjunction, "&&");
            foreach (var raw in conjuncts)
            {
                var conjunct = StripOuterParens(raw);
                if (conjunct == define || conjunct == "defined(" + define + ")")
                    return GuardCondition.RequiresDefine;
                if (conjunct == "!" + define || conjunct == "!defined(" + define + ")")
                    return GuardCondition.RequiresNotDefine;
            }

            return GuardCondition.Unknown;
        }

        private static string StripOuterParens(string value)
        {
            var current = value;
            while (current.Length >= 2 && current[0] == '(' && current[current.Length - 1] == ')')
            {
                // Only strip if the leading '(' matches the trailing ')'.
                var depth = 0;
                var matched = true;
                for (var i = 0; i < current.Length - 1; i++)
                {
                    if (current[i] == '(') depth++;
                    else if (current[i] == ')') depth--;
                    if (depth == 0)
                    {
                        matched = false;
                        break;
                    }
                }

                if (!matched)
                    break;
                current = current.Substring(1, current.Length - 2);
            }

            return current;
        }

        private static List<string> SplitTopLevel(string value, string separator)
        {
            var parts = new List<string>();
            var depth = 0;
            var start = 0;
            for (var i = 0; i <= value.Length - separator.Length; i++)
            {
                var c = value[i];
                if (c == '(') depth++;
                else if (c == ')') depth = Math.Max(0, depth - 1);
                else if (depth == 0 && string.CompareOrdinal(value, i, separator, 0, separator.Length) == 0)
                {
                    parts.Add(value.Substring(start, i - start));
                    i += separator.Length - 1;
                    start = i + 1;
                }
            }

            parts.Add(value.Substring(start));
            return parts;
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
