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
        public static string Runtime(string fileName)
            => Text("Packages/dev.unity2foxglove.sdk/Tests/Runtime/" + fileName);

        public static string Text(string relativePath)
        {
            var normalized = relativePath.Replace('\\', '/');
            if (normalized.StartsWith("Scripts/", StringComparison.Ordinal)
                && normalized.EndsWith(".py", StringComparison.OrdinalIgnoreCase))
                return Unity.FoxgloveSDK.Tests.PhaseValidationSourceHelpers.ReadSplitPythonSource(normalized);
            if (string.Equals(
                    normalized,
                    "Tools/ros2_bridge/unity2foxglove_ros2_bridge/src/unity2foxglove_ros2_bridge.cpp",
                    StringComparison.Ordinal))
                return Unity.FoxgloveSDK.Tests.PhaseValidationSourceHelpers.ReadUnity2FoxgloveRos2BridgeSources(RepoRoot);
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
                return Unity.FoxgloveSDK.Tests.PhaseValidationSourceHelpers.ReadCameraPublisherEditorSources(RepoRoot);
            if (string.Equals(
                    normalized,
                    "Tools/foxglove-extensions/unity-cursor-bridge/src/index.ts",
                    StringComparison.Ordinal))
                return Unity.FoxgloveSDK.Tests.PhaseValidationSourceHelpers.ReadCursorBridgeSources(RepoRoot);
            if (string.Equals(
                    normalized,
                    "Packages/dev.unity2foxglove.sdk/Tests/Runtime/Program.cs",
                    StringComparison.Ordinal))
                return ProgramSources();
            if (string.Equals(
                    normalized,
                    "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/FoxRun/FoxRunRos2SubscriptionHub.cs",
                    StringComparison.Ordinal))
                return Ros2ForUnitySubscriptionHubSourceComposer.Compose(RepoRoot);
            if (string.Equals(
                    normalized,
                    "Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityCustomTypesupportSelectionTransaction.cs",
                    StringComparison.Ordinal))
                return Unity.FoxgloveSDK.Tests.PhaseValidationSourceHelpers.ReadRos2ForUnityCustomTypesupportSelectionTransactionSources(RepoRoot);
            if (string.Equals(
                    normalized,
                    "Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase184FoxRunProfileAcceptance.cs",
                    StringComparison.Ordinal))
                return Phase184AcceptanceSources();

            var path = Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), "Source file not found: " + relativePath + " (" + path + ")");
            return ReadLogicalSource(path);
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
            var expected = new HashSet<string>(fileNames, StringComparer.Ordinal);
            var actual = Directory.GetFiles(
                    root,
                    "MediaFoundationH264EncoderSidecar*.cs",
                    SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .ToHashSet(StringComparer.Ordinal);
            Assert.Equal(expected.OrderBy(name => name, StringComparer.Ordinal),
                actual.OrderBy(name => name, StringComparer.Ordinal));
            var paths = fileNames.Select(name => Path.Combine(root, name)).ToArray();
            return string.Join(Environment.NewLine, paths.Select(File.ReadAllText));
        }

        internal static string CreateMediaFoundationFixture()
        {
            var fixtureRoot = Path.Combine(
                Path.GetTempPath(),
                "phase192-media-foundation-" + Guid.NewGuid().ToString("N"));
            var sourceDirectory = Path.Combine(
                RepoRoot,
                "Packages",
                "dev.unity2foxglove.sdk",
                "Runtime",
                "Schemas",
                "Proto",
                "Video");
            var targetDirectory = Path.Combine(
                fixtureRoot,
                "Packages",
                "dev.unity2foxglove.sdk",
                "Runtime",
                "Schemas",
                "Proto",
                "Video");
            Directory.CreateDirectory(targetDirectory);
            foreach (var fileName in new[]
            {
                "MediaFoundationH264EncoderSidecar.cs",
                "MediaFoundationH264EncoderSidecar.Lifecycle.cs",
                "MediaFoundationH264EncoderSidecar.NativeLifecycle.cs",
                "MediaFoundationH264EncoderSidecar.Output.cs",
                "MediaFoundationH264EncoderSidecar.ComInterop.cs",
            })
            {
                File.Copy(
                    Path.Combine(sourceDirectory, fileName),
                    Path.Combine(targetDirectory, fileName));
            }

            return fixtureRoot;
        }

        internal static string CreateBridgeSmokeCppFixture()
        {
            var fixtureRoot = Path.Combine(
                Path.GetTempPath(),
                "phase192-bridge-smoke-" + Guid.NewGuid().ToString("N"));
            var testDirectory = Path.Combine(
                fixtureRoot,
                "Tools",
                "ros2_bridge",
                "unity2foxglove_ros2_bridge",
                "test");
            var fragmentDirectory = Path.Combine(testDirectory, "test_bridge_smoke");
            Directory.CreateDirectory(fragmentDirectory);
            var names = new[]
            {
                "FixtureAndHelpers.inl",
                "ProtocolContractTests.inl",
                "LifecycleSetup.inl",
                "SessionAndPublisherTests.inl",
                "PublisherContractTests.inl",
                "TransportIntegrationTests.inl",
            };
            File.WriteAllLines(
                Path.Combine(testDirectory, "test_bridge_smoke.cpp"),
                names.Select(name => "#include \"test_bridge_smoke/" + name + "\""));
            foreach (var name in names)
                File.WriteAllText(Path.Combine(fragmentDirectory, name), "int value = 0;\n");
            return fixtureRoot;
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
                var companionPath = Path.Combine(package, "sections");
                if (Directory.Exists(companionPath))
                {
                    foreach (var section in Directory
                        .GetFiles(companionPath, "*.py", SearchOption.TopDirectoryOnly)
                        .Where(path => !string.Equals(
                            Path.GetFileName(path),
                            "__init__.py",
                            StringComparison.OrdinalIgnoreCase))
                        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                    {
                        source += Environment.NewLine + File.ReadAllText(section);
                    }
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
                        var files = Directory.GetFiles(child, "*.cs", SearchOption.AllDirectories);
                        var fileNames = new HashSet<string>(
                            files.Select(path => Path.GetRelativePath(child, path).Replace('\\', '/')),
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

    }
}
