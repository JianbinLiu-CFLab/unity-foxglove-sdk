// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System;
using Unity.FoxgloveSDK.IO;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests
{
    public sealed class McapDataLoaderReviewTests
    {
        [Fact]
        public void ChannelAndTopicFiltersUseUnionSemantics()
        {
            using var stream = BuildTwoChannelMcap();
            using var loader = new McapDataLoader(stream, leaveOpen: true);

            var messages = loader.CreateIterator(new McapDataLoaderQuery
            {
                ChannelIds = new List<ushort> { 1 },
                Topics = new List<string> { "/b" }
            }).ToList();

            Assert.Equal(new ushort[] { 1, 2 }, messages.Select(message => message.ChannelId).ToArray());
        }

        [Fact]
        public void AmendmentStatisticsAccumulateRecordsWhenIndexesWereOmitted()
        {
            var path = Path.Combine(Path.GetTempPath(), "mcap-amendment-review-" + Guid.NewGuid().ToString("N") + ".mcap");
            try
            {
                File.WriteAllBytes(path, BuildUnindexedAttachmentMetadataMcap());
                using (var amendment = new McapAmendmentWriter(path))
                {
                    amendment.AddAttachment("new", "application/octet-stream", new byte[] { 2 }, 2);
                    amendment.AddMetadata("new", new Dictionary<string, string> { ["k"] = "v" });
                    amendment.Close();
                }

                using var stream = File.OpenRead(path);
                var summary = new McapReader(stream).ReadSummary();
                Assert.Equal((ulong)2, summary.Statistics.AttachmentCount);
                Assert.Equal((ulong)3, summary.Statistics.MetadataCount);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
                var backup = path + ".bak";
                if (File.Exists(backup)) File.Delete(backup);
            }
        }

        [Fact]
        public void MetadataLookupFallsBackWhenMetadataIndexesWereOmitted()
        {
            using var stream = BuildMetadataWithoutIndexMcap();
            using var reader = new McapIndexedReader(stream, leaveOpen: true);

            var metadata = reader.FindMetadata("review");

            Assert.NotNull(metadata);
            Assert.Equal("v", metadata.Metadata["k"]);
        }

        [Fact]
        public void MetadataLookupFallsBackWhenMetadataIndexIsPartial()
        {
            using var stream = BuildPartiallyIndexedMetadataMcap();
            using var reader = new McapIndexedReader(stream, leaveOpen: true);

            var metadata = reader.FindMetadata("unindexed");

            Assert.NotNull(metadata);
            Assert.Equal("fallback", metadata.Metadata["k"]);
        }

        [Fact]
        public void ReplayMetadataLookupFallsBackWhenMetadataIndexIsPartial()
        {
            var path = Path.Combine(Path.GetTempPath(), "mcap-replay-partial-metadata-" + Guid.NewGuid().ToString("N") + ".mcap");
            try
            {
                using (var stream = BuildPartiallyIndexedMetadataMcap())
                    File.WriteAllBytes(path, stream.ToArray());

                using var engine = new McapReplayEngine();
                engine.Load(path);
                var metadata = engine.FindMetadata("unindexed");

                Assert.NotNull(metadata);
                Assert.Equal("fallback", metadata.Metadata["k"]);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void MetadataFallbackReturnsFreshValuesAndHonorsCumulativeBounds()
        {
            using (var stream = BuildMetadataWithoutIndexMcap())
            using (var reader = new McapIndexedReader(stream, leaveOpen: true))
            {
                var first = reader.FindMetadata("review");
                first.Metadata["k"] = "caller mutation";

                var second = reader.FindMetadata("review");
                Assert.Equal("v", second.Metadata["k"]);
            }

            using (var stream = BuildPartiallyIndexedMetadataMcap())
            using (var reader = new McapReader(stream))
            {
                var summary = reader.ReadSummary();
                Assert.Throws<InvalidOperationException>(() =>
                    reader.BuildMetadataIndexInDataSection(
                        summary.DataSectionEndOffset,
                        maxRecords: 1,
                        maxBytes: 0));
            }
        }

        [Fact]
        public void LatestTieUsesStableSourcePositionAcrossSequentialCandidates()
        {
            using var stream = BuildTieMcap();
            using var reader = new McapReader(stream);
            var summary = reader.ReadSummary();
            var latest = new Dictionary<ushort, McapMessage>();
            reader.VisitSequentialMessages(
                summary.DataSectionEndOffset,
                message => McapLatestAtQuery.ConsiderLatestCandidate(
                    message,
                    new McapReadOptions { EndTimeNs = 100 },
                    null,
                    latest));

            Assert.Single(latest);
            Assert.Equal(new byte[] { 2 }, latest[1].Data);
            Assert.True(latest[1].SourceOffset > 0);
        }

        private static MemoryStream BuildTwoChannelMcap()
        {
            var stream = new MemoryStream();
            using (var writer = new McapWriter(stream, leaveOpen: true))
            {
                writer.WriteMagic();
                writer.WriteHeader("", "review");
                writer.WriteChannel(1, 0, "/a", "json", new Dictionary<string, string>());
                writer.WriteChannel(2, 0, "/b", "json", new Dictionary<string, string>());
                writer.WriteMessage(1, 1, 10, 10, Encoding.UTF8.GetBytes("a"));
                writer.WriteMessage(2, 1, 20, 20, Encoding.UTF8.GetBytes("b"));
                writer.WriteDataEnd();
                var summary = new McapFileSummary
                {
                    Statistics = new McapStatistics
                    {
                        MessageCount = 2,
                        ChannelCount = 2,
                        MessageStartTime = 10,
                        MessageEndTime = 20,
                        ChannelMessageCounts = new Dictionary<ushort, ulong> { [1] = 1, [2] = 1 }
                    }
                };
                summary.Channels.Add(new McapChannel { Id = 1, Topic = "/a", MessageEncoding = "json" });
                summary.Channels.Add(new McapChannel { Id = 2, Topic = "/b", MessageEncoding = "json" });
                McapSummarySerializer.WriteSummaryAndFooter(writer, summary, true, true);
                writer.WriteMagic();
                writer.Flush();
            }

            stream.Position = 0;
            return stream;
        }

        private static byte[] BuildUnindexedAttachmentMetadataMcap()
        {
            using var stream = new MemoryStream();
            using (var writer = new McapWriter(stream, leaveOpen: true))
            {
                writer.WriteMagic();
                writer.WriteHeader("", "review");
                writer.WriteAttachment(1, 1, "old", "application/octet-stream", new byte[] { 1 });
                writer.WriteMetadata("old", new Dictionary<string, string> { ["k"] = "v" });
                writer.WriteDataEnd();
                var summary = new McapFileSummary
                {
                    Statistics = new McapStatistics
                    {
                        AttachmentCount = 1,
                        MetadataCount = 2
                    }
                };
                McapSummarySerializer.WriteSummaryAndFooter(writer, summary, true, true);
                writer.WriteMagic();
                writer.Flush();
            }

            return stream.ToArray();
        }

        private static MemoryStream BuildMetadataWithoutIndexMcap()
        {
            var stream = new MemoryStream();
            using (var writer = new McapWriter(stream, leaveOpen: true))
            {
                writer.WriteMagic();
                writer.WriteHeader("", "review");
                writer.WriteMetadata("review", new Dictionary<string, string> { ["k"] = "v" });
                writer.WriteDataEnd();
                var summary = new McapFileSummary
                {
                    Statistics = new McapStatistics { MetadataCount = 1 }
                };
                McapSummarySerializer.WriteSummaryAndFooter(writer, summary, true, true);
                writer.WriteMagic();
                writer.Flush();
            }

            stream.Position = 0;
            return stream;
        }

        private static MemoryStream BuildPartiallyIndexedMetadataMcap()
        {
            var stream = new MemoryStream();
            using (var writer = new McapWriter(stream, leaveOpen: true))
            {
                writer.WriteMagic();
                writer.WriteHeader("", "review");
                var indexedOffset = (ulong)stream.Position;
                writer.WriteMetadata("indexed", new Dictionary<string, string> { ["k"] = "indexed" });
                var indexedLength = (ulong)stream.Position - indexedOffset;
                writer.WriteMetadata("unindexed", new Dictionary<string, string> { ["k"] = "fallback" });
                writer.WriteDataEnd();
                var summary = new McapFileSummary
                {
                    Statistics = new McapStatistics { MetadataCount = 2 }
                };
                summary.MetadataIndexes.Add(new McapMetadataIndex
                {
                    Offset = indexedOffset,
                    Length = indexedLength,
                    Name = "indexed"
                });
                McapSummarySerializer.WriteSummaryAndFooter(writer, summary, true, true);
                writer.WriteMagic();
                writer.Flush();
            }

            stream.Position = 0;
            return stream;
        }

        private static MemoryStream BuildTieMcap()
        {
            var stream = new MemoryStream();
            using (var writer = new McapWriter(stream, leaveOpen: true))
            {
                writer.WriteMagic();
                writer.WriteHeader("", "tie");
                writer.WriteChannel(1, 0, "/tie", "json", new Dictionary<string, string>());
                writer.WriteMessage(1, 7, 100, 100, new byte[] { 1 });
                writer.WriteMessage(1, 7, 100, 100, new byte[] { 2 });
                writer.WriteDataEnd();
                var summary = new McapFileSummary
                {
                    Statistics = new McapStatistics
                    {
                        MessageCount = 2,
                        ChannelCount = 1,
                        MessageStartTime = 100,
                        MessageEndTime = 100,
                        ChannelMessageCounts = new Dictionary<ushort, ulong> { [1] = 2 }
                    }
                };
                summary.Channels.Add(new McapChannel { Id = 1, Topic = "/tie", MessageEncoding = "json" });
                McapSummarySerializer.WriteSummaryAndFooter(writer, summary, true, true);
                writer.WriteMagic();
                writer.Flush();
            }

            stream.Position = 0;
            return stream;
        }
    }
}
