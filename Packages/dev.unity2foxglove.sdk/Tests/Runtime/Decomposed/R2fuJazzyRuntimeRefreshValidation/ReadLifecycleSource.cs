// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 161 validation for the R2FU Jazzy Win64 runtime refresh.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace Unity.FoxgloveSDK.Tests
{
    public static partial class R2fuJazzyRuntimeRefreshValidation
    {
        private static string ReadLifecycleSource(string path)
        {
            if (!NativeBridgeLifecycleSourceCache.TryGetValue(path, out var source))
            {
                source = File.ReadAllText(path);
                NativeBridgeLifecycleSourceCache[path] = source;
            }

            return source;
        }

        private static bool BridgeUpdatePrewarmsRos2FromGuardedPlayMode(string source)
        {
            var body = MethodBody(source, "private void Update()");
            if (body.Length == 0)
                return false;

            var shutdownGate = body.IndexOf("if (IsShuttingDown", StringComparison.Ordinal);
            var ensure = body.IndexOf("EnsureRos2UnityReady()", StringComparison.Ordinal);
            var refresh = body.IndexOf("RefreshBindings();", StringComparison.Ordinal);
            return shutdownGate >= 0
                   && ensure > shutdownGate
                   && refresh > ensure
                   && body.Contains("!_ros2RuntimeWasReady", StringComparison.Ordinal)
                   && !body.Contains("ROS2UnityComponent", StringComparison.Ordinal);
        }

        private static bool BridgeCallbackGetterUsesReadyRuntimeOnly(string source)
        {
            var body = MethodBody(source, "private bool TryGetRos2Unity(out ROS2UnityComponent ros2Unity)");
            var shutdownGate = body.IndexOf("if (IsShuttingDown)", StringComparison.Ordinal);
            var readyGate = body.IndexOf("if (!_ros2RuntimeWasReady)", StringComparison.Ordinal);
            var existing = body.IndexOf("TryGetExistingRos2Unity", StringComparison.Ordinal);
            return shutdownGate >= 0
                   && readyGate > shutdownGate
                   && existing > readyGate
                   && !body.Contains("EnsureRos2UnityReady()", StringComparison.Ordinal);
        }

        private static string MethodBody(string source, string signature)
        {
            var start = source.IndexOf(signature, StringComparison.Ordinal);
            if (start < 0)
                return string.Empty;

            var bodyStart = source.IndexOf('{', start);
            if (bodyStart < 0)
                return string.Empty;

            var depth = 0;
            var bodyEnd = -1;
            for (var i = bodyStart; i < source.Length; i++)
            {
                var current = source[i];
                if (current == '/' && i + 1 < source.Length && source[i + 1] == '/')
                {
                    i = SkipLineComment(source, i + 2);
                    continue;
                }

                if (current == '/' && i + 1 < source.Length && source[i + 1] == '*')
                {
                    i = SkipBlockComment(source, i + 2);
                    continue;
                }

                if (current == '\'')
                {
                    i = SkipCharLiteral(source, i + 1);
                    continue;
                }

                if (current == '"')
                {
                    i = SkipRegularString(source, i + 1);
                    continue;
                }

                if ((current == '@' || current == '$') && i + 1 < source.Length)
                {
                    var next = source[i + 1];
                    if (current == '@' && next == '"')
                    {
                        i = SkipVerbatimString(source, i + 2);
                        continue;
                    }

                    if (current == '$' && next == '"')
                    {
                        i = SkipRegularString(source, i + 2);
                        continue;
                    }

                    if (i + 2 < source.Length
                        && ((current == '$' && next == '@') || (current == '@' && next == '$'))
                        && source[i + 2] == '"')
                    {
                        i = SkipVerbatimString(source, i + 3);
                        continue;
                    }
                }

                if (current == '{')
                    depth++;
                else if (current == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        bodyEnd = i;
                        break;
                    }
                }
            }

            if (bodyEnd <= bodyStart)
                return string.Empty;

            return source.Substring(bodyStart, bodyEnd - bodyStart + 1);
        }

        private static int SkipLineComment(string source, int start)
        {
            for (var i = start; i < source.Length; i++)
            {
                if (source[i] == '\n')
                    return i;
            }

            return source.Length - 1;
        }

        private static int SkipBlockComment(string source, int start)
        {
            for (var i = start; i + 1 < source.Length; i++)
            {
                if (source[i] == '*' && source[i + 1] == '/')
                    return i + 1;
            }

            return source.Length - 1;
        }

        private static int SkipCharLiteral(string source, int start)
        {
            var escaped = false;
            for (var i = start; i < source.Length; i++)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (source[i] == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (source[i] == '\'')
                    return i;
            }

            return source.Length - 1;
        }

        private static int SkipRegularString(string source, int start)
        {
            var escaped = false;
            for (var i = start; i < source.Length; i++)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (source[i] == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (source[i] == '"')
                    return i;
            }

            return source.Length - 1;
        }

        private static int SkipVerbatimString(string source, int start)
        {
            for (var i = start; i < source.Length; i++)
            {
                if (source[i] != '"')
                    continue;

                if (i + 1 < source.Length && source[i + 1] == '"')
                {
                    i++;
                    continue;
                }

                return i;
            }

            return source.Length - 1;
        }

        private static bool NativeDllExists(string fileName)
            => File.Exists(RepoPath(RuntimePackage + "/Runtime/Ros2ForUnity/Plugins/Windows/x86_64/" + fileName));

        private static bool RepoFileExists(string relativePath)
            => File.Exists(RepoPath(relativePath));

        private static string ReadRepoText(string relativePath)
        {
            var path = RepoPath(relativePath);
            Check(File.Exists(path), $"161-file: {relativePath} exists");
            if (FileTextCache.TryGetValue(path, out var cached))
                return cached;

            var text = File.ReadAllText(path);
            FileTextCache[path] = text;
            return text;
        }

        private static string RepoPath(string relativePath)
            => Path.Combine(Phase16Validation.FindRepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));

        private static string[] RuntimePackageKeys(string json)
        {
            var matches = Regex.Matches(
                json ?? string.Empty,
                "\"(dev\\.unity2foxglove\\.ros2forunity\\.runtime\\.[^\"]+)\"\\s*:");
            return matches.Cast<Match>().Select(match => match.Groups[1].Value).ToArray();
        }

        private static string RuntimeId(string runtimeManifest)
        {
            var match = Regex.Match(
                runtimeManifest ?? string.Empty,
                "\"runtimeId\"\\s*:\\s*\"([^\"]+)\"",
                RegexOptions.CultureInvariant);
            return match.Success ? match.Groups[1].Value : string.Empty;
        }

        private static string ExpectedRuntimeId(string runtimePackage)
        {
            const string prefix = "dev.unity2foxglove.ros2forunity.runtime.";
            var suffix = runtimePackage.StartsWith(prefix, StringComparison.Ordinal)
                ? runtimePackage.Substring(prefix.Length)
                : runtimePackage;
            return "r2fu-" + suffix.Replace('.', '-');
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
            {
                Failures.Add(message);
                Console.WriteLine("[FAIL] " + message);
                return;
            }

            _passed++;
            Console.WriteLine("[PASS] " + message);
        }

        private static void ThrowIfFailures()
        {
            if (Failures.Count == 0)
                return;

            throw new Exception(
                "[FAIL] Phase 161 validation failed " + Failures.Count + " check(s): "
                + string.Join("; ", Failures));
        }
    }
}
