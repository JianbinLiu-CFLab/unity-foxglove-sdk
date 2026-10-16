// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Shared Python source inspection helpers for runtime validation phases.

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
    }
}
