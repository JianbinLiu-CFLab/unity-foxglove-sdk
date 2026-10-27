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

            var fileNames = new[]
            {
                "MediaFoundationH264EncoderSidecar.cs",
                "MediaFoundationH264EncoderSidecar.Lifecycle.cs",
                "MediaFoundationH264EncoderSidecar.NativeLifecycle.cs",
                "MediaFoundationH264EncoderSidecar.Output.cs",
                "MediaFoundationH264EncoderSidecar.ComInterop.cs",
            };
            var files = fileNames
                .Select(name => Path.Combine(dir, name))
                .Where(File.Exists)
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
