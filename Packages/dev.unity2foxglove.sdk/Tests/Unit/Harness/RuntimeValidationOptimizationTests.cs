// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Phase 140-89/90/91/92/94 runtime validation optimization checks.

using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    [Trait("Phase", "140-89")]
    [Trait("Domain", "Harness")]
    public sealed class Ros2BridgeSchemaOptimizationTests
    {
        [Fact]
        public void Phase90CachesStableRelativePathsBeforeHashing()
        {
            var method = TestSources.Slice(TestSources.Runtime("Phase90Validation.cs"), "private static string ComputeSourceTreeSha256", "        private static string ToStableRelativePath");

            Assert.Contains("Select(path => new SourceFilePath(path, ToStableRelativePath(sourceRoot, path)))", method, StringComparison.Ordinal);
            Assert.Contains("OrderBy(file => file.RelativePath", method, StringComparison.Ordinal);
            Assert.Contains("Encoding.UTF8.GetBytes(file.RelativePath)", method, StringComparison.Ordinal);
        }

        [Fact]
        public void Phase91ReusesPointCloudFrameAcrossBuilderChecks()
        {
            var source = TestSources.Runtime("Phase91Validation.cs");
            var verifyMethod = TestSources.Slice(source, "private static void VerifyMessageBuilders", "        private static void VerifyFrameTransformBuilder");

            Assert.Contains("var pointFrame = BuildPointCloudFrame();", verifyMethod, StringComparison.Ordinal);
            Assert.Contains("VerifyPointCloudBuilder(pointFrame);", verifyMethod, StringComparison.Ordinal);
            Assert.Contains("VerifyCompressedPointCloudBuilder(pointFrame);", verifyMethod, StringComparison.Ordinal);
            Assert.Contains("private static void VerifyPointCloudSharedPacking(PointCloudFrame frame)", source, StringComparison.Ordinal);
        }

        [Fact]
        public void Phase100DelegatesMethodExtractionToSharedRoslynParser()
        {
            var source = TestSources.Runtime("Phase100Validation.cs");
            var method = TestSources.Slice(source, "private static string ExtractMethodBody", "        private static string ReadRepoText");

            Assert.Contains("PhaseValidationSourceHelpers.SourceMethod(source, methodName)", method, StringComparison.Ordinal);
            Assert.DoesNotContain("FindMethodSignature", source, StringComparison.Ordinal);
            Assert.DoesNotContain("MethodSignatureRegexes", source, StringComparison.Ordinal);
        }

        [Fact]
        public void Phase14089MigratedConsolePhaseIsRemoved()
            => TestSources.AssertConsolePhaseRemoved("Phase140_89Validation.cs", "--phase140-89", "Phase140_89Validation.Validate");
    }






class TestSources
    {
        private static readonly string CachedRepoRoot = FindRepoRoot();

        public static string Runtime(string fileName)
            => Text("Packages/dev.unity2foxglove.sdk/Tests/Runtime/" + fileName);

        public static string Text(string relativePath)
        {
            if (string.Equals(
                    relativePath.Replace('\\', '/'),
                    "Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Replay/McapReplayEngine.cs",
                    StringComparison.Ordinal))
                return McapReplayEngineSources();

            var path = Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), "Source file not found: " + relativePath + " (" + path + ")");
            return File.ReadAllText(path);
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

            return string.Join(
                Environment.NewLine,
                paths.Select(path => TextWithoutComposition(path)));
        }

        private static string TextWithoutComposition(string relativePath)
        {
            var path = Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), "Source file not found: " + relativePath + " (" + path + ")");
            return File.ReadAllText(path);
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

        public static string ExtractMethod(string source, string signature)
        {
            var index = source.IndexOf(signature, StringComparison.Ordinal);
            Assert.True(index >= 0, "Could not locate method signature: " + signature);
            var brace = source.IndexOf('{', index);
            Assert.True(brace >= 0, "Could not locate method body: " + signature);

            var depth = 0;
            var inString = false;
            var inVerbatimString = false;
            var inCharacter = false;
            var inLineComment = false;
            var inBlockComment = false;
            var inBacktickString = false;
            var rawStringDelimiterLength = 0;
            for (var i = brace; i < source.Length; i++)
            {
                var current = source[i];
                var next = i + 1 < source.Length ? source[i + 1] : '\0';

                if (inLineComment)
                {
                    if (current == '\n')
                        inLineComment = false;
                    continue;
                }

                if (inBlockComment)
                {
                    if (current == '*' && next == '/')
                    {
                        inBlockComment = false;
                        i++;
                    }
                    continue;
                }

                if (rawStringDelimiterLength > 0)
                {
                    if (current == '"' && CountRun(source, i, '"') >= rawStringDelimiterLength)
                    {
                        i += rawStringDelimiterLength - 1;
                        rawStringDelimiterLength = 0;
                    }
                    continue;
                }

                if (inString)
                {
                    if (inVerbatimString && current == '"' && next == '"')
                    {
                        i++;
                        continue;
                    }

                    if (current == '"' && (inVerbatimString || !IsEscaped(source, i)))
                    {
                        inString = false;
                        inVerbatimString = false;
                    }
                    continue;
                }

                if (inCharacter)
                {
                    if (current == '\'' && !IsEscaped(source, i))
                        inCharacter = false;
                    continue;
                }

                if (inBacktickString)
                {
                    if (current == '`' && !IsEscaped(source, i))
                        inBacktickString = false;
                    continue;
                }

                if (current == '/' && next == '/')
                {
                    inLineComment = true;
                    i++;
                    continue;
                }

                if (current == '/' && next == '*')
                {
                    inBlockComment = true;
                    i++;
                    continue;
                }

                if (current == '"')
                {
                    var quoteRun = CountRun(source, i, '"');
                    if (quoteRun >= 3)
                    {
                        rawStringDelimiterLength = quoteRun;
                        i += quoteRun - 1;
                    }
                    else
                    {
                        inString = true;
                        inVerbatimString = i > 0 && source[i - 1] == '@';
                    }
                    continue;
                }

                if (current == '\'')
                {
                    inCharacter = true;
                    continue;
                }

                if (current == '`')
                {
                    inBacktickString = true;
                    continue;
                }

                if (current == '{') depth++;
                else if (current == '}')
                {
                    depth--;
                    if (depth == 0)
                        return source.Substring(index, i - index + 1);
                }
            }

            return source.Substring(index);
        }

        private static int CountRun(string source, int index, char value)
        {
            var count = 0;
            while (index + count < source.Length && source[index + count] == value)
                count++;
            return count;
        }

        private static bool IsEscaped(string source, int index)
        {
            var slashCount = 0;
            for (var i = index - 1; i >= 0 && source[i] == '\\'; i--)
                slashCount++;
            return slashCount % 2 == 1;
        }

        public static int Count(string source, string value)
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

        private static string NormalizeLineEndings(string text)
            => (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);

        private static string RepoRoot
            => CachedRepoRoot;

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

            throw new DirectoryNotFoundException("Could not locate repository root from " + AppContext.BaseDirectory);
        }
    }

}
