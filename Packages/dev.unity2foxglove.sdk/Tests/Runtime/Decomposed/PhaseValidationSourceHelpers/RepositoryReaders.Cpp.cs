// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: C++ and generic repository source readers.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
namespace Unity.FoxgloveSDK.Tests
{
    internal static partial class PhaseValidationSourceHelpers
    {
        public static string ReadIncludedCppSource(string relativePath)
            => ReadIncludedCppSource(FindRequiredRepoRoot(), relativePath);
        public static string ReadIncludedCppSource(string root, string relativePath)
        {
            var facadePath = Path.Combine(
                root,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(facadePath))
                throw new FileNotFoundException("Missing C++ source facade: " + relativePath, facadePath);
            var expectedIncludes = string.Equals(
                    relativePath.Replace('\\', '/'),
                    "Tools/ros2_bridge/unity2foxglove_ros2_bridge/test/test_bridge_smoke.cpp",
                    StringComparison.Ordinal)
                ? new[]
                {
                    "test_bridge_smoke/FixtureAndHelpers.inl",
                    "test_bridge_smoke/ProtocolContractTests.inl",
                    "test_bridge_smoke/LifecycleSetup.inl",
                    "test_bridge_smoke/SessionAndPublisherTests.inl",
                    "test_bridge_smoke/PublisherContractTests.inl",
                    "test_bridge_smoke/TransportIntegrationTests.inl",
                }
                : Array.Empty<string>();
            var includes = File.ReadLines(facadePath)
                .Select(line => line.Trim())
                .Where(line => line.StartsWith("#include \"", StringComparison.Ordinal)
                    && line.EndsWith(".inl\"", StringComparison.Ordinal))
                .Select(line => line.Substring(10, line.Length - 11))
                .ToArray();
            if (expectedIncludes.Length > 0)
            {
                var fragmentDirectory = Path.Combine(
                    Path.GetDirectoryName(facadePath)!,
                    "test_bridge_smoke");
                var actual = Directory.GetFiles(
                        fragmentDirectory,
                        "*.inl",
                        SearchOption.TopDirectoryOnly)
                    .Select(path => Path.GetRelativePath(
                        Path.GetDirectoryName(facadePath)!,
                        path).Replace('\\', '/'))
                    .ToHashSet(StringComparer.Ordinal);
                if (!expectedIncludes.SequenceEqual(includes)
                    || !actual.SetEquals(expectedIncludes))
                {
                    throw new InvalidDataException(
                        "C++ source include manifest mismatch. Missing: "
                        + string.Join(", ", expectedIncludes.Except(actual))
                        + "; unexpected: "
                        + string.Join(", ", actual.Except(expectedIncludes)));
                }
            }
            var source = new StringBuilder();
            foreach (var line in File.ReadAllLines(facadePath))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("#include \"", StringComparison.Ordinal)
                    && trimmed.EndsWith(".inl\"", StringComparison.Ordinal))
                {
                    var include = trimmed.Substring(10, trimmed.Length - 11);
                    var fragmentPath = Path.Combine(
                        Path.GetDirectoryName(facadePath)!,
                        include.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(fragmentPath))
                        throw new FileNotFoundException(
                            "Missing C++ source fragment: " + fragmentPath,
                            fragmentPath);
                    source.Append(File.ReadAllText(fragmentPath).TrimEnd('\r', '\n'));
                    source.AppendLine();
                }
                else
                {
                    source.AppendLine(line);
                }
            }
            return source.ToString();
        }
        private static string ReadGenericCSharpSource(string relativePath)
        {
            var facadePath = RepoPath(relativePath);
            if (!relativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                return File.ReadAllText(facadePath);
            var directory = new DirectoryInfo(
                Path.GetDirectoryName(facadePath)
                    ?? throw new DirectoryNotFoundException(facadePath));
            var stem = Path.GetFileNameWithoutExtension(facadePath);
            string sectionDirectory = null;
            for (var ancestor = directory; ancestor != null; ancestor = ancestor.Parent)
            {
                var relativeDirectory = Path.GetRelativePath(ancestor.FullName, directory.FullName);
                var candidate = Path.Combine(
                    ancestor.FullName,
                    "Decomposed",
                    relativeDirectory,
                    stem);
                if (Directory.Exists(candidate))
                {
                    sectionDirectory = candidate;
                    break;
                }
                if (File.Exists(Path.Combine(ancestor.FullName, ".git"))
                    || Directory.Exists(Path.Combine(ancestor.FullName, ".git")))
                    break;
            }
            if (sectionDirectory == null)
                return File.ReadAllText(facadePath);
            var sectionFiles = Directory.GetFiles(
                sectionDirectory,
                "*.cs",
                SearchOption.AllDirectories);
            var marker = File.ReadLines(facadePath)
                .Select(line => Regex.Match(
                    line,
                    @"^\s*///\s*<remarks>Phase192 source sections:\s*(?<names>[^<]+)</remarks>\s*$",
                    RegexOptions.CultureInvariant))
                .FirstOrDefault(match => match.Success);
            var filesByName = sectionFiles.ToDictionary(
                path => Path.GetRelativePath(sectionDirectory, path).Replace('\\', '/'),
                StringComparer.OrdinalIgnoreCase);
            IEnumerable<string> orderedFiles = sectionFiles.OrderBy(
                path => Path.GetRelativePath(sectionDirectory, path),
                StringComparer.Ordinal);
            if (marker != null)
            {
                var names = marker.Groups["names"].Value
                    .Split(',')
                    .Select(name => name.Trim())
                    .Where(name => name.Length > 0)
                    .ToArray();
                if (names.Length != filesByName.Count
                    || names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length
                    || names.Any(name => !filesByName.ContainsKey(name)))
                    throw new InvalidDataException(
                        "Invalid Phase192 source-section order in " + facadePath);
                orderedFiles = names.Select(name => filesByName[name]);
            }
            var source = new StringBuilder(File.ReadAllText(facadePath));
            foreach (var section in orderedFiles)
            {
                source.Append(Environment.NewLine);
                source.Append(File.ReadAllText(section));
            }
            return source.ToString();
        }
        private static bool IsPythonFacadePath(string normalized)
        {
            if (!normalized.StartsWith("Scripts/", StringComparison.Ordinal)
                || !normalized.EndsWith(".py", StringComparison.Ordinal))
                return false;
            var facadePath = RepoPath(normalized);
            var packagePath = Path.Combine(
                Path.GetDirectoryName(facadePath)
                    ?? throw new DirectoryNotFoundException(facadePath),
                Path.GetFileNameWithoutExtension(facadePath));
            return Directory.Exists(packagePath)
                || Directory.Exists(Path.Combine(
                    Path.GetDirectoryName(facadePath)
                        ?? throw new DirectoryNotFoundException(facadePath),
                    "Decomposed",
                    Path.GetFileNameWithoutExtension(facadePath)));
        }
    }
}
