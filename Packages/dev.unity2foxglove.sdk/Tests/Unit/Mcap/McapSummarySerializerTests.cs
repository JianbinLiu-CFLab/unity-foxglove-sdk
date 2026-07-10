// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.FoxgloveSDK.IO;
using Xunit;

namespace FoxgloveSdk.UnitTests.Mcap
{
    public sealed class McapSummarySerializerTests
    {
        [Theory]
        [InlineData(true, true)]
        [InlineData(false, false)]
        public void WritesDeterministicReadableSummary(bool writeSummaryOffsets, bool enableSummaryCrc)
        {
            var first = WriteFinalizedMcap(writeSummaryOffsets, enableSummaryCrc);
            var second = WriteFinalizedMcap(writeSummaryOffsets, enableSummaryCrc);

            Assert.Equal(first, second);

            using var stream = new MemoryStream(first);
            using var reader = new McapReader(stream);
            var summary = reader.ReadSummary();

            Assert.Single(summary.Schemas);
            Assert.Equal("serializer.Schema", summary.Schemas[0].Name);
            Assert.Single(summary.Channels);
            Assert.Equal("/serializer/test", summary.Channels[0].Topic);
            Assert.NotNull(summary.Statistics);
            Assert.Equal(7UL, summary.Statistics.MessageCount);
            Assert.Equal(7UL, summary.Statistics.ChannelMessageCounts[1]);
        }

        private static byte[] WriteFinalizedMcap(bool writeSummaryOffsets, bool enableSummaryCrc)
        {
            using var stream = new MemoryStream();
            using var writer = new McapWriter(stream, leaveOpen: true);
            writer.WriteMagic();
            writer.WriteHeader("", "summary-serializer-test");
            writer.WriteDataEnd();
            McapSummarySerializer.WriteSummaryAndFooter(
                writer,
                CreateSummary(),
                writeSummaryOffsets,
                enableSummaryCrc);
            writer.WriteMagic();
            writer.Flush();
            return stream.ToArray();
        }

        private static McapFileSummary CreateSummary()
        {
            var summary = new McapFileSummary
            {
                Statistics = new McapStatistics
                {
                    MessageCount = 7,
                    SchemaCount = 1,
                    ChannelCount = 1,
                    MessageStartTime = 10,
                    MessageEndTime = 20,
                    ChannelMessageCounts = new Dictionary<ushort, ulong> { [1] = 7 }
                }
            };
            summary.Schemas.Add(new McapSchema
            {
                Id = 1,
                Name = "serializer.Schema",
                Encoding = "jsonschema",
                Data = Encoding.UTF8.GetBytes("{}")
            });
            summary.Channels.Add(new McapChannel
            {
                Id = 1,
                SchemaId = 1,
                Topic = "/serializer/test",
                MessageEncoding = "json",
                Metadata = new Dictionary<string, string> { ["source"] = "unit" }
            });
            return summary;
        }
    }
}
