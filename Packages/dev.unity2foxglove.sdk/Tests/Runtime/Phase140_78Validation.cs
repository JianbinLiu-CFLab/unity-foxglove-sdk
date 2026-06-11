// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 140-78 source-shape regression coverage for release validator optimizations.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase140_78Validation.
    /// </summary>
    public static class Phase140_78Validation
    {
        private static int _passed;

        /// <summary>
        /// Validation method for Validate.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 140-78: Release Validators and Package Builders Optimization ===");
            _passed = 0;

            VerifyZipEntryHashingStreamsChunks();
            VerifyComponentSummaryCachesLowercaseNames();
            VerifyGeneratedMetasUseSinglePackageWalk();
            VerifyPackageBuildArtifactCheckShortCircuitsByName();
            VerifyRunCiSummaryRemainsReadable();
            VerifyRegistration();

            Console.WriteLine($"Phase 140-78: {_passed} checks passed.");
        }

        private static void VerifyZipEntryHashingStreamsChunks()
        {
            var source = Read("Scripts/release/inspect_r2fu_runtime_artifact.py");
            Check(source.Contains("def sha256_zip_entry(archive: zipfile.ZipFile, info: zipfile.ZipInfo) -> str:", StringComparison.Ordinal)
                  && source.Contains("with archive.open(info) as stream:", StringComparison.Ordinal)
                  && source.Contains("for chunk in iter(lambda: stream.read(1024 * 1024), b\"\"):", StringComparison.Ordinal)
                  && !source.Contains("archive.read(info.filename)", StringComparison.Ordinal)
                  && !source.Contains("sha256_bytes(data)", StringComparison.Ordinal),
                "140-78A-1: zip inventory hashes entries with bounded streaming reads");
        }

        private static void VerifyComponentSummaryCachesLowercaseNames()
        {
            var source = Read("Scripts/release/inspect_r2fu_runtime_artifact.py");
            var summary = Slice(source, "def summarize_components", "def inspect_zip");
            Check(summary.Contains("lower_names = [(name, name.lower()) for name in names]", StringComparison.Ordinal)
                  && summary.Contains("for name, lower in lower_names", StringComparison.Ordinal)
                  && !summary.Contains("name.lower() for pattern", StringComparison.Ordinal),
                "140-78B-1: component summary computes lowercase names once per entry");
        }

        private static void VerifyGeneratedMetasUseSinglePackageWalk()
        {
            var source = Read("Scripts/release/build_r2fu_runtime_package.py");
            var method = Slice(source, "def write_generated_metas", "def package_json");
            Check(method.Contains("paths = list(package.rglob(\"*\"))", StringComparison.Ordinal)
                  && method.Contains("directories = sorted((path for path in paths if path.is_dir())", StringComparison.Ordinal)
                  && method.Contains("files = sorted((path for path in paths if path.is_file())", StringComparison.Ordinal)
                  && CountOccurrences(method, "package.rglob(\"*\")") == 1,
                "140-78C-1: generated metadata writer walks package tree once");
        }

        private static void VerifyPackageBuildArtifactCheckShortCircuitsByName()
        {
            var source = Read("Scripts/release/validate_package.py");
            var method = Slice(source, "def check_package_build_artifacts", "def check_google_protobuf_collision");
            Check(method.Contains("if path.name in forbidden_dirs and path.is_dir():", StringComparison.Ordinal)
                  && !method.Contains("if path.is_dir() and path.name in forbidden_dirs:", StringComparison.Ordinal),
                "140-78D-1: package artifact check avoids directory stat for non-matching names");
        }

        private static void VerifyRunCiSummaryRemainsReadable()
        {
            var source = Read("Scripts/release/run_ci.py");
            Check(source.Contains("for name, ok in results.items():", StringComparison.Ordinal)
                  && source.Contains("failed = [n for n, ok in results.items() if not ok]", StringComparison.Ordinal),
                "140-78E-1: run_ci summary readability is intentionally unchanged");
        }

        private static void VerifyRegistration()
        {
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(project.Contains("Phase140_78Validation.cs", StringComparison.Ordinal),
                "140-78F-1: test project compiles Phase140_78Validation");
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            Check(registry.Contains("\"--phase140-78\"", StringComparison.Ordinal)
                  && registry.Contains("Phase140_78Validation.Validate", StringComparison.Ordinal),
                "140-78F-2: validation registry exposes --phase140-78");
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

        private static int CountOccurrences(string source, string needle)
        {
            var count = 0;
            var index = 0;
            while ((index = source.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += needle.Length;
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
