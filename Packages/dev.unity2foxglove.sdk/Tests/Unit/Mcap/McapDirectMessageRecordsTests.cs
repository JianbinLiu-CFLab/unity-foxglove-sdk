// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Direct-write MCAP message records (migrated from Phase37Validation).

using System;
using System.IO;
using System.Text;
using Unity.FoxgloveSDK.IO;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests
{
    /// <summary>
    /// Direct-write MCAP message records: roundtrip, null/empty payloads,
    /// interleaved channels, and small-chunk readability. Ported from Phase37Validation.
    /// </summary>
    [Trait("Phase", "37")]
    [Trait("Domain", "Mcap")]
    public class McapDirectMessageRecordsTests
    {
        [Fact]
        public void DirectMessageRoundtrip()
        {
            using var ms = new MemoryStream();
            using (var recorder = new McapRecorder(ms))
            {
                recorder.AddChannel(1, "/rt", "json", "test.RT", "jsonschema", "{\"type\":\"object\"}");
                recorder.WriteMessage(1, 100, Encoding.UTF8.GetBytes("{\"s\":0}"));
                recorder.WriteMessage(1, 200, Encoding.UTF8.GetBytes("{\"s\":1}"));
                recorder.WriteMessage(1, 300, Encoding.UTF8.GetBytes("{\"s\":2}"));
                recorder.Close();
            }

            ms.Position = 0;
            using var reader = new McapReader(ms);
            var summary = reader.ReadSummary();

            Assert.Single(summary.Channels);
            Assert.Equal(3UL, summary.Statistics.MessageCount);

            var chunkIdx = summary.ChunkIndexes[0];
            var records = reader.ReadChunkRecords(chunkIdx.ChunkStartOffset, chunkIdx.ChunkLength, out var crcValid);
            Assert.True(crcValid, "37A-1c: chunk CRC valid");
            var messages = reader.ReadChunkMessages(records);

            Assert.Equal(3, messages.Count);
            Assert.Equal(1, messages[0].ChannelId);
            Assert.Equal(0U, messages[0].Sequence);
            Assert.Equal(100UL, messages[0].LogTime);
            Assert.Equal(100UL, messages[0].PublishTime);
            Assert.Equal("{\"s\":0}", Encoding.UTF8.GetString(messages[0].Data));

            Assert.Equal(1U, messages[1].Sequence);
            Assert.Equal(200UL, messages[1].LogTime);

            Assert.Equal(2U, messages[2].Sequence);
            Assert.Equal(300UL, messages[2].LogTime);
        }

        [Fact]
        public void NullAndEmptyPayloads()
        {
            using var ms = new MemoryStream();
            using (var recorder = new McapRecorder(ms))
            {
                recorder.AddChannel(1, "/null", "json", "test.Null", "jsonschema", "{\"type\":\"object\"}");
                recorder.WriteMessage(1, 1000, null);
                recorder.WriteMessage(1, 2000, Array.Empty<byte>());
                recorder.Close();
            }

            ms.Position = 0;
            using var reader = new McapReader(ms);
            var summary = reader.ReadSummary();
            var chunkIdx = summary.ChunkIndexes[0];
            var records = reader.ReadChunkRecords(chunkIdx.ChunkStartOffset, chunkIdx.ChunkLength, out _);
            var messages = reader.ReadChunkMessages(records);

            Assert.Equal(2, messages.Count);
            Assert.Empty(messages[0].Data);
            Assert.Empty(messages[1].Data);
        }

        [Fact]
        public void InterleavedChannelsKeepCounts()
        {
            using var ms = new MemoryStream();
            using (var recorder = new McapRecorder(ms))
            {
                recorder.AddChannel(1, "/a", "json", "test.A", "jsonschema", "{\"type\":\"object\"}");
                recorder.AddChannel(2, "/b", "json", "test.B", "jsonschema", "{\"type\":\"object\"}");
                recorder.AddChannel(3, "/c", "json", "test.C", "jsonschema", "{\"type\":\"object\"}");

                recorder.WriteMessage(1, 100, Encoding.UTF8.GetBytes("{}"));
                recorder.WriteMessage(2, 200, Encoding.UTF8.GetBytes("{}"));
                recorder.WriteMessage(3, 300, Encoding.UTF8.GetBytes("{}"));
                recorder.WriteMessage(1, 400, Encoding.UTF8.GetBytes("{}"));
                recorder.WriteMessage(2, 500, Encoding.UTF8.GetBytes("{}"));
                recorder.Close();
            }

            ms.Position = 0;
            using var reader = new McapReader(ms);
            var summary = reader.ReadSummary();

            Assert.Equal(5UL, summary.Statistics.MessageCount);
            Assert.Equal(2UL, summary.Statistics.ChannelMessageCounts[1]);
            Assert.Equal(2UL, summary.Statistics.ChannelMessageCounts[2]);
            Assert.Equal(1UL, summary.Statistics.ChannelMessageCounts[3]);

            var chunkIdx = summary.ChunkIndexes[0];
            var records = reader.ReadChunkRecords(chunkIdx.ChunkStartOffset, chunkIdx.ChunkLength, out _);
            var messages = reader.ReadChunkMessages(records);
            Assert.Equal(5, messages.Count);
        }

        [Fact]
        public void SmallChunksRemainReadable()
        {
            using var ms = new MemoryStream();
            using (var recorder = new McapRecorder(ms, null, chunkSizeBytes: 96))
            {
                recorder.AddChannel(1, "/chunk", "json", "test.Chunk", "jsonschema", "{\"type\":\"object\"}");
                for (var i = 0; i < 20; i++)
                    recorder.WriteMessage(1, (ulong)i * 1000, Encoding.UTF8.GetBytes($"{{\"i\":{i}}}"));
                recorder.Close();
            }

            ms.Position = 0;
            using var reader = new McapReader(ms);
            var summary = reader.ReadSummary();

            Assert.Equal(20UL, summary.Statistics.MessageCount);
            Assert.True(summary.ChunkIndexes.Count > 1, "37A-4b: multiple chunks written");

            var totalMessages = 0;
            foreach (var ci in summary.ChunkIndexes)
            {
                var records = reader.ReadChunkRecords(ci.ChunkStartOffset, ci.ChunkLength, out var crcValid);
                Assert.True(crcValid, "37A-4c: chunk CRC valid");
                totalMessages += reader.ReadChunkMessages(records).Count;
            }

            Assert.Equal(20, totalMessages);
        }
    }
}
