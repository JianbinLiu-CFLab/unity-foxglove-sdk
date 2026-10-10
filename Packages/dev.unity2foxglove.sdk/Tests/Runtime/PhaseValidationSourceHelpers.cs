// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Shared source inspection helpers for runtime validation phases.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class PhaseValidationSourceHelpers
    {
        private const string FoxgloveRuntimeTrailingGateMarker =
            "    /// <summary>Shared executable generation predicate for main-thread client events.";
        private static readonly string[] FoxgloveRuntimeSourceFileNames =
        {
            "FoxgloveRuntime.cs",
            "FoxgloveRuntime.Lifecycle.cs",
            "FoxgloveRuntime.Publishing.cs",
            "FoxgloveRuntime.RecordingAndReplay.cs",
            "FoxgloveRuntime.RuntimeAndSchema.cs",
            "FoxgloveRuntime.ReplaySuppression.cs",
            "FoxgloveRuntime.SchemaRegistration.cs",
        };
        private static readonly HashSet<string> PythonSectionKeywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "False", "None", "True", "and", "as", "assert", "async", "await", "break",
            "class", "continue", "def", "del", "elif", "else", "except", "finally", "for", "from",
            "global", "if", "import", "in", "is", "lambda", "nonlocal", "not", "or", "pass",
            "raise", "return", "try", "while", "with", "yield",
        };
        public static string FindRequiredRepoRoot()
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new DirectoryNotFoundException("Could not find repository root for source validation.");
            return root;
        }

        public static string RepoPath(string relativePath)
        {
            var root = FindRequiredRepoRoot();
            var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
                throw new FileNotFoundException("Missing repository file: " + relativePath, path);
            return path;
        }

        public static string ReadRequiredRepoText(string relativePath)
        {
            var normalized = relativePath.Replace('\\', '/');
            if (string.Equals(
                    normalized,
                    "Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Replay/McapReplayEngine.cs",
                    StringComparison.Ordinal))
                return ReadMcapReplayEngineSources();
            if (string.Equals(
                    normalized,
                    "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/MediaFoundationH264EncoderSidecar.cs",
                    StringComparison.Ordinal))
                return ReadMediaFoundationH264EncoderSidecarSources();

            if (string.Equals(
                    normalized,
                    "Packages/dev.unity2foxglove.sdk/Runtime/Core/Runtime/FoxgloveRuntime.cs",
                    StringComparison.Ordinal))
                return ReadFoxgloveRuntimeSources();

            return File.ReadAllText(RepoPath(relativePath));
        }

        public static string ReadMcapReplayEngineSources()
        {
            var root = FindRequiredRepoRoot();
            var facadePath = Path.Combine(
                root,
                "Packages",
                "dev.unity2foxglove.sdk",
                "Runtime",
                "IO",
                "Mcap",
                "Replay",
                "McapReplayEngine.cs");
            var fragmentPaths = new[]
            {
                Path.Combine(root, "Packages", "dev.unity2foxglove.sdk", "Runtime", "IO", "Mcap", "Replay", "Decomposed", "McapReplayEngine", "Load.cs"),
                Path.Combine(root, "Packages", "dev.unity2foxglove.sdk", "Runtime", "IO", "Mcap", "Replay", "Decomposed", "McapReplayEngine", "History.cs"),
                Path.Combine(root, "Packages", "dev.unity2foxglove.sdk", "Runtime", "IO", "Mcap", "Replay", "Decomposed", "McapReplayEngine", "TryReadIndexedBoundedHistory.cs"),
                Path.Combine(root, "Packages", "dev.unity2foxglove.sdk", "Runtime", "IO", "Mcap", "Replay", "Decomposed", "McapReplayEngine", "HistoryCandidate.cs"),
                Path.Combine(root, "Packages", "dev.unity2foxglove.sdk", "Runtime", "IO", "Mcap", "Replay", "Decomposed", "McapReplayEngine", "SortPending.cs"),
                Path.Combine(root, "Packages", "dev.unity2foxglove.sdk", "Runtime", "IO", "Mcap", "Replay", "Decomposed", "McapReplayEngine", "FinishTickResult.cs")
            };

            if (!File.Exists(facadePath))
                throw new FileNotFoundException("Missing MCAP replay source: " + facadePath, facadePath);

            foreach (var path in fragmentPaths)
                if (!File.Exists(path))
                    throw new FileNotFoundException("Missing MCAP replay source: " + path, path);

            var chunks = new List<string>
            {
                ReadMcapReplayFacadeBody(facadePath),
            };
            chunks.AddRange(fragmentPaths.Select(ReadMcapReplayFragmentBody));
            return string.Join(Environment.NewLine + Environment.NewLine, chunks)
                + Environment.NewLine
                + "    }"
                + Environment.NewLine
                + "}";
        }

        private static string ReadMcapReplayFacadeBody(string path)
        {
            var lines = File.ReadAllLines(path);
            if (lines.Length < 3)
                throw new InvalidDataException("MCAP replay facade is too short: " + path);
            return string.Join(Environment.NewLine, lines.Take(lines.Length - 2));
        }

        private static string ReadMcapReplayFragmentBody(string path)
        {
            var lines = File.ReadAllLines(path);
            var classLine = Array.FindIndex(
                lines,
                line => line.Contains("partial class McapReplayEngine", StringComparison.Ordinal));
            if (classLine < 0)
                throw new InvalidDataException("MCAP replay fragment class is missing: " + path);
            var openBrace = Array.FindIndex(
                lines,
                classLine,
                line => line.Trim() == "{");
            if (openBrace < 0 || lines.Length - openBrace < 4)
                throw new InvalidDataException("MCAP replay fragment is too short: " + path);
            return string.Join(Environment.NewLine, lines.Skip(openBrace + 1).Take(lines.Length - openBrace - 3));
        }

        public static string ReadSplitPythonSource(string relativePath)
            => ReadSplitPythonSourcePath(RepoPath(relativePath));

        internal static string ReadSplitPythonSourcePath(string facadePath)
        {
            var packagePath = Path.Combine(
                Path.GetDirectoryName(facadePath)
                    ?? throw new DirectoryNotFoundException(facadePath),
                Path.GetFileNameWithoutExtension(facadePath));
            if (!Directory.Exists(packagePath))
                return File.ReadAllText(facadePath);

            var sections = EnumeratePythonSectionPaths(packagePath).ToArray();
            if (sections.Length == 0)
                throw new InvalidOperationException(
                    "No exported Python source sections found in "
                    + Path.Combine(packagePath, "__init__.py"));

            var source = new StringBuilder(File.ReadAllText(facadePath));
            foreach (var section in sections)
            {
                source.Append(Environment.NewLine);
                source.Append(File.ReadAllText(section));
            }

            return source.ToString();
        }

        private static string StripPythonCommentsAndStrings(
            string rawLine,
            ref string quote,
            ref bool escaped)
        {
            var sanitized = new StringBuilder(rawLine.Length);
            for (var index = 0; index < rawLine.Length; index++)
            {
                var character = rawLine[index];
                if (quote != null)
                {
                    if (quote.Length == 3
                        && index + 2 < rawLine.Length
                        && rawLine.Substring(index, 3) == quote
                        && !escaped)
                    {
                        sanitized.Append("   ");
                        index += 2;
                        quote = null;
                        continue;
                    }
                    if (quote.Length == 1 && character == quote[0] && !escaped)
                    {
                        sanitized.Append(' ');
                        quote = null;
                        continue;
                    }
                    sanitized.Append(character == '\r' || character == '\n' ? character : ' ');
                    if (escaped)
                        escaped = false;
                    else if (character == '\\')
                        escaped = true;
                    continue;
                }

                if (character == '#')
                    break;
                if (character == '\'' || character == '"')
                {
                    var triple = index + 2 < rawLine.Length
                        && rawLine[index + 1] == character
                        && rawLine[index + 2] == character;
                    quote = triple ? new string(character, 3) : character.ToString();
                    sanitized.Append(triple ? "   " : " ");
                    if (triple)
                        index += 2;
                    escaped = false;
                    continue;
                }
                sanitized.Append(character);
            }
            return sanitized.ToString();
        }

        private static IEnumerable<string> SplitTopLevelPythonStatements(string statement)
        {
            var start = 0;
            var depth = 0;
            for (var index = 0; index < statement.Length; index++)
            {
                if (statement[index] == '(')
                    depth++;
                else if (statement[index] == ')')
                    depth--;
                else if (statement[index] == ';' && depth == 0)
                {
                    yield return statement.Substring(start, index - start).Trim();
                    start = index + 1;
                }
            }
            if (start < statement.Length)
                yield return statement.Substring(start).Trim();
        }

        private static IEnumerable<string> ParsePythonImportNames(
            string rawNames,
            string initPath,
            bool allowStar)
        {
            var names = rawNames.Trim();
            if (names.StartsWith("(", StringComparison.Ordinal))
            {
                if (!names.EndsWith(")", StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        "Invalid Python source-section import in " + initPath);
                names = names.Substring(1, names.Length - 2).Trim();
            }
            if (names.Length == 0)
                throw new InvalidOperationException(
                    "Invalid Python source-section import in " + initPath);

            var parts = names.Split(',');
            for (var index = 0; index < parts.Length; index++)
            {
                var part = parts[index].Trim();
                if (part.Length == 0)
                {
                    if (index == parts.Length - 1)
                        continue;
                    throw new InvalidOperationException(
                        "Invalid Python source-section import in " + initPath);
                }
                if (part == "*")
                {
                    if (!allowStar)
                        throw new InvalidOperationException(
                            "Invalid Python source-section name in " + initPath);
                    continue;
                }
                var match = Regex.Match(
                    part,
                    @"^(?<name>[A-Za-z_][A-Za-z0-9_]*)(?:\s+as\s+(?<alias>[A-Za-z_][A-Za-z0-9_]*))?$",
                    RegexOptions.CultureInvariant);
                if (!match.Success)
                    throw new InvalidOperationException(
                        "Invalid Python source-section name in " + initPath);
                var name = match.Groups["name"].Value;
                if (PythonSectionKeywords.Contains(name))
                    throw new InvalidOperationException(
                        "Python source-section name is a keyword in " + initPath);
                var alias = match.Groups["alias"].Value;
                if (alias.Length > 0 && PythonSectionKeywords.Contains(alias))
                    throw new InvalidOperationException(
                        "Python source-section alias is a keyword in " + initPath);
                yield return name;
            }
        }

        private static IEnumerable<string> EnumeratePythonSectionPaths(string packagePath)
        {
            var initPath = Path.Combine(packagePath, "__init__.py");
            if (!File.Exists(initPath))
                throw new FileNotFoundException("Missing Python package initializer.", initPath);

            var seen = new HashSet<string>(
                Path.DirectorySeparatorChar == '\\'
                    ? StringComparer.OrdinalIgnoreCase
                    : StringComparer.Ordinal);
            var statement = new StringBuilder();
            var parenthesisDepth = 0;
            var explicitContinuation = false;
            string quote = null;
            var escaped = false;
            foreach (var rawLine in File.ReadLines(initPath))
            {
                var hasIndentation = rawLine.Length != rawLine.TrimStart().Length;
                var line = StripPythonCommentsAndStrings(rawLine, ref quote, ref escaped).Trim();
                if (statement.Length == 0 && hasIndentation)
                    continue;
                if (line.Length == 0)
                {
                    if (statement.Length > 0 && (explicitContinuation || parenthesisDepth > 0))
                        throw new InvalidOperationException(
                            "Interrupted Python source-section import in " + initPath);
                    continue;
                }

                var hasContinuation = line.EndsWith("\\", StringComparison.Ordinal);
                if (hasContinuation)
                    line = line.Substring(0, line.Length - 1).TrimEnd();
                explicitContinuation = hasContinuation;

                if (statement.Length == 0 && !line.StartsWith("from .", StringComparison.Ordinal))
                {
                    explicitContinuation = false;
                    continue;
                }
                if (statement.Length > 0)
                    statement.Append(' ');
                statement.Append(line);
                parenthesisDepth += line.Count(character => character == '(');
                parenthesisDepth -= line.Count(character => character == ')');
                if (parenthesisDepth < 0)
                    throw new InvalidOperationException(
                        "Invalid Python source-section import in " + initPath);
                if (parenthesisDepth > 0 || explicitContinuation)
                    continue;

                var importStatement = statement.ToString();
                statement.Clear();
                parenthesisDepth = 0;
                foreach (var singleStatement in SplitTopLevelPythonStatements(importStatement))
                {
                    var match = Regex.Match(
                        singleStatement,
                        @"^from\s+(?<dots>\.+)(?<module>[A-Za-z_][A-Za-z0-9_]*)?\s+import\s+(?<names>.+?)$",
                        RegexOptions.CultureInvariant);
                    if (!match.Success)
                    {
                        if (singleStatement.StartsWith("from .", StringComparison.Ordinal))
                        {
                            var fromIndex = singleStatement.IndexOf("from ", StringComparison.Ordinal) + 5;
                            var dotCount = 0;
                            while (fromIndex + dotCount < singleStatement.Length
                                   && singleStatement[fromIndex + dotCount] == '.')
                                dotCount++;
                            if (dotCount == 1)
                                throw new InvalidOperationException(
                                    "Invalid Python source-section import in " + initPath);
                        }
                        continue;
                    }

                    var dots = match.Groups["dots"].Value;
                    if (dots.Length != 1)
                        continue;
                    var module = match.Groups["module"].Value;
                    if (module.Length > 0 && PythonSectionKeywords.Contains(module))
                        throw new InvalidOperationException(
                            "Python source-section name is a keyword in " + initPath);
                    var names = ParsePythonImportNames(
                        match.Groups["names"].Value,
                        initPath,
                        allowStar: module.Length > 0);
                    if (module.Length > 0)
                        names = new[] { module };
                    foreach (var name in names)
                    {
                        if (string.Equals(name, "__init__", StringComparison.Ordinal))
                            continue;

                        var packageRoot = Path.GetFullPath(packagePath)
                            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                            + Path.DirectorySeparatorChar;
                        var candidate = Path.GetFullPath(Path.Combine(packagePath, name + ".py"));
                        var comparer = Path.DirectorySeparatorChar == '\\'
                            ? StringComparison.OrdinalIgnoreCase
                            : StringComparison.Ordinal;
                        if (!candidate.StartsWith(packageRoot, comparer))
                            throw new InvalidOperationException(
                                "Python source-section path escaped its package.");
                        if (!File.Exists(candidate))
                            throw new FileNotFoundException(
                                "Declared Python source section is missing.",
                                candidate);
                        if (!seen.Add(candidate))
                            throw new InvalidOperationException(
                                "Duplicate Python source-section import in " + initPath);
                        yield return candidate;
                    }
                }
            }

            if (parenthesisDepth != 0 || explicitContinuation || statement.Length != 0 || quote != null)
                throw new InvalidOperationException(
                    "Unterminated Python source-section import in " + initPath);
        }

        public static string ReadCameraPublisherSources()
        {
            var root = FindRequiredRepoRoot();

            var dir = Path.Combine(
                root,
                "Packages",
                "dev.unity2foxglove.sdk",
                "Runtime",
                "Schemas",
                "Proto",
                "Publishers");
            if (!Directory.Exists(dir))
                throw new DirectoryNotFoundException("Camera publisher directory was not found.");

            var files = Directory.GetFiles(dir, "FoxgloveCameraPublisher*.cs")
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            var source = new StringBuilder();
            foreach (var file in files)
            {
                if (source.Length > 0)
                    source.Append(Environment.NewLine);
                source.Append(File.ReadAllText(file));
            }

            return source.ToString();
        }

        public static string ReadMediaFoundationH264EncoderSidecarSources()
        {
            var root = FindRequiredRepoRoot();

            var dir = Path.Combine(
                root,
                "Packages",
                "dev.unity2foxglove.sdk",
                "Runtime",
                "Schemas",
                "Proto",
                "Video");
            if (!Directory.Exists(dir))
                throw new DirectoryNotFoundException("Media Foundation H.264 sidecar directory was not found.");

            var main = Path.Combine(dir, "MediaFoundationH264EncoderSidecar.cs");
            if (!File.Exists(main))
                throw new FileNotFoundException("Missing Media Foundation H.264 sidecar facade.", main);

            var files = new[] { main }
                .Concat(Directory.GetFiles(dir, "MediaFoundationH264EncoderSidecar.*.cs")
                    .OrderBy(path => Path.GetFileName(path).Contains(
                        ".WorkerLifecycle.",
                        StringComparison.Ordinal) ? 0 : Path.GetFileName(path).Contains(
                        ".NativeEncoding.",
                        StringComparison.Ordinal) ? 1 : Path.GetFileName(path).Contains(
                        ".ComInterop.",
                        StringComparison.Ordinal) ? 2 : 3)
                    .ThenBy(path => path, StringComparer.Ordinal))
                .ToArray();

            var source = new StringBuilder();
            foreach (var file in files)
            {
                if (source.Length > 0)
                    source.Append(Environment.NewLine);
                source.Append(File.ReadAllText(file));
            }

            return source.ToString();
        }

        public static string ReadFoxgloveServiceHubSources()
        {
            var root = FindRequiredRepoRoot();

            var dir = Path.Combine(
                root,
                "Packages",
                "dev.unity2foxglove.sdk",
                "Runtime",
                "Components",
                "FoxService");
            if (!Directory.Exists(dir))
                throw new DirectoryNotFoundException("FoxgloveServiceHub directory was not found.");

            var main = Path.Combine(dir, "FoxgloveServiceHub.cs");
            if (!File.Exists(main))
                throw new FileNotFoundException("Missing FoxgloveServiceHub facade.", main);

            var files = new[] { main }
                .Concat(Directory.GetFiles(dir, "FoxgloveServiceHub.*.cs")
                    .OrderBy(path => path, StringComparer.Ordinal))
                .ToArray();

            var source = new StringBuilder();
            foreach (var file in files)
            {
                if (source.Length > 0)
                    source.Append(Environment.NewLine);
                source.Append(File.ReadAllText(file));
            }

            return source.ToString();
        }

        public static string ReadReplayControllerSources()
        {
            var root = FindRequiredRepoRoot();

            var dir = Path.Combine(
                root,
                "Packages",
                "dev.unity2foxglove.sdk",
                "Runtime",
                "Core",
                "Replay");
            if (!Directory.Exists(dir))
                throw new DirectoryNotFoundException("Replay controller directory was not found.");

            var files = Directory.GetFiles(dir, "ReplayController*.cs")
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            var source = new StringBuilder();
            foreach (var file in files)
            {
                if (source.Length > 0)
                    source.Append(Environment.NewLine);
                source.Append(File.ReadAllText(file));
            }

            return source.ToString();
        }

        public static string ReadFoxgloveRuntimeSources()
        {
            var root = FindRequiredRepoRoot();
            var dir = Path.Combine(
                root,
                "Packages",
                "dev.unity2foxglove.sdk",
                "Runtime",
                "Core",
                "Runtime");
            if (!Directory.Exists(dir))
                throw new DirectoryNotFoundException("FoxgloveRuntime directory was not found.");

            var fileNames = FoxgloveRuntimeSourceFileNames;
            var expected = new HashSet<string>(fileNames, StringComparer.Ordinal);
            var actual = Directory.GetFiles(dir, "FoxgloveRuntime*.cs", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .ToHashSet(StringComparer.Ordinal);
            if (!expected.SetEquals(actual))
            {
                var missing = expected.Except(actual).OrderBy(name => name, StringComparer.Ordinal);
                var unexpected = actual.Except(expected).OrderBy(name => name, StringComparer.Ordinal);
                throw new InvalidDataException(
                    "FoxgloveRuntime source composition is incomplete or contains unregistered partial files. " +
                    "Missing: " + string.Join(", ", missing) + "; unexpected: " + string.Join(", ", unexpected));
            }

            var facade = File.ReadAllText(Path.Combine(dir, fileNames[0]));
            var trailingGateStart = facade.LastIndexOf(
                FoxgloveRuntimeTrailingGateMarker,
                StringComparison.Ordinal);
            if (trailingGateStart < 0)
                throw new InvalidDataException("FoxgloveRuntime facade is missing ClientEventGenerationGate.");

            var source = new StringBuilder();
            source.Append(facade.Substring(0, trailingGateStart).TrimEnd());
            for (var i = 1; i < fileNames.Length; i++)
            {
                var path = Path.Combine(dir, fileNames[i]);
                if (source.Length > 0)
                    source.Append(Environment.NewLine);
                source.Append(File.ReadAllText(path));
            }
            source.Append(Environment.NewLine);
            source.Append(facade.Substring(trailingGateStart).TrimStart());

            return source.ToString();
        }

        public static string ReadMcapRecorderSources()
        {
            var root = FindRequiredRepoRoot();

            var dir = Path.Combine(
                root,
                "Packages",
                "dev.unity2foxglove.sdk",
                "Runtime",
                "IO",
                "Mcap",
                "Recording");
            if (!Directory.Exists(dir))
                throw new DirectoryNotFoundException("MCAP recorder directory was not found.");

            var files = Directory.GetFiles(dir, "McapRecorder*.cs")
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            var source = new StringBuilder();
            foreach (var file in files)
            {
                if (source.Length > 0)
                    source.Append(Environment.NewLine);
                source.Append(File.ReadAllText(file));
            }

            return source.ToString();
        }

        public static string ReadMcapDataLoaderSources()
        {
            var root = FindRequiredRepoRoot();

            var dir = Path.Combine(
                root,
                "Packages",
                "dev.unity2foxglove.sdk",
                "Runtime",
                "IO",
                "Mcap",
                "DataLoader");
            if (!Directory.Exists(dir))
                throw new DirectoryNotFoundException("MCAP DataLoader directory was not found.");

            var main = Path.Combine(dir, "McapDataLoader.cs");
            if (!File.Exists(main))
                throw new FileNotFoundException("Missing MCAP DataLoader facade.", main);

            var files = new[] { main }
                .Concat(Directory.GetFiles(dir, "McapDataLoader.*.cs")
                    .OrderBy(path => path, StringComparer.Ordinal))
                .ToArray();

            var source = new StringBuilder();
            foreach (var file in files)
            {
                if (source.Length > 0)
                    source.Append(Environment.NewLine);
                source.Append(File.ReadAllText(file));
            }

            return source.ToString();
        }

        public static string ReadRemoteMcapHttpRouterSources()
        {
            var root = FindRequiredRepoRoot();

            var dir = Path.Combine(
                root,
                "Packages",
                "dev.unity2foxglove.sdk",
                "Runtime",
                "IO",
                "Mcap",
                "Remote");
            if (!Directory.Exists(dir))
                throw new DirectoryNotFoundException("Remote MCAP router directory was not found.");

            var main = Path.Combine(dir, "RemoteMcapHttpRouter.cs");
            if (!File.Exists(main))
                throw new FileNotFoundException("Missing Remote MCAP router facade.", main);

            var files = new[] { main }
                .Concat(Directory.GetFiles(dir, "RemoteMcapHttpRouter.*.cs")
                    .OrderBy(path => path, StringComparer.Ordinal))
                .ToArray();

            var source = new StringBuilder();
            foreach (var file in files)
            {
                if (source.Length > 0)
                    source.Append(Environment.NewLine);
                source.Append(File.ReadAllText(file));
            }

            return source.ToString();
        }

        public static string ReadFoxgloveLogSourceGeneratorSources()
        {
            var root = FindRequiredRepoRoot();

            var dir = Path.Combine(
                root,
                "Packages",
                "dev.unity2foxglove.sdk",
                "Editor",
                "SourceGenerators",
                "src");
            if (!Directory.Exists(dir))
                throw new DirectoryNotFoundException("Source generator src directory was not found.");

            var files = Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            var source = new StringBuilder();
            foreach (var file in files)
            {
                if (source.Length > 0)
                    source.Append(Environment.NewLine);
                source.Append(File.ReadAllText(file));
            }

            return source.ToString();
        }

        public static string ReadFoxgloveManagerEditorSources()
        {
            var root = FindRequiredRepoRoot();

            var dir = Path.Combine(
                root,
                "Packages",
                "dev.unity2foxglove.sdk",
                "Editor",
                "Manager");
            if (!Directory.Exists(dir))
                throw new DirectoryNotFoundException("FoxgloveManagerEditor directory was not found.");

            var files = Directory.GetFiles(dir, "FoxgloveManagerEditor*.cs")
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            var source = new StringBuilder();
            foreach (var file in files)
            {
                if (source.Length > 0)
                    source.Append(Environment.NewLine);
                source.Append(File.ReadAllText(file));
            }

            return source.ToString();
        }

        public static string ReadFoxgloveManagerPublishingSources()
        {
            var root = FindRequiredRepoRoot();

            var dir = Path.Combine(
                root,
                "Packages",
                "dev.unity2foxglove.sdk",
                "Runtime",
                "Components",
                "Manager");
            if (!Directory.Exists(dir))
                throw new DirectoryNotFoundException("FoxgloveManager publishing directory was not found.");

            var files = Directory.GetFiles(dir, "FoxgloveManager.Publishing*.cs")
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            var source = new StringBuilder();
            foreach (var file in files)
            {
                if (source.Length > 0)
                    source.Append(Environment.NewLine);
                source.Append(File.ReadAllText(file));
            }

            return source.ToString();
        }

        public static string ReadFoxgloveManagerServerSources()
        {
            var root = FindRequiredRepoRoot();

            var dir = Path.Combine(
                root,
                "Packages",
                "dev.unity2foxglove.sdk",
                "Runtime",
                "Components",
                "Manager");
            if (!Directory.Exists(dir))
                throw new DirectoryNotFoundException("FoxgloveManager server directory was not found.");

            var files = Directory.GetFiles(dir, "FoxgloveManager.Server*.cs")
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            var source = new StringBuilder();
            foreach (var file in files)
            {
                if (source.Length > 0)
                    source.Append(Environment.NewLine);
                source.Append(File.ReadAllText(file));
            }

            return source.ToString();
        }

        public static bool SourceMethodContains(string source, string methodName, string needle)
            => RequiredSourceMethod(source, methodName).Contains(needle, StringComparison.Ordinal);

        public static int InvocationCountInMethod(
            string source,
            string methodName,
            string invocationName)
            => QualifiedInvocationCountInMethod(
                source,
                methodName,
                receiverName: null,
                invocationName);

        public static int QualifiedInvocationCountInMethod(
            string source,
            string methodName,
            string receiverName,
            string invocationName)
        {
            var methods = CSharpSyntaxTree.ParseText(source)
                .GetRoot()
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Where(method =>
                    string.Equals(
                        method.Identifier.ValueText,
                        methodName,
                        StringComparison.Ordinal))
                .ToArray();
            if (methods.Length != 1)
                return -1;

            return methods[0]
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                // Calls inside local-function declarations belong to that
                // nested owner, not the containing method being inspected.
                .Where(invocation => !invocation.Ancestors()
                    .Any(ancestor => ancestor is LocalFunctionStatementSyntax))
                .Count(invocation =>
                    InvocationMatches(
                        invocation,
                        receiverName,
                        invocationName));
        }

        public static int InvocationCount(
            string source,
            string invocationName)
            => QualifiedInvocationCount(
                source,
                receiverName: null,
                invocationName);

        public static int QualifiedInvocationCount(
            string source,
            string receiverName,
            string invocationName)
            => CSharpSyntaxTree.ParseText(source)
                .GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Count(invocation =>
                    InvocationMatches(
                        invocation,
                        receiverName,
                        invocationName));

        public static bool TypeHasAttribute(
            string source,
            string typeName,
            string attributeName)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (string.IsNullOrWhiteSpace(typeName))
                throw new ArgumentException(
                    "Type name cannot be empty.",
                    nameof(typeName));
            if (string.IsNullOrWhiteSpace(attributeName))
                throw new ArgumentException(
                    "Attribute name cannot be empty.",
                    nameof(attributeName));

            var shortName = attributeName.EndsWith(
                "Attribute",
                StringComparison.Ordinal)
                ? attributeName.Substring(
                    0,
                    attributeName.Length - "Attribute".Length)
                : attributeName;
            var fullName = shortName + "Attribute";
            var matches = CSharpSyntaxTree.ParseText(source)
                .GetRoot()
                .DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .Where(type =>
                    string.Equals(
                        type.Identifier.ValueText,
                        typeName,
                        StringComparison.Ordinal))
                .ToArray();
            if (matches.Length != 1)
                return false;

            return matches[0].AttributeLists
                .SelectMany(list => list.Attributes)
                .Any(attribute =>
                {
                    var identifier = attribute.Name
                        .DescendantNodesAndSelf()
                        .OfType<IdentifierNameSyntax>()
                        .LastOrDefault()
                        ?.Identifier.ValueText;
                    return string.Equals(
                               identifier,
                               shortName,
                               StringComparison.Ordinal)
                           || string.Equals(
                               identifier,
                               fullName,
                               StringComparison.Ordinal);
                });
        }

        public static string TrySourceMethod(string source, string methodName)
            => SourceDeclaration(
                source,
                methodName,
                IsSourceMethodDeclaration,
                CSharpParseOptions.Default);

        public static string SourceMethod(string source, string methodName)
        {
            var method = TrySourceMethod(source, methodName);
            if (string.IsNullOrEmpty(method))
            {
                throw new InvalidOperationException(
                    "Could not resolve exactly one source method for: " + methodName);
            }

            return method;
        }

        /// <summary>
        /// Resolves one method by identifier and a stable body marker.  This is
        /// useful for overloads whose tuple-heavy signatures are legitimately
        /// reformatted by different C# writers; the marker still anchors the
        /// intended implementation body and the Roslyn span keeps extraction
        /// syntax-aware. Resolution is fail-closed: callers receive an
        /// exception unless exactly one declaration matches.
        /// </summary>
        public static string SourceMethodContaining(
            string source,
            string methodName,
            string bodyMarker)
        {
            if (string.IsNullOrWhiteSpace(source)
                || string.IsNullOrWhiteSpace(methodName)
                || string.IsNullOrWhiteSpace(bodyMarker))
            {
                throw new ArgumentException(
                    "Source, method name, and body marker are required.");
            }

            var matches = CSharpSyntaxTree.ParseText(source)
                .GetRoot()
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Where(method => string.Equals(
                    method.Identifier.ValueText,
                    methodName,
                    StringComparison.Ordinal))
                // Keep a method span even when a formatter leaves a recoverable
                // diagnostic in a tuple header; the body marker is the gate.
                .Select(method => source.Substring(method.SpanStart, method.Span.Length))
                .Where(method => method.Contains(bodyMarker, StringComparison.Ordinal))
                .ToArray();

            if (matches.Length != 1)
            {
                throw new InvalidOperationException(
                    "Could not resolve exactly one source method containing '"
                    + bodyMarker
                    + "' for: "
                    + methodName
                    + "; matches="
                    + matches.Length);
            }

            return matches[0];
        }

        public static string RequiredSourceMethod(string source, string methodName)
            => SourceMethod(source, methodName);

        public static string SourceMethodWithPreprocessorSymbols(
            string source,
            string methodName,
            params string[] preprocessorSymbols)
        {
            if (preprocessorSymbols == null || preprocessorSymbols.Length == 0)
                return string.Empty;

            return SourceDeclaration(
                source,
                methodName,
                IsSourceMethodDeclaration,
                CSharpParseOptions.Default.WithPreprocessorSymbols(preprocessorSymbols));
        }

        public static string SourceType(string source, string typeName)
            => SourceDeclaration(
                source,
                typeName,
                node => node is TypeDeclarationSyntax,
                CSharpParseOptions.Default);

        public static string SourceProperty(string source, string propertyName)
            => SourceDeclaration(
                source,
                propertyName,
                node => node is PropertyDeclarationSyntax,
                CSharpParseOptions.Default);

        private static string SourceDeclaration(
            string source,
            string requestedDeclaration,
            Func<SyntaxNode, bool> declarationFilter,
            CSharpParseOptions parseOptions)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrWhiteSpace(requestedDeclaration))
                return string.Empty;

            var matches = CSharpSyntaxTree.ParseText(source, parseOptions)
                .GetRoot()
                .DescendantNodes()
                .Where(declarationFilter)
                .Where(declaration => !declaration.ContainsDiagnostics)
                .Where(declaration => SourceDeclarationMatches(source, declaration, requestedDeclaration))
                .ToArray();
            if (matches.Length != 1)
                return string.Empty;

            var match = matches[0];
            return source.Substring(match.SpanStart, match.Span.Length);
        }

        private static bool IsSourceMethodDeclaration(SyntaxNode node)
            => node is MethodDeclarationSyntax
               || node is ConstructorDeclarationSyntax
               || node is LocalFunctionStatementSyntax;

        private static bool SourceDeclarationMatches(
            string source,
            SyntaxNode declaration,
            string requestedDeclaration)
        {
            var identifier = declaration switch
            {
                MethodDeclarationSyntax method => method.Identifier.ValueText,
                ConstructorDeclarationSyntax constructor => constructor.Identifier.ValueText,
                LocalFunctionStatementSyntax localFunction => localFunction.Identifier.ValueText,
                TypeDeclarationSyntax type => type.Identifier.ValueText,
                PropertyDeclarationSyntax property => property.Identifier.ValueText,
                _ => string.Empty
            };

            if (SyntaxFacts.IsValidIdentifier(requestedDeclaration))
                return string.Equals(identifier, requestedDeclaration, StringComparison.Ordinal);
            if (!ContainsIdentifierToken(requestedDeclaration, identifier))
                return false;

            var headerEnd = declaration switch
            {
                MethodDeclarationSyntax method => SourceMethodHeaderEnd(
                    method.Body?.OpenBraceToken.SpanStart,
                    method.ExpressionBody?.ArrowToken.SpanStart,
                    method.SemicolonToken.SpanStart),
                ConstructorDeclarationSyntax constructor => SourceMethodHeaderEnd(
                    constructor.Body?.OpenBraceToken.SpanStart,
                    constructor.ExpressionBody?.ArrowToken.SpanStart,
                    constructor.SemicolonToken.SpanStart),
                LocalFunctionStatementSyntax localFunction => SourceMethodHeaderEnd(
                    localFunction.Body?.OpenBraceToken.SpanStart,
                    localFunction.ExpressionBody?.ArrowToken.SpanStart,
                    localFunction.SemicolonToken.SpanStart),
                TypeDeclarationSyntax type => type.OpenBraceToken.SpanStart,
                PropertyDeclarationSyntax property => SourceMethodHeaderEnd(
                    property.AccessorList?.OpenBraceToken.SpanStart,
                    property.ExpressionBody?.ArrowToken.SpanStart,
                    property.SemicolonToken.SpanStart),
                _ => -1
            };
            if (headerEnd < declaration.SpanStart)
                return false;

            var header = source.Substring(declaration.SpanStart, headerEnd - declaration.SpanStart);
            return CollapseSourceWhitespace(header)
                .Contains(CollapseSourceWhitespace(requestedDeclaration), StringComparison.Ordinal);
        }

        private static int SourceMethodHeaderEnd(int? bodyStart, int? expressionBodyStart, int semicolonStart)
            => bodyStart ?? expressionBodyStart ?? semicolonStart;

        private static string CollapseSourceWhitespace(string value)
        {
            var result = new StringBuilder(value.Length);
            var pendingSpace = false;
            foreach (var current in value)
            {
                if (char.IsWhiteSpace(current))
                {
                    pendingSpace = result.Length > 0;
                    continue;
                }

                if (pendingSpace)
                    result.Append(' ');
                result.Append(current);
                pendingSpace = false;
            }

            return result.ToString();
        }

        private static bool ContainsIdentifierToken(string value, string identifier)
        {
            var offset = 0;
            while (offset < value.Length)
            {
                var index = value.IndexOf(identifier, offset, StringComparison.Ordinal);
                if (index < 0)
                    return false;

                var beforeIsIdentifier = index > 0
                                         && SyntaxFacts.IsIdentifierPartCharacter(value[index - 1]);
                var afterIndex = index + identifier.Length;
                var afterIsIdentifier = afterIndex < value.Length
                                        && SyntaxFacts.IsIdentifierPartCharacter(value[afterIndex]);
                if (!beforeIsIdentifier && !afterIsIdentifier)
                    return true;

                offset = index + identifier.Length;
            }

            return false;
        }

        private static bool InvocationMatches(
            InvocationExpressionSyntax invocation,
            string receiverName,
            string invocationName)
        {
            if (invocation?.Expression
                is IdentifierNameSyntax identifier)
            {
                return receiverName == null
                       && identifier.Identifier.ValueText
                       == invocationName;
            }

            if (invocation?.Expression
                is not MemberAccessExpressionSyntax memberAccess)
            {
                return false;
            }

            return memberAccess.Name.Identifier.ValueText
                   == invocationName
                   && (receiverName == null
                       || string.Equals(
                           memberAccess.Expression.ToString(),
                           receiverName,
                           StringComparison.Ordinal));
        }

    }
}
