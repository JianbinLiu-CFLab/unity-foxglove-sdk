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
        public static string FindRequiredRepoRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Unity2Foxglove.sln"))
                    || Directory.Exists(Path.Combine(directory.FullName, ".git"))
                    || File.Exists(Path.Combine(directory.FullName, ".git")))
                    return directory.FullName;
                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException(
                "Could not find repository root for source validation from " + AppContext.BaseDirectory);
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
            if (IsPythonFacadePath(normalized))
                return ReadSplitPythonSource(normalized);

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
            if (string.Equals(
                    normalized,
                    "Packages/dev.unity2foxglove.sdk/Runtime/Core/Session/FoxgloveSession.cs",
                    StringComparison.Ordinal))
                return ReadFoxgloveSessionSources();
            if (string.Equals(
                    normalized,
                    "Packages/dev.unity2foxglove.sdk/Runtime/Transport/WebSocket/ManagedWsBackend.cs",
                    StringComparison.Ordinal))
                return ReadManagedWsBackendSources();
            if (string.Equals(
                    normalized,
                    "Packages/dev.unity2foxglove.ros2bridge/Runtime/Ros2Bridge/Ros2BridgeOutboundScheduler.cs",
                    StringComparison.Ordinal))
                return ReadRos2BridgeOutboundSchedulerSources();
            if (string.Equals(
                    normalized,
                    "Packages/dev.unity2foxglove.ros2bridge/Runtime/Ros2Bridge/Ros2BridgeRuntime.cs",
                    StringComparison.Ordinal))
                return ReadRos2BridgeRuntimeSources();
            if (string.Equals(
                    normalized,
                    "Packages/dev.unity2foxglove.ros2bridge/Editor/FoxRun/Ros2CustomCdrEmitter.cs",
                    StringComparison.Ordinal))
                return ReadRos2CustomCdrEmitterSources();
            if (string.Equals(
                    normalized,
                    "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/FoxRun/FoxRunRos2SubscriptionHub.cs",
                    StringComparison.Ordinal))
                return ReadRos2ForUnitySubscriptionHubSources();
            if (string.Equals(
                    normalized,
                    "Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityCustomTypesupportSelectionTransaction.cs",
                    StringComparison.Ordinal))
                return ReadRos2ForUnityCustomTypesupportSelectionTransactionSources();
            if (string.Equals(
                    normalized,
                    "Packages/dev.unity2foxglove.sdk/Editor/Publishers/FoxgloveCameraPublisherEditor.cs",
                    StringComparison.Ordinal))
                return ReadCameraPublisherEditorSources();
            if (string.Equals(
                    normalized,
                    "Tools/foxglove-extensions/unity-cursor-bridge/src/index.ts",
                    StringComparison.Ordinal))
                return ReadCursorBridgeSources();

            return ReadGenericCSharpSource(relativePath);
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
            return Directory.Exists(packagePath);
        }
        public static string ReadFoxgloveSessionSources()
        {
            var root = FindRequiredRepoRoot();
            var sessionRoot = Path.Combine(
                root,
                "Packages",
                "dev.unity2foxglove.sdk",
                "Runtime",
                "Core",
                "Session");
            var paths = FoxgloveSessionSourceFileNames
                .Select(relativePath => Path.Combine(
                    sessionRoot,
                    relativePath.Replace('/', Path.DirectorySeparatorChar)))
                .ToArray();

            foreach (var path in paths)
                if (!File.Exists(path))
                    throw new FileNotFoundException("Missing Foxglove session source: " + path, path);

            var fragmentDirectory = Path.Combine(
                sessionRoot,
                "Decomposed",
                "FoxgloveSession");
            var expectedFragments = new HashSet<string>(
                paths.Skip(1).Select(Path.GetFileName),
                StringComparer.Ordinal);
            var actualFragments = new HashSet<string>(
                Directory.GetFiles(fragmentDirectory, "*.cs", SearchOption.TopDirectoryOnly)
                    .Select(Path.GetFileName),
                StringComparer.Ordinal);
            if (!expectedFragments.SetEquals(actualFragments))
                throw new InvalidDataException(
                    "Foxglove session source set mismatch. Expected: "
                    + string.Join(", ", expectedFragments.OrderBy(name => name, StringComparer.Ordinal))
                    + "; actual: "
                    + string.Join(", ", actualFragments.OrderBy(name => name, StringComparer.Ordinal)));

            var chunks = new List<string>
            {
                ReadFoxgloveSessionFacadeBody(paths[0]),
            };
            chunks.AddRange(paths.Skip(1).Select(ReadFoxgloveSessionFragmentBody));
            return string.Join(Environment.NewLine + Environment.NewLine, chunks)
                + Environment.NewLine
                + "    }"
                + Environment.NewLine
                + "}";
        }
        private static string ReadFoxgloveSessionFacadeBody(string path)
        {
            var lines = File.ReadAllLines(path);
            if (lines.Length < 3)
                throw new InvalidDataException("Foxglove session facade is too short: " + path);
            return string.Join(Environment.NewLine, lines.Take(lines.Length - 2));
        }
        private static string ReadFoxgloveSessionFragmentBody(string path)
        {
            var lines = File.ReadAllLines(path);
            var classLine = Array.FindIndex(
                lines,
                line => line.Contains("partial class FoxgloveSession", StringComparison.Ordinal));
            if (classLine < 0)
                throw new InvalidDataException("Foxglove session fragment class is missing: " + path);
            var openBrace = Array.FindIndex(
                lines,
                classLine,
                line => line.Trim() == "{");
            if (openBrace < 0 || lines.Length - openBrace < 4)
                throw new InvalidDataException("Foxglove session fragment is too short: " + path);
            return string.Join(Environment.NewLine, lines.Skip(openBrace + 1).Take(lines.Length - openBrace - 3));
        }
        public static string ReadRos2BridgeOutboundSchedulerSources()
        {
            var root = FindRequiredRepoRoot();
            var paths = new[]
            {
                Path.Combine(root, "Packages", "dev.unity2foxglove.ros2bridge", "Runtime", "Ros2Bridge", "Ros2BridgeOutboundScheduler.cs"),
                Path.Combine(root, "Packages", "dev.unity2foxglove.ros2bridge", "Runtime", "Ros2Bridge", "Decomposed", "Ros2BridgeOutboundScheduler", "StateAndConstruction.cs"),
                Path.Combine(root, "Packages", "dev.unity2foxglove.ros2bridge", "Runtime", "Ros2Bridge", "Decomposed", "Ros2BridgeOutboundScheduler", "AdmissionAndWrites.cs"),
                Path.Combine(root, "Packages", "dev.unity2foxglove.ros2bridge", "Runtime", "Ros2Bridge", "Decomposed", "Ros2BridgeOutboundScheduler", "LifecycleAndAccounting.cs"),
            };

            foreach (var path in paths)
                if (!File.Exists(path))
                    throw new FileNotFoundException("Missing ROS2 Bridge outbound scheduler source: " + path, path);

            return string.Join(Environment.NewLine, paths.Select(File.ReadAllText));
        }
        public static string ReadRos2ForUnitySubscriptionHubSources()
            => Ros2ForUnitySubscriptionHubSourceComposer.Compose(FindRequiredRepoRoot());
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
    }
}
