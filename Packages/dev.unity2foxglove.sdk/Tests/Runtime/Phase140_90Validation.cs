// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 140-90 source-shape regression coverage for optional ROS2/R2FU test optimizations.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase140_90Validation.
    /// </summary>
    public static class Phase140_90Validation
    {
        private static int _passed;

        /// <summary>
        /// Validation method for Validate.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 140-90: Optional ROS2/R2FU Tests Optimization ===");
            _passed = 0;

            VerifyPhase115CachesRoslynReferences();
            VerifyPhase105CachesSummaryLines();
            VerifyPhase107EnumeratesEditorFilesOnce();
            VerifyPhase108CachesRuntimeTextFiles();
            VerifyHashSidecarsAreDecodedFromSingleByteRead();
            VerifyRegistration();

            Console.WriteLine($"Phase 140-90: {_passed} checks passed.");
        }

        private static void VerifyPhase115CachesRoslynReferences()
        {
            var phase115E = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase115EValidation.cs");
            var phase115F = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase115FValidation.cs");
            var phase115G = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase115GValidation.cs");

            Check(phase115E.Contains("private static readonly Lazy<MetadataReference[]> CachedReferences", StringComparison.Ordinal)
                  && phase115E.Contains("private static MetadataReference[] References() => CachedReferences.Value;", StringComparison.Ordinal),
                "140-90A-1: Phase115E reuses Roslyn metadata references");
            Check(phase115F.Contains("private static readonly Lazy<MetadataReference[]> CachedReferences", StringComparison.Ordinal)
                  && phase115F.Contains("private static MetadataReference[] References() => CachedReferences.Value;", StringComparison.Ordinal),
                "140-90A-2: Phase115F reuses Roslyn metadata references");
            Check(phase115G.Contains("private static readonly Lazy<MetadataReference[]> CachedReferences", StringComparison.Ordinal)
                  && phase115G.Contains("CachedReferences.Value", StringComparison.Ordinal),
                "140-90A-3: Phase115G reuses Roslyn metadata references");
        }

        private static void VerifyPhase105CachesSummaryLines()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase105Validation.cs");

            Check(source.Contains("private static readonly Dictionary<string, string[]> SummaryLineCache", StringComparison.Ordinal)
                  && source.Contains("SummaryLineCache.Clear();", StringComparison.Ordinal)
                  && source.Contains("private static string[] ReadRepoLines", StringComparison.Ordinal)
                  && source.Contains("WindowBefore(lines, declaration", StringComparison.Ordinal),
                "140-90B-1: Phase105 summary checks reuse normalized source lines");
        }

        private static void VerifyPhase107EnumeratesEditorFilesOnce()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase107Validation.cs");

            Check(Count(source, "Directory.GetFiles(editorRoot, \"*.*\", SearchOption.AllDirectories)") == 1
                  && source.Contains("var editorFiles = Directory.GetFiles(editorRoot", StringComparison.Ordinal)
                  && source.Contains("foreach (var path in editorFiles.Where(HasTextExtension))", StringComparison.Ordinal),
                "140-90C-1: Phase107 optional editor boundary enumerates files once");
        }

        private static void VerifyPhase108CachesRuntimeTextFiles()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase108Validation.cs");

            Check(source.Contains("private static IReadOnlyList<string> _runtimeTextFiles", StringComparison.Ordinal)
                  && source.Contains("_runtimeTextFiles = null;", StringComparison.Ordinal)
                  && source.Contains("private static IReadOnlyList<string> RuntimeTextFiles()", StringComparison.Ordinal)
                  && source.Contains("return _runtimeTextFiles;", StringComparison.Ordinal),
                "140-90D-1: Phase108 runtime text file enumeration is cached per validation run");
        }

        private static void VerifyHashSidecarsAreDecodedFromSingleByteRead()
        {
            var phase112 = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase112Validation.cs");
            var phase115 = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase115Validation.cs");

            Check(!phase112.Contains("File.ReadAllText(hashPath)", StringComparison.Ordinal)
                  && phase112.Contains("Encoding.ASCII.GetString(hashBytes)", StringComparison.Ordinal),
                "140-90E-1: Phase112 validates manifest hash text from one byte read");
            Check(!phase115.Contains("File.ReadAllText(hashPath)", StringComparison.Ordinal)
                  && phase115.Contains("Encoding.ASCII.GetString(hashBytes)", StringComparison.Ordinal),
                "140-90E-2: Phase115 validates manifest hash text from one byte read");
        }

        private static void VerifyRegistration()
        {
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(project.Contains("Phase140_90Validation.cs", StringComparison.Ordinal),
                "140-90F-1: test project compiles Phase140_90Validation");
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            Check(registry.Contains("\"--phase140-90\"", StringComparison.Ordinal)
                  && registry.Contains("Phase140_90Validation.Validate", StringComparison.Ordinal),
                "140-90F-2: validation registry exposes --phase140-90");
        }

        private static int Count(string source, string needle)
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

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + label);
            Console.WriteLine("[PASS] " + label);
            _passed++;
        }
    }
}
