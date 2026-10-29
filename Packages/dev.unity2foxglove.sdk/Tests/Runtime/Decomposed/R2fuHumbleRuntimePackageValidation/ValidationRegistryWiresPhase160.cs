// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 160 validation for the R2FU Humble Win64 runtime package.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace Unity.FoxgloveSDK.Tests
{
    public static partial class R2fuHumbleRuntimePackageValidation
    {
        private static void ValidationRegistryWiresPhase160()
        {
            var registry = ReadRepoText(RegistryPath);
            var project = ReadRepoText(ProjectPath);

            Check(registry.Contains("Ci(\"--phase160\"", StringComparison.Ordinal)
                  && registry.Contains("R2fuHumbleRuntimePackageValidation.Validate", StringComparison.Ordinal),
                "160-H1: validation registry wires --phase160");
            Check(project.Contains("R2fuHumbleRuntimePackageValidation.cs", StringComparison.Ordinal),
                "160-H2: runtime validation project compiles the 160 validation");
        }

        private static bool NativeDllExists(string fileName)
            => File.Exists(RepoPath(RuntimePackage + "/Runtime/Ros2ForUnity/Plugins/Windows/x86_64/" + fileName));

        private static bool RepoFileExists(string relativePath)
            => File.Exists(RepoPath(relativePath));

        private static string ReadRepoText(string relativePath)
        {
            var path = RepoPath(relativePath);
            Check(File.Exists(path), $"160-file: {relativePath} exists");
            if (FileTextCache.TryGetValue(path, out var cached))
                return cached;

            var text = path.EndsWith(".py", StringComparison.OrdinalIgnoreCase)
                ? PhaseValidationSourceHelpers.ReadSplitPythonSourcePath(path)
                : PhaseValidationSourceHelpers.ReadRequiredRepoText(relativePath);
            FileTextCache[path] = text;
            return text;
        }

        private static string RepoPath(string relativePath)
            => Path.Combine(Phase16Validation.FindRepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));

        private static string[] RuntimePackageKeys(string json)
        {
            const string runtimePackagePrefix = "dev.unity2foxglove.ros2forunity.runtime.";
            var dependencies = JObject.Parse(json ?? string.Empty)["dependencies"] as JObject;
            return dependencies?.Properties()
                .Select(property => property.Name)
                .Where(name => name.StartsWith(runtimePackagePrefix, StringComparison.Ordinal))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray()
                ?? Array.Empty<string>();
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

        private static string RuntimeSettingsActivePackage(string settingsJson)
        {
            var match = Regex.Match(
                settingsJson ?? string.Empty,
                "\"activeRuntimePackage\"\\s*:\\s*\"([^\"]+)\"",
                RegexOptions.CultureInvariant);
            return match.Success ? match.Groups[1].Value : string.Empty;
        }

        private static string PlannedRuntimeSection(string json)
        {
            var match = Regex.Match(
                json ?? string.Empty,
                "\"plannedRuntimePackages\"\\s*:\\s*\\[(.*?)\\]",
                RegexOptions.Singleline | RegexOptions.CultureInvariant);
            return match.Success ? match.Groups[1].Value : string.Empty;
        }

        private static string ExtractMethodBody(string source, string methodName)
        {
            var marker = methodName + "(";
            var signature = source.IndexOf(marker, StringComparison.Ordinal);
            if (signature < 0)
                return string.Empty;

            var openBrace = source.IndexOf('{', signature);
            if (openBrace < 0)
                return string.Empty;

            var depth = 0;
            for (var i = openBrace; i < source.Length; i++)
            {
                if (source[i] == '{')
                    depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return source.Substring(openBrace, i - openBrace + 1);
                }
            }

            return string.Empty;
        }

        private static int CountSubstring(string source, string value)
        {
            var count = 0;
            var index = 0;
            while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += value.Length;
            }

            return count;
        }

        private static void RunCheckGroup(string name, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Failures.Add(name + ": " + ex.Message);
                Console.WriteLine("[FAIL] " + name + ": " + ex.Message);
            }
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
    }
}
