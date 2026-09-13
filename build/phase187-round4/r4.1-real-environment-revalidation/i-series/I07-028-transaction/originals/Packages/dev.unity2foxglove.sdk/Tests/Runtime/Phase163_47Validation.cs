// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 163-47 review closure for MCAP/replay validation hardening.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase163_47Validation
    {
        private static int _passed;

        public static void Validate()
        {
            _passed = 0;

            var compression = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                "Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Common/McapCompression.cs");
            Check(compression.Contains("LZ4 decompressed size mismatch", StringComparison.Ordinal)
                  && compression.Contains("Zstd decompressed size mismatch", StringComparison.Ordinal)
                  && !compression.Contains("InvalidOperationException($\"LZ4 decompressed size mismatch", StringComparison.Ordinal)
                  && !compression.Contains("InvalidOperationException($\"Zstd decompressed size mismatch", StringComparison.Ordinal),
                "163-47A-1: compressed MCAP size mismatches use malformed-data exceptions");

            var unit = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                "Packages/dev.unity2foxglove.sdk/Tests/Unit/Mcap/McapLengthPrefixBoundsTests.cs");
            Check(unit.Contains("CompressionSizeMismatchesThrowInvalidDataException", StringComparison.Ordinal)
                  && unit.Contains("McapCompression.Decompress(\"lz4\"", StringComparison.Ordinal)
                  && unit.Contains("McapCompression.Decompress(\"zstd\"", StringComparison.Ordinal),
                "163-47A-2: unit tests cover lz4 and zstd declared-size mismatches");

            var phase50 = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                "Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase50Validation.cs");
            Check(phase50.Contains("BuildChunkRecordHeader(McapWriter.OpcodeSchema", StringComparison.Ordinal)
                  && !phase50.Contains("source.Contains(\"len > int.MaxValue\")", StringComparison.Ordinal),
                "163-47B-1: non-message oversized chunk length is covered behaviorally");

            var phase55 = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                "Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase55Validation.cs");
            Check(phase55.Contains("latest 100-message window", StringComparison.Ordinal)
                  && phase55.Contains("new ReplayController(new NoopLogger()", StringComparison.Ordinal)
                  && phase55.Contains("try { File.Delete(tmp); } catch { }", StringComparison.Ordinal),
                "163-47C-1: Phase55 validation documents history policy and cleans lifecycle helpers");

            var project = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                "Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var registry = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                "Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            Check(project.Contains("Phase163_47Validation.cs", StringComparison.Ordinal)
                  && registry.Contains("--phase163-47", StringComparison.Ordinal)
                  && registry.Contains("Phase163_47Validation.Validate", StringComparison.Ordinal),
                "163-47D-1: validation registry exposes --phase163-47");

            Console.WriteLine($"Phase 163-47: {_passed} MCAP/replay review checks passed.");
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidDataException("[FAIL] " + label);
            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
