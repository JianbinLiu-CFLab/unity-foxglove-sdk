// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 140-89 source-shape regression coverage for ROS2 bridge/schema test optimizations.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase140_89Validation.
    /// </summary>
    public static class Phase140_89Validation
    {
        private static int _passed;

        /// <summary>
        /// Validation method for Validate.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 140-89: ROS2 Bridge and Schema Tests Optimization ===");
            _passed = 0;

            VerifyPhase90CachesStableRelativePaths();
            VerifyPhase91ReusesPointCloudFrameAcrossBuilderChecks();
            VerifyPhase100CachesMethodSignatureRegexes();
            VerifyRegistration();

            Console.WriteLine($"Phase 140-89: {_passed} checks passed.");
        }

        private static void VerifyPhase90CachesStableRelativePaths()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase90Validation.cs");
            var method = Slice(source, "private static string ComputeSourceTreeSha256", "        private static string ToStableRelativePath");

            Check(method.Contains("Select(path => new SourceFilePath(path, ToStableRelativePath(sourceRoot, path)))", StringComparison.Ordinal)
                  && method.Contains("OrderBy(file => file.RelativePath", StringComparison.Ordinal)
                  && method.Contains("Encoding.UTF8.GetBytes(file.RelativePath)", StringComparison.Ordinal),
                "140-89A-1: Phase90 hashes source files with a cached stable relative path");
        }

        private static void VerifyPhase91ReusesPointCloudFrameAcrossBuilderChecks()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase91Validation.cs");
            var verifyMethod = Slice(source, "private static void VerifyMessageBuilders", "        private static void VerifyFrameTransformBuilder");

            Check(verifyMethod.Contains("var pointFrame = BuildPointCloudFrame();", StringComparison.Ordinal)
                  && verifyMethod.Contains("VerifyPointCloudBuilder(pointFrame);", StringComparison.Ordinal)
                  && verifyMethod.Contains("VerifyCompressedPointCloudBuilder(pointFrame);", StringComparison.Ordinal)
                  && source.Contains("private static void VerifyPointCloudSharedPacking(PointCloudFrame frame)", StringComparison.Ordinal),
                "140-89B-1: Phase91 reuses one point cloud frame across identical builder checks");
        }

        private static void VerifyPhase100CachesMethodSignatureRegexes()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase100Validation.cs");
            var method = Slice(source, "private static int FindMethodSignature", "        private static void Check");

            Check(source.Contains("private static readonly Dictionary<string, Regex> MethodSignatureRegexes", StringComparison.Ordinal)
                  && method.Contains("lock (MethodSignatureRegexes)", StringComparison.Ordinal)
                  && (method.Contains("MethodSignatureRegexes.TryGetValue(methodName, out var regex)", StringComparison.Ordinal)
                      || method.Contains("MethodSignatureRegexes.TryGetValue(methodName, out regex)", StringComparison.Ordinal))
                  && method.Contains("regex.Match(source)", StringComparison.Ordinal),
                "140-89C-1: Phase100 reuses method-signature regexes by method name");
        }

        private static void VerifyRegistration()
        {
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(project.Contains("Phase140_89Validation.cs", StringComparison.Ordinal),
                "140-89D-1: test project compiles Phase140_89Validation");
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            Check(registry.Contains("\"--phase140-89\"", StringComparison.Ordinal)
                  && registry.Contains("Phase140_89Validation.Validate", StringComparison.Ordinal),
                "140-89D-2: validation registry exposes --phase140-89");
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

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + label);
            Console.WriteLine("[PASS] " + label);
            _passed++;
        }
    }
}
