// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Util;
using Xunit;

namespace FoxgloveSdk.UnitTests.Mcap
{
    [Trait("Phase", "187-R4-F03")]
    [Trait("Domain", "MCAP")]
    public sealed class RemoteMcapRangeWriterTests
    {
        [Fact]
        public void CreateSlicePreservesSequenceAndPublishTime()
        {
            var path = Path.Combine(Path.GetTempPath(), "r4_f03_001_" + Guid.NewGuid().ToString("N") + ".mcap");
            try
            {
                File.WriteAllBytes(path, BuildUnchunkedMcap(
                    new MessageSpec(42, 100, 777, new byte[] { 1 }),
                    new MessageSpec(43, 100, 778, new byte[] { 2 })));

                using var slice = RemoteMcapRangeWriter.CreateSlice(
                    path,
                    new RemoteMcapRequest { StartTimeNs = 100, EndTimeNs = 100 },
                    -1);
                using var reader = new McapIndexedReader(slice, leaveOpen: true);
                var messages = reader.ReadMessages(new McapReadOptions
                {
                    AllowLinearFallback = true,
                    MaxMessages = 0
                });

                Assert.Equal(2, messages.Count);
                Assert.Equal((uint)42, messages[0].Sequence);
                Assert.Equal(100UL, messages[0].LogTime);
                Assert.Equal(777UL, messages[0].PublishTime);
                Assert.Equal(new byte[] { 1 }, messages[0].Data);
                Assert.Equal((uint)43, messages[1].Sequence);
                Assert.Equal(778UL, messages[1].PublishTime);
                Assert.Equal(new byte[] { 2 }, messages[1].Data);
            }
            finally
            {
                TryDelete(path);
            }
        }

        [Fact]
        public void CreateSliceRejectsCapBeforeReadingRecordsBeyondBudget()
        {
            var path = Path.Combine(Path.GetTempPath(), "r4_f03_002_" + Guid.NewGuid().ToString("N") + ".mcap");
            try
            {
                File.WriteAllBytes(path, BuildChunkedMcapWithLargeAndMalformedTail());

                var error = Assert.Throws<RemoteMcapRangeTooLargeException>(() =>
                    RemoteMcapRangeWriter.CreateSlice(
                        path,
                        new RemoteMcapRequest { StartTimeNs = 1, EndTimeNs = 3 },
                        1000));

                Assert.NotNull(error);
            }
            finally
            {
                TryDelete(path);
            }
        }

        private static byte[] BuildUnchunkedMcap(params MessageSpec[] messages)
        {
            using var stream = new MemoryStream();
            using (var writer = new McapWriter(stream, leaveOpen: true))
            {
                writer.WriteMagic();
                writer.WriteHeader("", "r4-f03-001");
                writer.WriteSchema(1, "r4.Schema", "jsonschema", Encoding.UTF8.GetBytes("{}"));
                writer.WriteChannel(1, 1, "/r4", "json", new Dictionary<string, string>());
                foreach (var message in messages)
                    writer.WriteMessage(1, message.Sequence, message.LogTime, message.PublishTime, message.Data);

                writer.WriteDataEnd();
                var summary = new McapFileSummary
                {
                    Statistics = new McapStatistics
                    {
                        MessageCount = (ulong)messages.Length,
                        SchemaCount = 1,
                        ChannelCount = 1,
                        MessageStartTime = messages.Length == 0 ? 0 : messages[0].LogTime,
                        MessageEndTime = messages.Length == 0 ? 0 : messages[messages.Length - 1].LogTime,
                        ChannelMessageCounts = new Dictionary<ushort, ulong> { [1] = (ulong)messages.Length }
                    }
                };
                summary.Schemas.Add(new McapSchema
                {
                    Id = 1,
                    Name = "r4.Schema",
                    Encoding = "jsonschema",
                    Data = Encoding.UTF8.GetBytes("{}")
                });
                summary.Channels.Add(new McapChannel
                {
                    Id = 1,
                    SchemaId = 1,
                    Topic = "/r4",
                    MessageEncoding = "json",
                    Metadata = new Dictionary<string, string>()
                });
                McapSummarySerializer.WriteSummaryAndFooter(writer, summary, true, true);
                writer.WriteMagic();
                writer.Flush();
            }

            return stream.ToArray();
        }

        private static byte[] BuildChunkedMcapWithLargeAndMalformedTail()
        {
            using var stream = new MemoryStream();
            using (var writer = new McapWriter(stream, leaveOpen: true))
            {
                writer.WriteMagic();
                writer.WriteHeader("", "r4-f03-002");
                writer.WriteSchema(1, "r4.Schema", "jsonschema", Encoding.UTF8.GetBytes("{}"));
                writer.WriteChannel(1, 1, "/r4", "json", new Dictionary<string, string>());
                var records = new MemoryStream();
                WriteRawRecord(records, MessageContent(1, 1, 1, new byte[] { 1 }));
                WriteRawRecord(records, MessageContent(1, 2, 2, new byte[8192]));
                WriteRawRecord(records, new byte[] { 1, 2, 3 });
                var raw = records.ToArray();
                var chunkOffset = (ulong)stream.Position;
                writer.WriteChunk(1, 3, (ulong)raw.Length, Crc32Helper.Compute(raw), "", (ulong)raw.Length, raw);
                var chunkLength = (ulong)stream.Position - chunkOffset;
                writer.WriteDataEnd();

                var summary = new McapFileSummary
                {
                    Statistics = new McapStatistics
                    {
                        MessageCount = 3,
                        SchemaCount = 1,
                        ChannelCount = 1,
                        MessageStartTime = 1,
                        MessageEndTime = 3,
                        ChannelMessageCounts = new Dictionary<ushort, ulong> { [1] = 3 }
                    }
                };
                summary.Schemas.Add(new McapSchema
                {
                    Id = 1,
                    Name = "r4.Schema",
                    Encoding = "jsonschema",
                    Data = Encoding.UTF8.GetBytes("{}")
                });
                summary.Channels.Add(new McapChannel
                {
                    Id = 1,
                    SchemaId = 1,
                    Topic = "/r4",
                    MessageEncoding = "json",
                    Metadata = new Dictionary<string, string>()
                });
                summary.ChunkIndexes.Add(new McapChunkIndex
                {
                    MessageStartTime = 1,
                    MessageEndTime = 3,
                    ChunkStartOffset = chunkOffset,
                    ChunkLength = chunkLength,
                    Compression = string.Empty,
                    CompressedSize = (ulong)raw.Length,
                    UncompressedSize = (ulong)raw.Length,
                    MessageIndexOffsets = new Dictionary<ushort, ulong>(),
                    MessageIndexLength = 0
                });
                McapSummarySerializer.WriteSummaryAndFooter(writer, summary, true, true);
                writer.WriteMagic();
                writer.Flush();
            }

            return stream.ToArray();
        }

        private static void WriteRawRecord(Stream destination, byte[] content)
        {
            destination.WriteByte(McapWriter.OpcodeMessage);
            McapWriter.WriteU64(destination, (ulong)content.Length);
            destination.Write(content, 0, content.Length);
        }

        private static byte[] MessageContent(ushort channelId, uint sequence, ulong time, byte[] data)
        {
            using var content = new MemoryStream();
            McapWriter.WriteU16(content, channelId);
            McapWriter.WriteU32(content, sequence);
            McapWriter.WriteU64(content, time);
            McapWriter.WriteU64(content, time);
            content.Write(data, 0, data.Length);
            return content.ToArray();
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { }
        }

        private readonly struct MessageSpec
        {
            public readonly uint Sequence;
            public readonly ulong LogTime;
            public readonly ulong PublishTime;
            public readonly byte[] Data;

            public MessageSpec(uint sequence, ulong logTime, ulong publishTime, byte[] data)
            {
                Sequence = sequence;
                LogTime = logTime;
                PublishTime = publishTime;
                Data = data;
            }
        }
    }
}
