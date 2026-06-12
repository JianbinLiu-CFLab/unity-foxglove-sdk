// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 140-92 source-shape regression coverage for RViz2 and standard ROS2 test optimizations.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase140_92Validation.
    /// </summary>
    public static class Phase140_92Validation
    {
        private static int _passed;

        /// <summary>
        /// Validation method for Validate.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 140-92: RViz2 And Standard ROS2 Tests Optimization ===");
            _passed = 0;

            VerifyBoundaryScansReadEachFileOnce();
            VerifyMcapRecordWritersAvoidExposableBufferCopies();
            VerifyPhase12UsesByteEquality();
            VerifyPhase13AvoidsRepeatedSnapshotsAndParses();
            VerifySecretScanAvoidsSplitArray();
            VerifyRegistration();

            Console.WriteLine($"Phase 140-92: {_passed} checks passed.");
        }

        private static void VerifyBoundaryScansReadEachFileOnce()
        {
            foreach (var file in new[]
            {
                "Phase128Validation.cs",
                "Phase129Validation.cs",
                "Phase130Validation.cs",
                "Phase131Validation.cs",
                "Phase132Validation.cs",
                "Phase143Validation.cs"
            })
            {
                var source = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/" + file);
                Check(!source.Contains("File.ReadAllText(path).Contains", StringComparison.Ordinal)
                      && source.Contains("var text = File.ReadAllText(path)", StringComparison.Ordinal),
                    "140-92A-1: " + file + " reads each boundary scan file once per token group");
            }
        }

        private static void VerifyMcapRecordWritersAvoidExposableBufferCopies()
        {
            foreach (var file in new[] { "Phase11Validation.cs", "Phase12Validation.cs" })
            {
                var source = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/" + file);
                Check(source.Contains("content.TryGetBuffer(out var segment)", StringComparison.Ordinal)
                      && source.Contains("s.Write(segment.Array, segment.Offset, length)", StringComparison.Ordinal)
                      && source.Contains("var data = content.ToArray();", StringComparison.Ordinal),
                    "140-92B-1: " + file + " writes MemoryStream records through TryGetBuffer with fallback");
            }
        }

        private static void VerifyPhase12UsesByteEquality()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase12Validation.cs");
            Check(source.Contains("raw.SequenceEqual(lz4Result)", StringComparison.Ordinal)
                  && source.Contains("raw.SequenceEqual(zstdResult)", StringComparison.Ordinal)
                  && !source.Contains("Encoding.UTF8.GetString(raw)", StringComparison.Ordinal),
                "140-92C-1: Phase12 compression roundtrips use byte equality");
        }

        private static void VerifyPhase13AvoidsRepeatedSnapshotsAndParses()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase13Validation.cs");
            Check(source.Contains("private static readonly byte[] PlaybackControlRequestIdBytes", StringComparison.Ordinal)
                  && Count(source, "Encoding.UTF8.GetBytes(\"phase13-paused-seek\")") == 1
                  && source.Contains("var requestIdBytes = PlaybackControlRequestIdBytes;", StringComparison.Ordinal),
                "140-92D-1: Phase13 reuses playback-control request id bytes");
            Check(source.Contains("public int SentBinaryFrameCount(uint clientId)", StringComparison.Ordinal)
                  && source.Contains("transport.SentBinaryFrameCount(7)", StringComparison.Ordinal),
                "140-92D-2: Phase13 avoids snapshot allocation for count-only frame checks");
            Check(source.Contains("Contains(\"\\\"serverInfo\\\"\", StringComparison.Ordinal)", StringComparison.Ordinal)
                  && source.Contains("Contains(\"\\\"advertise\\\"\", StringComparison.Ordinal)", StringComparison.Ordinal),
                "140-92D-3: Phase13 prefilters JSON text before parsing helper loops");
        }

        private static void VerifySecretScanAvoidsSplitArray()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase134_1Validation.cs");
            Check(source.Contains("using var reader = new StringReader(text);", StringComparison.Ordinal)
                  && !source.Contains("text.Split('\\n')", StringComparison.Ordinal),
                "140-92E-1: Phase134_1 scans serialized secrets line-by-line without Split");
        }

        private static void VerifyRegistration()
        {
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(project.Contains("Phase140_92Validation.cs", StringComparison.Ordinal),
                "140-92F-1: test project compiles Phase140_92Validation");
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            Check(registry.Contains("\"--phase140-92\"", StringComparison.Ordinal)
                  && registry.Contains("Phase140_92Validation.Validate", StringComparison.Ordinal),
                "140-92F-2: validation registry exposes --phase140-92");
        }

        private static string Read(string relativePath)
            => File.ReadAllText(Path.Combine(RepoRoot(), relativePath));

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
