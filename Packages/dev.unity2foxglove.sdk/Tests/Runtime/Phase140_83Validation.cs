// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 140-83 source-shape regression coverage for runtime harness helper optimizations.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase140_83Validation.
    /// </summary>
    public static class Phase140_83Validation
    {
        private static int _passed;

        /// <summary>
        /// Validation method for Validate.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 140-83: Runtime Test Harness Optimization ===");
            _passed = 0;

            VerifyR2fuGuardHelperAvoidsPerCallTokenArrayCopy();
            VerifyR2fuGuardHelperUsesTopFrameGuardState();
            VerifyRegistration();

            Console.WriteLine($"Phase 140-83: {_passed} checks passed.");
        }

        private static void VerifyR2fuGuardHelperAvoidsPerCallTokenArrayCopy()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseRos2ForUnityValidationHelpers.cs");
            var method = Slice(source, "public static bool AllR2fuReferencesAreGuarded", "        private static bool CurrentBranchGuarded");

            Check(method.Contains("IReadOnlyList<string> tokens", StringComparison.Ordinal)
                  && !method.Contains("tokens.ToArray()", StringComparison.Ordinal)
                  && !method.Contains("var tokenList =", StringComparison.Ordinal)
                  && method.Contains("FindToken(line, tokens)", StringComparison.Ordinal),
                "140-83A-1: R2FU guard helper scans caller tokens without per-call ToArray allocation");
        }

        private static void VerifyR2fuGuardHelperUsesTopFrameGuardState()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseRos2ForUnityValidationHelpers.cs");
            var method = Slice(source, "public static bool AllR2fuReferencesAreGuarded", "        private static bool CurrentBranchGuarded");

            Check(method.Contains("CurrentGuarded(stack)", StringComparison.Ordinal)
                  && !method.Contains("stack.Any(frame => frame.CurrentGuarded)", StringComparison.Ordinal),
                "140-83B-1: R2FU guard helper reads propagated guard state from the top stack frame");
        }

        private static void VerifyRegistration()
        {
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(project.Contains("Phase140_83Validation.cs", StringComparison.Ordinal),
                "140-83C-1: test project compiles Phase140_83Validation");
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            Check(registry.Contains("\"--phase140-83\"", StringComparison.Ordinal)
                  && registry.Contains("Phase140_83Validation.Validate", StringComparison.Ordinal),
                "140-83C-2: validation registry exposes --phase140-83");
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
