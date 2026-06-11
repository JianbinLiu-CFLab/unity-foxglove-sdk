// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 140-71 source-shape regression coverage for schema evidence and manifest optimizations.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase140_71Validation.
    /// </summary>
    public static class Phase140_71Validation
    {
        private static int _passed;

        /// <summary>
        /// Validation method for Validate.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 140-71: Native Editor Tooling and Schema Evidence Optimization ===");
            _passed = 0;

            VerifySchemaEvidenceSettingsHotPaths();
            VerifySchemaManifestBuilderOptimizations();
            VerifyRegistration();

            Console.WriteLine($"Phase 140-71: {_passed} checks passed.");
        }

        private static void VerifySchemaEvidenceSettingsHotPaths()
        {
            var paths = Read("Packages/dev.unity2foxglove.sdk/Editor/SchemaEvidence/Unity2FoxgloveSchemaEvidencePaths.cs");
            Check(paths.Contains("private static readonly string CachedProjectRoot = ResolveProjectRoot();", StringComparison.Ordinal)
                  && paths.Contains("private static string ProjectRoot => CachedProjectRoot;", StringComparison.Ordinal)
                  && paths.Contains("private static string ResolveProjectRoot()", StringComparison.Ordinal),
                "140-71A-1: schema evidence project root is cached outside repaint-time path resolution");

            var projectRoot = Slice(paths, "private static string ProjectRoot", "        private static string ResolveProjectRoot()");
            Check(!projectRoot.Contains("Application.dataPath", StringComparison.Ordinal)
                  && !projectRoot.Contains("Directory.GetParent", StringComparison.Ordinal),
                "140-71A-2: ProjectRoot accessor no longer recomputes Unity and filesystem state");

            var settings = Read("Packages/dev.unity2foxglove.sdk/Editor/SchemaEvidence/Unity2FoxgloveSchemaEvidenceSettings.cs");
            var drawSettings = Slice(settings, "private static void DrawSettings()", "        private static void SaveAndSync()");
            Check(drawSettings.Contains("var resolvedRoot = Unity2FoxgloveSchemaEvidencePaths.ResolveCurrentEvidenceRoot();", StringComparison.Ordinal)
                  && CountOccurrences(drawSettings, "ResolveCurrentEvidenceRoot()") == 1
                  && drawSettings.Contains("Directory.CreateDirectory(resolvedRoot)", StringComparison.Ordinal)
                  && drawSettings.Contains("EditorUtility.RevealInFinder(resolvedRoot)", StringComparison.Ordinal),
                "140-71B-1: settings GUI resolves current evidence root once per repaint");
        }

        private static void VerifySchemaManifestBuilderOptimizations()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Editor/Shared/SchemaManifest/Unity2FoxgloveSchemaManifestBuilder.cs");
            var buildFoxRun = Slice(source, "private static Unity2FoxgloveFoxRunSummarySection BuildFoxRunSection", "        private static Unity2FoxgloveProtobufRegistrySection BuildProtobufRegistrySection()");
            Check(buildFoxRun.Contains("foreach (var type in types)", StringComparison.Ordinal)
                  && buildFoxRun.Contains("contracts += type.Contracts.Count;", StringComparison.Ordinal)
                  && buildFoxRun.Contains("fields += contract.Fields.Count;", StringComparison.Ordinal)
                  && !buildFoxRun.Contains(".Sum(", StringComparison.Ordinal),
                "140-71C-1: FoxRun schema summary counts contracts and fields in one pass");

            var sha = Slice(source, "private static string Sha256Hex(byte[] bytes)", "    }\r\n}");
            Check(source.Contains("private const string LowerHexDigits", StringComparison.Ordinal)
                  && sha.Contains("LowerHexDigits[b >> 4]", StringComparison.Ordinal)
                  && sha.Contains("LowerHexDigits[b & 0x0F]", StringComparison.Ordinal)
                  && !sha.Contains("ToString(\"x2\")", StringComparison.Ordinal),
                "140-71D-1: descriptor SHA256 hex encoding avoids per-byte string allocations");

            var buildSdk = Slice(source, "private static Unity2FoxgloveSdkTypedPublishersSection BuildSdkTypedPublishersSection()", "        private static IReadOnlyList<Unity2FoxgloveSdkTypedPublisherEntry> BuildSortedSdkTypedPublisherEntries()");
            var buildSorted = Slice(source, "private static IReadOnlyList<Unity2FoxgloveSdkTypedPublisherEntry> BuildSortedSdkTypedPublisherEntries()", "        private static void ValidatePublisherCatalog");
            Check(source.Contains("private static readonly IReadOnlyList<Unity2FoxgloveSdkTypedPublisherEntry> SortedSdkTypedPublisherEntries", StringComparison.Ordinal)
                  && buildSdk.Contains("SortedSdkTypedPublisherEntries.Count", StringComparison.Ordinal)
                  && !buildSdk.Contains("OrderBy", StringComparison.Ordinal)
                  && buildSorted.Contains("ValidatePublisherCatalog(entries)", StringComparison.Ordinal),
                "140-71E-1: SDK typed publisher catalog sorting and validation run once");
        }

        private static void VerifyRegistration()
        {
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(project.Contains("Phase140_71Validation.cs", StringComparison.Ordinal),
                "140-71F-1: test project compiles Phase140_71Validation");
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            Check(registry.Contains("\"--phase140-71\"", StringComparison.Ordinal)
                  && registry.Contains("Phase140_71Validation.Validate", StringComparison.Ordinal),
                "140-71F-2: validation registry exposes --phase140-71");
        }

        private static string Read(string relativePath)
            => File.ReadAllText(Path.Combine(RepoRoot(), relativePath));

        private static string RepoRoot()
        {
            var directory = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(directory))
            {
                if (Directory.Exists(Path.Combine(directory, ".git")))
                    return directory;
                directory = Directory.GetParent(directory)?.FullName;
            }
            throw new DirectoryNotFoundException("Could not locate repository root.");
        }

        private static string Slice(string source, string startText, string endText)
        {
            var start = source.IndexOf(startText, StringComparison.Ordinal);
            if (start < 0)
                throw new InvalidOperationException("Could not locate source slice start: " + startText);
            var end = source.IndexOf(endText, start + startText.Length, StringComparison.Ordinal);
            if (end < 0)
                end = source.Length;
            return source.Substring(start, end - start);
        }

        private static int CountOccurrences(string source, string text)
        {
            var count = 0;
            var index = 0;
            while ((index = source.IndexOf(text, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += text.Length;
            }
            return count;
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + label);
            Console.WriteLine("[PASS] " + label);
            _passed++;
        }
    }
}
