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
        public static string ReadRegisteredCSharpSource(
            string facadeRelativePath,
            params string[] fragmentRelativePaths)
        {
            if (string.IsNullOrWhiteSpace(facadeRelativePath))
                throw new ArgumentException("Facade path is required.", nameof(facadeRelativePath));

            var allRelativePaths = new[] { facadeRelativePath }
                .Concat(fragmentRelativePaths ?? Array.Empty<string>())
                .ToArray();
            var allPaths = allRelativePaths
                .Select(RepoPath)
                .ToArray();
            var fragmentPaths = allPaths.Skip(1).ToArray();
            foreach (var group in fragmentPaths.GroupBy(Path.GetDirectoryName))
            {
                var expected = new HashSet<string>(
                    group.Select(path => Path.GetRelativePath(group.Key, path)),
                    StringComparer.OrdinalIgnoreCase);
                var actual = new HashSet<string>(
                    Directory.GetFiles(group.Key, "*.cs", SearchOption.AllDirectories)
                        .Select(path => Path.GetRelativePath(group.Key, path)),
                    StringComparer.OrdinalIgnoreCase);
                if (!expected.SetEquals(actual))
                    throw new InvalidDataException(
                        "Registered C# source set mismatch in " + group.Key
                        + ". Expected: " + string.Join(", ", expected.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
                        + "; actual: " + string.Join(", ", actual.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)));
            }

            return string.Join(Environment.NewLine, allPaths.Select(File.ReadAllText));
        }
        public static string ReadFoxRunRos2SubscriptionBindingSources()
        {
            return ReadFoxRunRos2SubscriptionBindingSources(FindRequiredRepoRoot());
        }
        public static string ReadFoxRunRos2SubscriptionBindingSources(string root)
        {
            const string facade =
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/FoxRun/FoxRunRos2SubscriptionBinding.cs";
            var facadePath = Path.Combine(root, facade.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(facadePath))
                throw new FileNotFoundException("Missing ROS2 for Unity subscription binding source: " + facadePath, facadePath);

            var fragmentDirectory = Path.Combine(
                root,
                "Packages",
                "dev.unity2foxglove.ros2forunity",
                "Runtime",
                "Native",
                "FoxRun",
                "Decomposed",
                "FoxRunRos2SubscriptionBinding");
            var expectedFragments = new[]
            {
                "FoxRunRos2SubscriptionBinding.Registration.cs",
                "FoxRunRos2SubscriptionBinding.Application.cs",
                "FoxRunRos2SubscriptionBinding.Acceptance.cs",
                "FoxRunRos2SubscriptionBinding.Lifecycle.cs",
                "FoxRunRos2SubscriptionBinding.Cleanup.cs",
            };
            if (!Directory.Exists(fragmentDirectory))
                throw new DirectoryNotFoundException("ROS2 for Unity subscription binding fragment directory was not found: " + fragmentDirectory);

            var actualFragments = Directory.GetFiles(fragmentDirectory, "*.cs", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(fragmentDirectory, path).Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            if (!expectedFragments.OrderBy(path => path, StringComparer.Ordinal).SequenceEqual(actualFragments, StringComparer.Ordinal))
                throw new InvalidDataException(
                    "ROS2 for Unity subscription binding source set mismatch. Expected: "
                    + string.Join(", ", expectedFragments)
                    + "; actual: " + string.Join(", ", actualFragments));

            var fragmentPaths = expectedFragments.Select(name => Path.Combine(fragmentDirectory, name));
            return string.Join(Environment.NewLine, new[] { facadePath }.Concat(fragmentPaths).Select(File.ReadAllText));
        }

        public static string ReadFoxRunRos2CustomPublisherHubSources()
        {
            return ReadFoxRunRos2CustomPublisherHubSources(FindRequiredRepoRoot());
        }

        public static string ReadFoxRunRos2CustomPublisherHubSources(string root)
        {
            const string facade =
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/FoxRun/FoxRunRos2CustomPublisherHub.cs";
            var facadePath = Path.Combine(root, facade.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(facadePath))
                throw new FileNotFoundException("Missing ROS2 for Unity custom publisher hub facade: " + facadePath, facadePath);

            var fragmentDirectory = Path.Combine(
                root,
                "Packages",
                "dev.unity2foxglove.ros2forunity",
                "Runtime",
                "Native",
                "FoxRun",
                "Decomposed",
                "FoxRunRos2CustomPublisherHub");
            var expectedFragments = new[]
            {
                "FoxRunRos2CustomPublisherHub.Application.cs",
                "FoxRunRos2CustomPublisherHub.Bindings.cs",
                "FoxRunRos2CustomPublisherHub.Cleanup.cs",
                "FoxRunRos2CustomPublisherHub.Registration.cs",
                "FoxRunRos2CustomPublisherHub.SessionTracker.cs",
            };
            if (!Directory.Exists(fragmentDirectory))
                throw new DirectoryNotFoundException("ROS2 for Unity custom publisher hub fragment directory was not found: " + fragmentDirectory);

            var actualFragments = Directory.GetFiles(fragmentDirectory, "*.cs", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(fragmentDirectory, path).Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            if (!expectedFragments.OrderBy(path => path, StringComparer.Ordinal).SequenceEqual(actualFragments, StringComparer.Ordinal))
                throw new InvalidDataException(
                    "ROS2 for Unity custom publisher hub source set mismatch. Expected: "
                    + string.Join(", ", expectedFragments)
                    + "; actual: " + string.Join(", ", actualFragments));

            var fragmentPaths = expectedFragments.Select(name => Path.Combine(fragmentDirectory, name));
            return string.Join(Environment.NewLine, new[] { facadePath }.Concat(fragmentPaths).Select(File.ReadAllText));
        }

        public static string ReadRos2ForUnityCustomTypesupportSelectionTransactionSources()
        {
            return ReadRos2ForUnityCustomTypesupportSelectionTransactionSources(FindRequiredRepoRoot());
        }

        public static string ReadRos2ForUnityCustomTypesupportSelectionTransactionSources(string root)
        {
            const string facade =
                "Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityCustomTypesupportSelectionTransaction.cs";
            var facadePath = Path.Combine(root, facade.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(facadePath))
                throw new FileNotFoundException("Missing R2FU typesupport selection facade: " + facadePath, facadePath);

            var fragmentDirectory = Path.Combine(
                root,
                "Packages",
                "dev.unity2foxglove.ros2forunity",
                "Editor",
                "Decomposed",
                "Ros2ForUnityCustomTypesupportSelectionTransaction");
            var expectedFragments = new[]
            {
                "Ros2ForUnityCustomTypesupportSelectionTransaction.Discovery.cs",
                "Ros2ForUnityCustomTypesupportSelectionTransaction.Manifest.cs",
                "Ros2ForUnityCustomTypesupportSelectionTransaction.Models.cs",
                "Ros2ForUnityCustomTypesupportSelectionTransaction.NativeIdentity.cs",
            };
            if (!Directory.Exists(fragmentDirectory))
                throw new DirectoryNotFoundException("R2FU typesupport selection fragment directory was not found: " + fragmentDirectory);

            var actualFragments = Directory.GetFiles(fragmentDirectory, "*.cs", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(fragmentDirectory, path).Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            if (!expectedFragments.OrderBy(path => path, StringComparer.Ordinal).SequenceEqual(actualFragments, StringComparer.Ordinal))
                throw new InvalidDataException(
                    "R2FU typesupport selection source set mismatch. Expected: "
                    + string.Join(", ", expectedFragments)
                    + "; actual: " + string.Join(", ", actualFragments));

            var fragmentPaths = expectedFragments.Select(name => Path.Combine(fragmentDirectory, name));
            return string.Join(Environment.NewLine, new[] { facadePath }.Concat(fragmentPaths).Select(File.ReadAllText));
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
        public static string ReadCameraPublisherEditorSources()
        {
            return ReadCameraPublisherEditorSources(FindRequiredRepoRoot());
        }

        public static string ReadCameraPublisherEditorSources(string root)
        {
            var directory = Path.Combine(
                root,
                "Packages",
                "dev.unity2foxglove.sdk",
                "Editor",
                "Publishers");
            var fileNames = new[]
            {
                "FoxgloveCameraPublisherEditor.cs",
                "FoxgloveCameraPublisherEditor.Sections.cs",
                "FoxgloveCameraPublisherEditor.Paths.cs",
                "FoxgloveCameraPublisherEditor.Windows.cs",
            };
            var expected = new HashSet<string>(fileNames, StringComparer.Ordinal);
            var actual = Directory.GetFiles(
                    directory,
                    "FoxgloveCameraPublisherEditor*.cs",
                    SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .ToHashSet(StringComparer.Ordinal);
            if (!expected.SetEquals(actual))
                throw new InvalidDataException(
                    "Camera editor source set mismatch. Expected: "
                    + string.Join(", ", expected.OrderBy(name => name, StringComparer.Ordinal))
                    + "; actual: " + string.Join(", ", actual.OrderBy(name => name, StringComparer.Ordinal)));

            return string.Join(
                Environment.NewLine,
                fileNames.Select(name => File.ReadAllText(Path.Combine(directory, name))));
        }

        public static string ReadCursorBridgeSources()
        {
            return ReadCursorBridgeSources(FindRequiredRepoRoot());
        }

        public static string ReadCursorBridgeSources(string root)
        {
            var directory = Path.Combine(
                root,
                "Tools",
                "foxglove-extensions",
                "unity-cursor-bridge",
                "src");
            var expected = new[]
            {
                "index.ts",
                "helpers.ts",
                "panel.ts",
                "panel_dom.ts",
            };
            var actualAll = Directory.GetFiles(directory, "*.ts", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(directory, path).Replace('\\', '/'))
                .ToHashSet(StringComparer.Ordinal);
            var knownTestFiles = new HashSet<string>(
                new[] { "index.test.ts" },
                StringComparer.Ordinal);
            var unexpectedTestFiles = actualAll
                .Where(name => name.EndsWith(".test.ts", StringComparison.OrdinalIgnoreCase)
                               && !knownTestFiles.Contains(name))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            if (unexpectedTestFiles.Length > 0)
                throw new InvalidDataException(
                    "Cursor bridge source set contains unknown test files: "
                    + string.Join(", ", unexpectedTestFiles));
            var actual = actualAll
                .Except(knownTestFiles, StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            if (!expected.OrderBy(name => name, StringComparer.Ordinal).SequenceEqual(actual, StringComparer.Ordinal))
                throw new InvalidDataException(
                    "Cursor bridge source set mismatch. Expected: "
                    + string.Join(", ", expected)
                    + "; actual: " + string.Join(", ", actual));

            return string.Join(
                Environment.NewLine,
                expected.Select(name => File.ReadAllText(Path.Combine(directory, name))));
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

            var topLevelNames = new[]
            {
                "McapRecorder.cs",
                "McapRecorder.Schema.cs",
                "McapRecorder.TopicRouter.cs",
                "McapRecorder.Types.cs"
            };
            var fragmentNames = new[]
            {
                "ChannelAdmission.cs",
                "ChunkLifecycle.cs",
                "CloseAndRecovery.cs",
                "MessageWriting.cs",
                "MetadataAndAttachments.cs"
            };
            var expectedSourcePaths = topLevelNames
                .Concat(new[] { "McapAmendmentWriter.cs" })
                .Concat(fragmentNames.Select(name => Path.Combine(
                    "Decomposed",
                    "McapRecorder",
                    name)))
                .Select(path => path.Replace('\\', '/'))
                .ToHashSet(StringComparer.Ordinal);
            var fragmentDir = Path.Combine(dir, "Decomposed", "McapRecorder");
            if (!Directory.Exists(fragmentDir))
                throw new DirectoryNotFoundException("MCAP recorder decomposed directory was not found.");

            var actualSourcePaths = Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(dir, path).Replace('\\', '/'))
                .ToHashSet(StringComparer.Ordinal);
            if (!expectedSourcePaths.SetEquals(actualSourcePaths))
            {
                throw new InvalidDataException(
                    "MCAP recorder source composition is incomplete or contains unregistered partial files. "
                    + "Missing: "
                    + string.Join(", ", expectedSourcePaths.Except(actualSourcePaths).OrderBy(name => name, StringComparer.Ordinal))
                    + "; unexpected: "
                    + string.Join(", ", actualSourcePaths.Except(expectedSourcePaths).OrderBy(name => name, StringComparer.Ordinal)));
            }

            var files = topLevelNames
                .Select(name => Path.Combine(dir, name))
                .Concat(fragmentNames.Select(name => Path.Combine(fragmentDir, name)))
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
        public static string ReadManagedWsBackendSources()
        {
            var root = FindRequiredRepoRoot();
            var transportRoot = Path.Combine(
                root,
                "Packages",
                "dev.unity2foxglove.sdk",
                "Runtime",
                "Transport",
                "WebSocket");
            if (!Directory.Exists(transportRoot))
                throw new DirectoryNotFoundException("Managed WebSocket directory was not found.");

            var files = ManagedWsBackendSourceFileNames
                .Select(relativePath => Path.Combine(
                    transportRoot,
                    relativePath.Replace('/', Path.DirectorySeparatorChar)))
                .ToArray();
            foreach (var file in files)
                if (!File.Exists(file))
                    throw new FileNotFoundException("Missing Managed WebSocket source: " + file, file);

            var fragmentDirectory = Path.Combine(
                transportRoot,
                "Decomposed",
                "ManagedWsBackend");
            var expectedFragments = new HashSet<string>(
                ManagedWsBackendSourceFileNames.Skip(1).Select(Path.GetFileName),
                StringComparer.Ordinal);
            var actualFragments = new HashSet<string>(
                Directory.GetFiles(fragmentDirectory, "*.cs", SearchOption.TopDirectoryOnly)
                    .Select(Path.GetFileName),
                StringComparer.Ordinal);
            if (!expectedFragments.SetEquals(actualFragments))
                throw new InvalidDataException("Managed WebSocket source fragment set does not match the expected files.");

            var source = new StringBuilder();
            foreach (var file in files)
            {
                if (source.Length > 0)
                    source.Append(Environment.NewLine);
                source.Append(File.ReadAllText(file));
            }

            return source.ToString();
        }
        public static string ReadRos2BridgeRuntimeSources()
            => ReadRos2BridgeRuntimeSources(FindRequiredRepoRoot());
        public static string ReadRos2BridgeRuntimeSources(string root)
        {
            var runtimeRoot = Path.Combine(
                root,
                "Packages",
                "dev.unity2foxglove.ros2bridge",
                "Runtime",
                "Ros2Bridge");
            var paths = Ros2BridgeRuntimeSourceFileNames
                .Select(relativePath => Path.Combine(
                    runtimeRoot,
                    relativePath.Replace('/', Path.DirectorySeparatorChar)))
                .ToArray();
            foreach (var path in paths)
                if (!File.Exists(path))
                    throw new FileNotFoundException("Missing ROS2 Bridge runtime source: " + path, path);

            var actualRootSiblings = Directory.GetFiles(
                    runtimeRoot,
                    "*.cs",
                    SearchOption.TopDirectoryOnly)
                .Where(path => !string.Equals(
                    Path.GetFullPath(path),
                    Path.GetFullPath(paths[0]),
                    StringComparison.OrdinalIgnoreCase))
                .Where(path => ContainsPartialClassDeclaration(path, "Ros2BridgeWorkerLease"))
                .Select(Path.GetFileName)
                .ToHashSet(StringComparer.Ordinal);
            if (actualRootSiblings.Count != 0)
                throw new InvalidDataException(
                    "ROS2 Bridge runtime root contains unregistered sibling partials: "
                    + string.Join(", ", actualRootSiblings.OrderBy(name => name, StringComparer.Ordinal)));

            var fragmentDirectory = Path.Combine(
                runtimeRoot,
                "Decomposed",
                "Ros2BridgeRuntime");
            if (!Directory.Exists(fragmentDirectory))
                throw new DirectoryNotFoundException(
                    "ROS2 Bridge runtime decomposed directory was not found: " + fragmentDirectory);
            var expectedFragments = new HashSet<string>(
                paths.Skip(1)
                    .Select(path => Path.GetRelativePath(fragmentDirectory, path).Replace('\\', '/')),
                StringComparer.Ordinal);
            var actualFragments = Directory.GetFiles(
                    fragmentDirectory,
                    "*.cs",
                    SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(fragmentDirectory, path).Replace('\\', '/'))
                .ToHashSet(StringComparer.Ordinal);
            if (!expectedFragments.SetEquals(actualFragments))
                throw new InvalidDataException(
                    "ROS2 Bridge runtime source set mismatch. Expected: "
                    + string.Join(", ", expectedFragments.OrderBy(name => name, StringComparer.Ordinal))
                    + "; actual: "
                    + string.Join(", ", actualFragments.OrderBy(name => name, StringComparer.Ordinal)));

            var source = new StringBuilder();
            foreach (var path in paths)
            {
                if (source.Length > 0)
                    source.Append(Environment.NewLine);
                source.Append(File.ReadAllText(path));
            }
            return source.ToString();
        }
        public static string ReadRos2CustomCdrEmitterSources()
            => ReadRos2CustomCdrEmitterSources(FindRequiredRepoRoot());
        public static string ReadRos2CustomCdrEmitterSources(string root)
        {
            var emitterRoot = Path.Combine(
                root,
                "Packages",
                "dev.unity2foxglove.ros2bridge",
                "Editor",
                "FoxRun");
            var paths = Ros2CustomCdrEmitterSourceFileNames
                .Select(relativePath => Path.Combine(
                    emitterRoot,
                    relativePath.Replace('/', Path.DirectorySeparatorChar)))
                .ToArray();
            foreach (var path in paths)
                if (!File.Exists(path))
                    throw new FileNotFoundException(
                        "Missing ROS2 custom CDR emitter source: " + path,
                        path);

            var fragmentDirectory = Path.Combine(
                emitterRoot,
                "Decomposed",
                "Ros2CustomCdrEmitter");
            var expectedFragments = new HashSet<string>(
                paths.Skip(1)
                    .Select(path => Path.GetRelativePath(fragmentDirectory, path).Replace('\\', '/')),
                StringComparer.Ordinal);
            var actualFragments = Directory.Exists(fragmentDirectory)
                ? new HashSet<string>(
                    Directory.GetFiles(
                        fragmentDirectory,
                        "*.cs",
                        SearchOption.AllDirectories)
                        .Select(path => Path.GetRelativePath(fragmentDirectory, path).Replace('\\', '/')),
                    StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);
            if (!expectedFragments.SetEquals(actualFragments))
                throw new InvalidDataException(
                    "ROS2 custom CDR emitter source set does not match the expected files. Missing: "
                    + string.Join(
                        ", ",
                        expectedFragments
                            .Except(actualFragments)
                            .OrderBy(name => name, StringComparer.Ordinal))
                    + "; unexpected: "
                    + string.Join(
                        ", ",
                        actualFragments
                            .Except(expectedFragments)
                            .OrderBy(name => name, StringComparer.Ordinal)));

            var chunks = new List<string>
            {
                ReadRos2CustomCdrEmitterFacadeBody(paths[0]),
            };
            chunks.AddRange(
                paths.Skip(1).Select(ReadRos2CustomCdrEmitterFragmentBody));
            return string.Join(Environment.NewLine + Environment.NewLine, chunks)
                + Environment.NewLine
                + "    }"
                + Environment.NewLine
                + "}";
        }
        private static string ReadRos2CustomCdrEmitterFacadeBody(string path)
        {
            var lines = File.ReadAllLines(path);
            var end = FindRos2CustomCdrEmitterBodyEnd(lines, 0, path);
            return string.Join(Environment.NewLine, lines.Take(end));
        }
        private static string ReadRos2CustomCdrEmitterFragmentBody(string path)
        {
            var lines = File.ReadAllLines(path);
            var classLine = Array.FindIndex(
                lines,
                line => line.Contains(
                    "partial class FoxRunBridgeSourceEmitter",
                    StringComparison.Ordinal));
            if (classLine < 0)
                throw new InvalidDataException(
                    "ROS2 custom CDR emitter fragment class is missing: " + path);
            var openBrace = Array.FindIndex(
                lines,
                classLine,
                line => line.Trim() == "{");
            if (openBrace < 0 || lines.Length - openBrace < 3)
                throw new InvalidDataException(
                    "ROS2 custom CDR emitter fragment is too short: " + path);
            var end = FindRos2CustomCdrEmitterBodyEnd(lines, openBrace + 1, path);
            return string.Join(
                Environment.NewLine,
                lines.Skip(openBrace + 1).Take(end - openBrace - 1));
        }
        private static bool ContainsPartialClassDeclaration(string path, string className)
        {
            var source = File.ReadAllText(path);
            var directiveSymbols = Regex.Matches(
                    source,
                    @"^\s*#\s*(?:if|elif)\s+(.+?)\s*$",
                    RegexOptions.Multiline)
                .SelectMany(match => Regex.Matches(
                        match.Groups[1].Value,
                        @"\b[A-Za-z_][A-Za-z0-9_]*\b")
                    .Select(symbol => symbol.Value))
                .Where(symbol => !string.Equals(symbol, "true", StringComparison.Ordinal)
                    && !string.Equals(symbol, "false", StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var allSymbols = new[]
                {
                    "UNITY_EDITOR",
                    "UNITY_EDITOR_WIN",
                    "UNITY_STANDALONE_WIN",
                }
                .Concat(directiveSymbols)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            const int maxEnumeratedSymbols = 12;
            if (allSymbols.Length > maxEnumeratedSymbols)
                return Regex.IsMatch(
                    source,
                    $@"\bpartial\s+(?:class|struct|interface)\s+{Regex.Escape(className)}\b");

            var symbolSets = new List<string[]>();
            var combinations = 1 << allSymbols.Length;
            for (var mask = 0; mask < combinations; mask++)
            {
                symbolSets.Add(
                    allSymbols
                        .Where((symbol, index) => (mask & (1 << index)) != 0)
                        .ToArray());
            }
            foreach (var symbols in symbolSets)
            {
                var options = CSharpParseOptions.Default.WithPreprocessorSymbols(symbols);
                if (ContainsPartialClassDeclaration(CSharpSyntaxTree.ParseText(source, options), className))
                    return true;
            }

            return false;
        }
        private static bool ContainsPartialClassDeclaration(SyntaxTree tree, string className)
            => tree.GetRoot()
                .DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .Any(declaration =>
                    string.Equals(declaration.Identifier.ValueText, className, StringComparison.Ordinal)
                    && declaration.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PartialKeyword)));
        private static int FindRos2CustomCdrEmitterBodyEnd(
            string[] lines,
            int minimumExclusive,
            string path)
        {
            var end = lines.Length;
            while (end > minimumExclusive && string.IsNullOrWhiteSpace(lines[end - 1]))
                end--;
            for (var brace = 0; brace < 2; brace++)
            {
                if (end <= minimumExclusive || lines[end - 1].Trim() != "}")
                    throw new InvalidDataException(
                        "ROS2 custom CDR emitter outer brace is missing: " + path);
                end--;
                while (end > minimumExclusive && string.IsNullOrWhiteSpace(lines[end - 1]))
                    end--;
            }
            return end;
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
    }
}
