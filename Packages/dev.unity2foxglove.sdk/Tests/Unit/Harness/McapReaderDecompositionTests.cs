// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    [Trait("Phase", "170C")]
    [Trait("Domain", "MCAP")]
    public sealed class McapReaderDecompositionTests
    {
        [Fact]
        public void McapReaderDelegatesSummaryAndChunkHelpers()
        {
            var reader = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Reader/McapReader.cs");
            var summary = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Reader/McapSummaryBuilder.cs");
            var chunk = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Reader/McapChunkReader.cs");

            Assert.Contains("public class McapReader", reader, StringComparison.Ordinal);
            Assert.Contains("McapSummaryBuilder.FromSummarySection", reader, StringComparison.Ordinal);
            Assert.Contains("new McapSummaryBuilder(", reader, StringComparison.Ordinal);
            Assert.Contains("McapChunkReader.ReadChunkRecords", reader, StringComparison.Ordinal);
            Assert.Contains("McapChunkReader.EnumerateMessages", reader, StringComparison.Ordinal);
            Assert.Contains("McapChunkReader.EnumeratePrivateRecords", reader, StringComparison.Ordinal);
            Assert.DoesNotContain("private static IEnumerable<McapPrivateRecord> EnumerateChunkPrivateRecords", reader, StringComparison.Ordinal);

            Assert.Contains("internal sealed class McapSummaryBuilder", summary, StringComparison.Ordinal);
            Assert.Contains("FromSummarySection", summary, StringComparison.Ordinal);
            Assert.Contains("ApplyRecord", summary, StringComparison.Ordinal);
            Assert.Contains("Build()", summary, StringComparison.Ordinal);
            Assert.Contains("McapRecordDecoder.AddSequentialMessage", summary, StringComparison.Ordinal);
            Assert.Contains("McapRecordDecoder.ScanChunkRecords", summary, StringComparison.Ordinal);

            Assert.Contains("internal static class McapChunkReader", chunk, StringComparison.Ordinal);
            Assert.Contains("ReadChunkRecords", chunk, StringComparison.Ordinal);
            Assert.Contains("EnumerateMessages", chunk, StringComparison.Ordinal);
            Assert.Contains("EnumeratePrivateRecords", chunk, StringComparison.Ordinal);
            Assert.Contains("MCAP chunk CRC mismatch.", chunk, StringComparison.Ordinal);
            Assert.Contains("Chunk inner record content is truncated.", chunk, StringComparison.Ordinal);
        }
    }
}
