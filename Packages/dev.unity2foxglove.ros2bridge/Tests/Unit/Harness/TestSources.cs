// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2Bridge.Tests/Unit
// Purpose: Repository-source helpers owned by the ROS2 Bridge test assembly.

using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    internal static class TestSources
    {
        private static readonly string CachedRepoRoot = FindRepoRoot();

        public static string Runtime(string fileName)
            => Text("Packages/dev.unity2foxglove.sdk/Tests/Runtime/" + fileName);

        public static string Text(string relativePath)
        {
            var path = Path.Combine(
                CachedRepoRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(
                File.Exists(path),
                "Source file not found: " + relativePath + " (" + path + ")");
            var source = File.ReadAllText(path);
            var sectionDirectory = Path.Combine(
                Path.GetDirectoryName(path)!,
                "Decomposed",
                Path.GetFileNameWithoutExtension(path));
            if (!Directory.Exists(sectionDirectory))
                return source;

            var sectionFiles = Directory.GetFiles(sectionDirectory, "*.cs");
            var marker = source
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                .Select(line => Regex.Match(
                    line,
                    @"^\s*///\s*<remarks>Phase192 source sections:\s*(?<names>[^<]+)</remarks>\s*$"))
                .FirstOrDefault(match => match.Success);
            var orderedFiles = sectionFiles.OrderBy(file => file, StringComparer.Ordinal);
            if (marker != null)
            {
                var names = marker.Groups["names"].Value
                    .Split(',')
                    .Select(name => name.Trim())
                    .Where(name => name.Length > 0)
                    .ToArray();
                var filesByName = sectionFiles.ToDictionary(
                    file => Path.GetFileName(file),
                    StringComparer.OrdinalIgnoreCase);
                orderedFiles = names.Select(name => filesByName[name]);
            }

            return source + Environment.NewLine + string.Join(
                Environment.NewLine,
                orderedFiles.Select(File.ReadAllText));
        }

        public static string Slice(string source, string startText, string endText)
        {
            var normalized = NormalizeLineEndings(source);
            var normalizedStart = NormalizeLineEndings(startText);
            var normalizedEnd = NormalizeLineEndings(endText);
            var start = normalized.IndexOf(normalizedStart, StringComparison.Ordinal);
            Assert.True(start >= 0, "Could not locate source slice start: " + startText);

            var end = normalized.IndexOf(
                normalizedEnd,
                start + normalizedStart.Length,
                StringComparison.Ordinal);
            if (end < 0)
                end = normalized.Length;

            return normalized.Substring(start, end - start);
        }

        public static void AssertConsolePhaseRemoved(
            string validationFile,
            string flag,
            string entryPoint)
        {
            Assert.DoesNotContain(
                validationFile,
                Runtime("FoxgloveSdk.Tests.csproj"),
                StringComparison.Ordinal);
            var registry = Runtime("PhaseValidationRegistry.cs");
            Assert.DoesNotContain("\"" + flag + "\"", registry, StringComparison.Ordinal);
            Assert.DoesNotContain(entryPoint, registry, StringComparison.Ordinal);
        }

        private static string NormalizeLineEndings(string text)
            => (text ?? string.Empty)
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace("\r", "\n", StringComparison.Ordinal);

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Unity2Foxglove.sln"))
                    || Directory.Exists(Path.Combine(dir.FullName, ".git"))
                    || File.Exists(Path.Combine(dir.FullName, ".git")))
                    return dir.FullName;

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException(
                "Could not locate repository root from " + AppContext.BaseDirectory);
        }
    }
}
