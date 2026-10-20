// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Phase 140-89/90/91/92/94 runtime validation optimization checks.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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

        [Theory]
        [InlineData("Phase121Validation.cs", "ReadRepoText")]
        [InlineData("Phase128Validation.cs", "ReadRepoText")]
        [InlineData("Phase129Validation.cs", "ReadRepoText")]
        [InlineData("Phase130Validation.cs", "ReadRepoText")]
        [InlineData("Phase131Validation.cs", "ReadRepoText")]
        [InlineData("Phase132Validation.cs", "ReadRepoText")]
        [InlineData("Phase134_30Validation.cs", "ReadRepoText")]
        [InlineData("Phase134_31Validation.cs", "ReadRepoText")]
        [InlineData("Phase164_42Validation.cs", "Read")]
        [InlineData("Phase163_25Validation.cs", "ReadRepoText")]
        [InlineData("Phase163_44Validation.cs", "Read")]
        [InlineData("Phase105Validation.cs", "ReadRepoText")]
        public void RuntimeReadersDelegateToSplitAwareHelper(string filename, string readerName)
        {
            var source = CSharpSyntaxTree.ParseText(TestSources.Runtime(filename));
            var method = Assert.Single(
                source.GetRoot()
                    .DescendantNodes()
                    .OfType<MethodDeclarationSyntax>(),
                candidate => candidate.Identifier.ValueText == readerName);
            var invocation = Assert.IsType<InvocationExpressionSyntax>(method.ExpressionBody?.Expression);

            Assert.Equal(
                "PhaseValidationSourceHelpers.ReadRequiredRepoText",
                invocation.Expression.ToString());
            var argument = Assert.Single(invocation.ArgumentList.Arguments);
            Assert.Equal("relativePath", argument.Expression.ToString());
        }

        [Fact]
        public void FoxgloveLogHubSectionsStayWithinSourceSizeBudget()
        {
            var files = new[]
            {
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/Decomposed/FoxgloveLogHub/DeferredSourceChanges.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/Decomposed/FoxgloveLogHub/LifecycleAndRegistration.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/Decomposed/FoxgloveLogHub/PublishingAndEncoding.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/Decomposed/FoxgloveLogHub/TransportSelection.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/Decomposed/FoxgloveLogHub/ScanningAndDiagnostics.cs",
            };

            foreach (var file in files)
            {
                var lineCount = TestSources.Text(file).Split('\n').Length;
                Assert.True(lineCount <= 500, file + " has " + lineCount + " lines.");
            }
        }

        [Fact]
        public void RegisteredCSharpSourceEnumerationIsRecursive()
        {
            var source = TestSources.Runtime("PhaseValidationSourceHelpers.cs");
            var method = TestSources.Slice(
                source,
                "public static string ReadRegisteredCSharpSource",
                "        public static string ReadSplitPythonSource");

            Assert.Contains("SearchOption.AllDirectories", method, StringComparison.Ordinal);
        }

        [Fact]
        public void Phase14089MigratedConsolePhaseIsRemoved()
            => TestSources.AssertConsolePhaseRemoved("Phase140_89Validation.cs", "--phase140-89", "Phase140_89Validation.Validate");
    }






internal static class TestSources
    {
        private static readonly string CachedRepoRoot = FindRepoRoot();

        public static string Runtime(string fileName)
            => Text("Packages/dev.unity2foxglove.sdk/Tests/Runtime/" + fileName);

        public static string Text(string relativePath)
        {
            var normalized = relativePath.Replace('\\', '/');
            if (string.Equals(
                    normalized,
                    "Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Replay/McapReplayEngine.cs",
                    StringComparison.Ordinal))
                return McapReplayEngineSources();
            if (string.Equals(
                    normalized,
                    "Packages/dev.unity2foxglove.sdk/Runtime/Core/Runtime/FoxgloveRuntime.cs",
                    StringComparison.Ordinal))
                return FoxgloveRuntimeSources();
            if (string.Equals(
                    normalized,
                    "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/MediaFoundationH264EncoderSidecar.cs",
                    StringComparison.Ordinal))
                return MediaFoundationH264EncoderSidecarSources();
            if (string.Equals(
                    normalized,
                    "Packages/dev.unity2foxglove.sdk/Editor/Publishers/FoxgloveCameraPublisherEditor.cs",
                    StringComparison.Ordinal))
                return CameraPublisherEditorSources();
            if (string.Equals(
                    normalized,
                    "Packages/dev.unity2foxglove.sdk/Tests/Runtime/Program.cs",
                    StringComparison.Ordinal))
                return ProgramSources();

            var path = Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), "Source file not found: " + relativePath + " (" + path + ")");
            return ReadLogicalSource(path);
        }

        private static string CameraPublisherEditorSources()
        {
            var directory = Path.Combine(
                RepoRoot,
                "Packages",
                "dev.unity2foxglove.sdk",
                "Editor",
                "Publishers");
            var path = Path.Combine(directory, "FoxgloveCameraPublisherEditor.cs");
            Assert.True(File.Exists(path), "Camera publisher editor source not found: " + path);
            return File.ReadAllText(path);
        }

        private static string ProgramSources()
        {
            var directory = Path.Combine(RepoRoot, "Packages", "dev.unity2foxglove.sdk", "Tests", "Runtime");
            var sectionDirectory = Path.Combine(directory, "Decomposed", "Program");
            var names = new[] { "ValidationExecution.cs", "LegacyTools.cs", "CiAndServer.cs", "MainCore.cs", "CollectOptionValues.cs" };
            var actualNames = Directory.GetFiles(
                    sectionDirectory,
                    "*.cs",
                    SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            var expectedNames = names
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            Assert.Equal(expectedNames, actualNames);
            var paths = new[] { Path.Combine(directory, "Program.cs") }
                .Concat(names.Select(name => Path.Combine(sectionDirectory, name)))
                .ToArray();
            foreach (var path in paths)
                Assert.True(File.Exists(path), "Program source not found: " + path);

            return string.Join(Environment.NewLine, paths.Select(File.ReadAllText));
        }

        private static string FoxgloveRuntimeSources()
        {
            var root = Path.Combine(
                RepoRoot,
                "Packages",
                "dev.unity2foxglove.sdk",
                "Runtime",
                "Core",
                "Runtime");
            var fileNames = new[]
            {
                "FoxgloveRuntime.cs",
                "FoxgloveRuntime.Lifecycle.cs",
                "FoxgloveRuntime.Publishing.cs",
                "FoxgloveRuntime.RecordingAndReplay.cs",
                "FoxgloveRuntime.RuntimeAndSchema.cs",
                "FoxgloveRuntime.ReplaySuppression.cs",
                "FoxgloveRuntime.SchemaRegistration.cs",
            };
            var facade = File.ReadAllText(Path.Combine(root, fileNames[0]));
            var marker = "    /// <summary>Shared executable generation predicate for main-thread client events.";
            var gateStart = facade.LastIndexOf(marker, StringComparison.Ordinal);
            Assert.True(gateStart >= 0, "FoxgloveRuntime facade is missing its trailing gate.");
            var source = facade.Substring(0, gateStart).TrimEnd();
            foreach (var fileName in fileNames.Skip(1))
                source += Environment.NewLine + File.ReadAllText(Path.Combine(root, fileName));
            return source + Environment.NewLine + facade.Substring(gateStart).TrimStart();
        }

        private static string MediaFoundationH264EncoderSidecarSources()
        {
            var root = Path.Combine(
                RepoRoot,
                "Packages",
                "dev.unity2foxglove.sdk",
                "Runtime",
                "Schemas",
                "Proto",
                "Video");
            var fileNames = new[]
            {
                "MediaFoundationH264EncoderSidecar.cs",
                "MediaFoundationH264EncoderSidecar.Lifecycle.cs",
                "MediaFoundationH264EncoderSidecar.NativeLifecycle.cs",
                "MediaFoundationH264EncoderSidecar.Output.cs",
                "MediaFoundationH264EncoderSidecar.ComInterop.cs",
            };
            var paths = fileNames
                .Select(name => Path.Combine(root, name))
                .Where(File.Exists)
                .ToArray();
            Assert.NotEmpty(paths);
            return string.Join(Environment.NewLine, paths.Select(File.ReadAllText));
        }

        private static string ReadLogicalSource(string facadePath)
        {
            var source = File.ReadAllText(facadePath);
            var directory = Path.GetDirectoryName(facadePath);
            Assert.NotNull(directory);
            var package = Path.Combine(directory, Path.GetFileNameWithoutExtension(facadePath));
            if (Path.GetExtension(facadePath).Equals(".py", StringComparison.OrdinalIgnoreCase)
                && Directory.Exists(package)
                && File.Exists(Path.Combine(package, "__init__.py")))
            {
                var sectionNames = Regex.Matches(
                        File.ReadAllText(Path.Combine(package, "__init__.py")),
                        @"from\s+\.\s*(?:import\s+)?(?<name>[A-Za-z_][A-Za-z0-9_]*)")
                    .Cast<Match>()
                    .Select(match => match.Groups["name"].Value)
                    .Where(name => !name.Equals("__init__", StringComparison.Ordinal))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                foreach (var name in sectionNames)
                {
                    var section = Path.Combine(package, name + ".py");
                    Assert.True(File.Exists(section), "Source section not found: " + section);
                    source += Environment.NewLine + File.ReadAllText(section);
                }
                return source;
            }

            var stem = Path.GetFileNameWithoutExtension(facadePath);
            var directoryInfo = new DirectoryInfo(directory);
            var sectionDirectories = new System.Collections.Generic.List<string>();
            for (var ancestor = directoryInfo; ancestor != null; ancestor = ancestor.Parent)
            {
                var relativeDirectory = Path.GetRelativePath(ancestor.FullName, directoryInfo.FullName);
                var decomposed = Path.Combine(ancestor.FullName, "Decomposed", relativeDirectory, stem);
                if (Directory.Exists(decomposed))
                    sectionDirectories.Add(Path.GetFullPath(decomposed));
                if (File.Exists(Path.Combine(ancestor.FullName, ".git"))
                    || Directory.Exists(Path.Combine(ancestor.FullName, ".git")))
                    break;
            }
            var localDecomposed = Path.Combine(directoryInfo.FullName, "Decomposed");
            if (Directory.Exists(localDecomposed))
            {
                var marker = File.ReadLines(facadePath)
                    .Select(line => Regex.Match(
                        line,
                        @"^\s*///\s*<remarks>Phase192 source sections:\s*(?<names>[^<]+)</remarks>\s*$",
                        RegexOptions.CultureInvariant))
                    .FirstOrDefault(match => match.Success);
                if (marker != null)
                {
                    var names = marker.Groups["names"].Value
                        .Split(',')
                        .Select(name => name.Trim())
                        .Where(name => name.Length > 0)
                        .ToArray();
                    foreach (var child in Directory.GetDirectories(localDecomposed, stem, SearchOption.TopDirectoryOnly))
                    {
                        var files = Directory.GetFiles(child, "*.cs", SearchOption.TopDirectoryOnly);
                        var fileNames = new HashSet<string>(
                            files.Select(Path.GetFileName),
                            StringComparer.OrdinalIgnoreCase);
                        if (names.Length == files.Length
                            && names.All(fileNames.Contains))
                            sectionDirectories.Add(Path.GetFullPath(child));
                    }
                }
            }
            sectionDirectories = sectionDirectories
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();
            Assert.True(
                sectionDirectories.Count <= 1,
                "Ambiguous decomposed source directories for " + facadePath + ": "
                + string.Join(", ", sectionDirectories));
            var sections = sectionDirectories.Count == 0
                ? Array.Empty<string>()
                : EnumerateCSharpSections(facadePath, sectionDirectories[0]).ToArray();
            foreach (var section in sections)
                source += Environment.NewLine + File.ReadAllText(section);
            return source;
        }

        private static IEnumerable<string> EnumerateCSharpSections(
            string facadePath,
            string sectionDirectory)
        {
            var files = Directory.GetFiles(sectionDirectory, "*.cs", SearchOption.TopDirectoryOnly);
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
            var filesByName = files.ToDictionary(Path.GetFileName, StringComparer.OrdinalIgnoreCase);
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
