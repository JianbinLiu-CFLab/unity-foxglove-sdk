// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Phase 140-89/90/91/92/94 runtime validation optimization checks.

using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{

    [Trait("Phase", "140-92")]
    [Trait("Domain", "Harness")]
    public sealed class RvizStandardRos2ValidationOptimizationTests
    {
        [Fact]
        public void BoundaryScansReadEachFileOncePerTokenGroup()
        {
            foreach (var file in new[] { "Phase128Validation.cs", "Phase129Validation.cs", "Phase130Validation.cs", "Phase131Validation.cs", "Phase132Validation.cs", "Phase143Validation.cs" })
            {
                var source = TestSources.Runtime(file);
                Assert.DoesNotContain("File.ReadAllText(path).Contains", source, StringComparison.Ordinal);
                Assert.Contains("var text = File.ReadAllText(path)", source, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void McapRecordWritersUseTryGetBufferWithFallback()
        {
            foreach (var file in new[] { "Phase11Validation.cs", "Phase12Validation.cs" })
            {
                var source = TestSources.Runtime(file);
                Assert.Contains("content.TryGetBuffer(out var segment)", source, StringComparison.Ordinal);
                Assert.Contains("s.Write(segment.Array, segment.Offset, length)", source, StringComparison.Ordinal);
                Assert.Contains("var data = content.ToArray();", source, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void Phase12AndPhase13AvoidRepeatedTextAndSnapshotWork()
        {
            var phase12 = TestSources.Runtime("Phase12Validation.cs");
            var phase13 = TestSources.Runtime("Phase13Validation.cs");

            Assert.Contains("raw.SequenceEqual(lz4Result)", phase12, StringComparison.Ordinal);
            Assert.Contains("raw.SequenceEqual(zstdResult)", phase12, StringComparison.Ordinal);
            Assert.DoesNotContain("Encoding.UTF8.GetString(raw)", phase12, StringComparison.Ordinal);
            Assert.Contains("private static readonly byte[] PlaybackControlRequestIdBytes", phase13, StringComparison.Ordinal);
            Assert.Equal(1, TestSources.Count(phase13, "Encoding.UTF8.GetBytes(\"phase13-paused-seek\")"));
            Assert.Contains("var requestIdBytes = PlaybackControlRequestIdBytes;", phase13, StringComparison.Ordinal);
            Assert.Contains("public int SentBinaryFrameCount(uint clientId)", phase13, StringComparison.Ordinal);
            Assert.Contains("transport.SentBinaryFrameCount(7)", phase13, StringComparison.Ordinal);
            Assert.Contains("Contains(\"\\\"serverInfo\\\"\", StringComparison.Ordinal)", phase13, StringComparison.Ordinal);
            Assert.Contains("Contains(\"\\\"advertise\\\"\", StringComparison.Ordinal)", phase13, StringComparison.Ordinal);
        }

        [Fact]
        public void SecretScanAvoidsSplitArray()
        {
            var source = TestSources.Runtime("Phase134_1Validation.cs");

            Assert.Contains("using var reader = new StringReader(text);", source, StringComparison.Ordinal);
            Assert.DoesNotContain("text.Split('\\n')", source, StringComparison.Ordinal);
        }

        [Fact]
        public void Phase14092MigratedConsolePhaseIsRemoved()
            => TestSources.AssertConsolePhaseRemoved("Phase140_92Validation.cs", "--phase140-92", "Phase140_92Validation.Validate");
    }
}
