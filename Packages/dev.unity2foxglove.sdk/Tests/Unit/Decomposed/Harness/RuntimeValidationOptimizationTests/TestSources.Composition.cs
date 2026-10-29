// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Shared source fixtures for runtime validation optimization tests.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.FoxgloveSDK.Tests;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    internal static partial class TestSources
    {
        private static string Phase184AcceptanceSources()
        {
            var directory = Path.Combine(
                RepoRoot,
                "Unity2Foxglove",
                "Assets",
                "Scripts",
                "ManualAcceptance");
            var sectionDirectory = Path.Combine(
                directory,
                "Decomposed",
                "Phase184FoxRunProfileAcceptance");
            var facadePath = Path.Combine(directory, "Phase184FoxRunProfileAcceptance.cs");
            var facade = File.ReadAllText(facadePath);
            var mainSections = new[]
            {
                "Phase184FoxRunProfileAcceptance.Lifecycle.cs",
                "Phase184FoxRunProfileAcceptance.Context.cs",
            };
            var source = facade;
            var insertion = new System.Text.StringBuilder();
            foreach (var name in mainSections)
            {
                var section = File.ReadAllText(Path.Combine(sectionDirectory, name));
                var declaration = "public sealed partial class Phase184FoxRunProfileAcceptance";
                var declarationIndex = section.IndexOf(declaration, StringComparison.Ordinal);
                Assert.True(declarationIndex >= 0, "Missing Phase184 acceptance declaration in " + name);
                var openBrace = section.IndexOf('{', declarationIndex);
                var close = section.LastIndexOf("\n    }\n}", StringComparison.Ordinal);
                Assert.True(openBrace >= 0 && close > openBrace, "Invalid Phase184 acceptance section " + name);
                insertion.Append(section.Substring(openBrace + 1, close - openBrace - 1));
            }
            var facadeClose = source.LastIndexOf("\n    }\n}\n", StringComparison.Ordinal);
            Assert.True(facadeClose >= 0, "Invalid Phase184 acceptance facade.");
            source = source.Insert(facadeClose, insertion.ToString());
            var remaining = new[]
            {
                "Phase184TransportClientMarkerState.cs",
                "Phase184AcceptanceRoute.cs",
                "Phase184FoxgloveProfileRoute.cs",
                "Phase184MultiTargetRoute.cs",
                "Phase184DegradedTargetRoute.cs",
                "Phase184QosContractRoute.cs",
                "Phase184StreamRoute.cs",
                "Phase184AcceptanceText.cs",
            };
            foreach (var name in remaining)
                source += Environment.NewLine + File.ReadAllText(Path.Combine(sectionDirectory, name));
            return source;
        }

        private static IEnumerable<string> EnumerateCSharpSections(
            string facadePath,
            string sectionDirectory)
        {
            var files = Directory.GetFiles(sectionDirectory, "*.cs", SearchOption.AllDirectories);
            var marker = File.ReadLines(facadePath)
                .Select(line => Regex.Match(
                    line,
                    @"^\s*///\s*<remarks>Phase192 source sections:\s*(?<names>[^<]+)</remarks>\s*$",
                    RegexOptions.CultureInvariant))
                .FirstOrDefault(match => match.Success);
            if (marker == null)
                return files.OrderBy(path => path, StringComparer.Ordinal);

            var names = marker.Groups["names"].Value
                .Split(',')
                .Select(name => name.Trim())
                .Where(name => name.Length > 0)
                .ToArray();
            var filesByName = files.ToDictionary(
                path => Path.GetRelativePath(sectionDirectory, path).Replace('\\', '/'),
                StringComparer.OrdinalIgnoreCase);
            Assert.True(
                names.Length == filesByName.Count
                && names.Distinct(StringComparer.OrdinalIgnoreCase).Count() == names.Length
                && names.All(filesByName.ContainsKey),
                "Invalid Phase192 source-section order in " + facadePath);
            return names.Select(name => filesByName[name]);
        }

        private static string McapReplayEngineSources()
        {
            var paths = new[]
            {
                "Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Replay/McapReplayEngine.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Replay/Decomposed/McapReplayEngine/Load.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Replay/Decomposed/McapReplayEngine/History.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Replay/Decomposed/McapReplayEngine/TryReadIndexedBoundedHistory.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Replay/Decomposed/McapReplayEngine/HistoryCandidate.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Replay/Decomposed/McapReplayEngine/SortPending.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Replay/Decomposed/McapReplayEngine/FinishTickResult.cs"
            };

            var facade = TextWithoutComposition(paths[0]);
            var fragments = paths.Skip(1).Select(path => TextWithoutComposition(path));
            return string.Join(
                Environment.NewLine + Environment.NewLine,
                new[] { facade }.Concat(fragments))
                + Environment.NewLine
                + "    }"
                + Environment.NewLine
                + "}";
        }

        private static string TextWithoutComposition(string relativePath)
        {
            var path = Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), "Source file not found: " + relativePath + " (" + path + ")");
            var lines = File.ReadAllLines(path);
            if (relativePath.EndsWith("McapReplayEngine.cs", StringComparison.Ordinal))
            {
                Assert.True(lines.Length >= 3, "MCAP replay facade is too short: " + relativePath);
                return string.Join(Environment.NewLine, lines.Take(lines.Length - 2));
            }

            var classLine = Array.FindIndex(
                lines,
                line => line.Contains("partial class McapReplayEngine", StringComparison.Ordinal));
            Assert.True(classLine >= 0, "MCAP replay fragment class is missing: " + relativePath);
            var openBrace = Array.FindIndex(
                lines,
                classLine,
                line => line.Trim() == "{");
            Assert.True(openBrace >= 0 && lines.Length - openBrace >= 4, "MCAP replay fragment is too short: " + relativePath);
            return string.Join(Environment.NewLine, lines.Skip(openBrace + 1).Take(lines.Length - openBrace - 3));
        }

        public static string SourceGeneratorSources()
        {
            var dir = Path.Combine(
                RepoRoot,
                "Packages",
                "dev.unity2foxglove.sdk",
                "Editor",
                "SourceGenerators",
                "src");
            Assert.True(Directory.Exists(dir), "Source generator src directory not found: " + dir);

            var files = Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            return string.Join(Environment.NewLine, files.Select(File.ReadAllText));
        }

        public static string ManagerEditorSources()
        {
            var dir = Path.Combine(
                RepoRoot,
                "Packages",
                "dev.unity2foxglove.sdk",
                "Editor",
                "Manager");
            Assert.True(Directory.Exists(dir), "Manager editor directory not found: " + dir);

            var files = Directory.GetFiles(dir, "FoxgloveManagerEditor*.cs")
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            return string.Join(Environment.NewLine, files.Select(File.ReadAllText));
        }

        public static string ManagerPublishingSources()
        {
            var dir = Path.Combine(
                RepoRoot,
                "Packages",
                "dev.unity2foxglove.sdk",
                "Runtime",
                "Components",
                "Manager");
            Assert.True(Directory.Exists(dir), "Manager directory not found: " + dir);

            var files = Directory.GetFiles(dir, "FoxgloveManager.Publishing*.cs")
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            return string.Join(Environment.NewLine, files.Select(File.ReadAllText));
        }

        public static void AssertConsolePhaseRemoved(string validationFile, string flag, string entryPoint)
        {
            Assert.DoesNotContain(validationFile, Runtime("FoxgloveSdk.Tests.csproj"), StringComparison.Ordinal);
            var removedPath = Path.Combine(RepoRoot, "Packages", "dev.unity2foxglove.sdk", "Tests", "Runtime", validationFile);
            Assert.False(File.Exists(removedPath), "Migrated console validation source still exists: " + removedPath);
            var registry = Runtime("PhaseValidationRegistry.cs");
            Assert.DoesNotContain("\"" + flag + "\"", registry, StringComparison.Ordinal);
            Assert.DoesNotContain(entryPoint, registry, StringComparison.Ordinal);
        }

        public static string Slice(string source, string startText, string endText)
        {
            var normalized = NormalizeLineEndings(source);
            var normalizedStart = NormalizeLineEndings(startText);
            var normalizedEnd = NormalizeLineEndings(endText);
            var start = normalized.IndexOf(normalizedStart, StringComparison.Ordinal);
            Assert.True(start >= 0, "Could not locate source slice start: " + startText);
            var end = normalized.IndexOf(normalizedEnd, start + normalizedStart.Length, StringComparison.Ordinal);
            Assert.True(end >= 0, "Could not locate source slice end: " + endText);
            return normalized.Substring(start, end - start);
        }

    }
}
