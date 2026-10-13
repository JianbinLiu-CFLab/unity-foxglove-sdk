// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Shared source composition for the ROS2 For Unity subscription hub.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Ros2ForUnitySubscriptionHubSourceComposer
    {
        private static readonly string[] SourceFileNames =
        {
            "FoxRunRos2SubscriptionHub.cs",
            "Decomposed/FoxRunRos2SubscriptionHub/FoxRunRos2SubscriptionHub.Scanning.cs",
            "Decomposed/FoxRunRos2SubscriptionHub/FoxRunRos2SubscriptionHub.Lifecycle.cs",
            "Decomposed/FoxRunRos2SubscriptionHub/FoxRunRos2SubscriptionHub.Bindings.cs",
        };

        public static string Compose(string repositoryRoot)
        {
            var nativeRoot = Path.Combine(
                repositoryRoot,
                "Packages",
                "dev.unity2foxglove.ros2forunity",
                "Runtime",
                "Native",
                "FoxRun");
            var paths = SourceFileNames
                .Select(relativePath => Path.Combine(
                    nativeRoot,
                    relativePath.Replace('/', Path.DirectorySeparatorChar)))
                .ToArray();
            foreach (var path in paths)
                if (!File.Exists(path))
                    throw new FileNotFoundException("Missing ROS2 for Unity subscription hub source: " + path, path);

            var fragmentDirectory = Path.Combine(
                nativeRoot,
                "Decomposed",
                "FoxRunRos2SubscriptionHub");
            if (!Directory.Exists(fragmentDirectory))
                throw new DirectoryNotFoundException("ROS2 for Unity subscription hub decomposed directory was not found.");

            var expectedFragments = SourceFileNames
                .Skip(1)
                .Select(Path.GetFileName)
                .ToHashSet(StringComparer.Ordinal);
            var actualFragments = Directory.GetFiles(fragmentDirectory, "*.cs", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(fragmentDirectory, path).Replace('\\', '/'))
                .ToHashSet(StringComparer.Ordinal);
            if (!expectedFragments.SetEquals(actualFragments))
                throw new InvalidDataException(
                    "ROS2 for Unity subscription hub source set mismatch. Expected: "
                    + string.Join(", ", expectedFragments.OrderBy(name => name, StringComparer.Ordinal))
                    + "; actual: "
                    + string.Join(", ", actualFragments.OrderBy(name => name, StringComparer.Ordinal)));

            var sources = new List<string> { TrimFacade(paths[0]) };
            sources.AddRange(paths.Skip(1).Select(TrimFragment));
            return string.Join(Environment.NewLine + Environment.NewLine, sources)
                + Environment.NewLine
                + "    }"
                + Environment.NewLine
                + "}"
                + Environment.NewLine
                + "#endif";
        }

        private static string TrimFacade(string path)
        {
            var lines = File.ReadAllLines(path);
            var end = TrimWrapperEnd(lines, 0, path);
            return string.Join(Environment.NewLine, lines.Take(end));
        }

        private static string TrimFragment(string path)
        {
            var lines = File.ReadAllLines(path);
            var classLine = Array.FindIndex(
                lines,
                line => line.Contains("partial class FoxRunRos2SubscriptionHub", StringComparison.Ordinal));
            if (classLine < 0)
                throw new InvalidDataException("ROS2 for Unity subscription hub fragment class is missing: " + path);
            var openBrace = Array.FindIndex(lines, classLine, line => line.Trim() == "{");
            if (openBrace < 0)
                throw new InvalidDataException("ROS2 for Unity subscription hub fragment class body is missing: " + path);
            var end = TrimWrapperEnd(lines, openBrace + 1, path);
            return string.Join(Environment.NewLine, lines.Skip(openBrace + 1).Take(end - openBrace - 1));
        }

        private static int TrimWrapperEnd(string[] lines, int minimum, string path)
        {
            var end = TrimFooter(lines);
            for (var brace = 0; brace < 2; brace++)
            {
                while (end > minimum && string.IsNullOrWhiteSpace(lines[end - 1]))
                    end--;
                if (end <= minimum || lines[end - 1].Trim() != "}")
                    throw new InvalidDataException("ROS2 for Unity subscription hub wrapper is malformed: " + path);
                end--;
            }

            while (end > minimum && string.IsNullOrWhiteSpace(lines[end - 1]))
                end--;
            if (end <= minimum)
                throw new InvalidDataException("ROS2 for Unity subscription hub body is empty: " + path);
            return end;
        }

        private static int TrimFooter(string[] lines)
        {
            var end = lines.Length;
            while (end > 0 && string.IsNullOrWhiteSpace(lines[end - 1]))
                end--;
            if (end > 0 && lines[end - 1].Trim() == "#endif")
                end--;
            while (end > 0 && string.IsNullOrWhiteSpace(lines[end - 1]))
                end--;
            return end;
        }
    }
}
