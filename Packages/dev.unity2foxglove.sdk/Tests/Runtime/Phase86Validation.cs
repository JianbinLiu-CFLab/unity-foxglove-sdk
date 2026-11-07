// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 86 validation for runtime hardening bugfixes.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validates Phase 86 runtime hardening source changes.
    /// </summary>
    public static class Phase86Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 86: Runtime Hardening Bugfixes ===");
            _passed = 0;

            VerifyAssetRegistryPathGuard();

            Console.WriteLine($"Phase 86: {_passed} checks passed.");
        }

        private static void VerifyMediaFoundationBitrateValidation()
        {
            var sidecar = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/MediaFoundationH264EncoderSidecar.cs");
            Check(!sidecar.Contains("checked((int)Math.Max(1, options.BitrateKbps) * 1000)"),
                "86C-3: Media Foundation sidecar uses validated bitrate calculation");
        }

        private static void VerifyAssetRegistryPathGuard()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Assets/FoxgloveAssetRegistry.cs");
            Check(source.Contains("FileSystemPathComparison")
                  && source.Contains("StartsWith(rootPrefix, comparison)")
                  && source.Contains("string.Equals(resolved, normalizedRoot, comparison)"),
                "86I-1: asset registry path traversal guard uses platform-aware comparison");
        }

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + name);

            _passed++;
            Console.WriteLine("[PASS] " + name);
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new InvalidOperationException("Could not find repository root.");

            var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
                throw new FileNotFoundException("Required repository file is missing.", path);
            return PhaseValidationSourceHelpers.ReadRequiredRepoText(relativePath);
        }
    }
}
