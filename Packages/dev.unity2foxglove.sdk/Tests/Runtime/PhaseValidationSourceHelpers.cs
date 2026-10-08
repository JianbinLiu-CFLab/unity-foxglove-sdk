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
    internal static partial class PhaseValidationSourceHelpers
    {
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
            => File.ReadAllText(RepoPath(relativePath));

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
}
}
