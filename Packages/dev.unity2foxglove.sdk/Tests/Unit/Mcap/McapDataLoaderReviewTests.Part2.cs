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
    public sealed partial class McapDataLoaderReviewTests
    {
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

        private static MemoryStream BuildPartiallyIndexedMetadataMcap(string indexedName = "indexed")
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
                    Name = indexedName
                });
                McapSummarySerializer.WriteSummaryAndFooter(writer, summary, true, true);
                writer.WriteMagic();
                writer.Flush();
            }

            stream.Position = 0;
            return stream;
        }

        private static MemoryStream BuildCompleteMetadataIndexMcap()
        {
            var stream = new MemoryStream();
            using (var writer = new McapWriter(stream, leaveOpen: true))
            {
                writer.WriteMagic();
                writer.WriteHeader("", "review");
                var indexedOffset = (ulong)stream.Position;
                writer.WriteMetadata("indexed", new Dictionary<string, string> { ["k"] = "indexed" });
                var indexedLength = (ulong)stream.Position - indexedOffset;
                writer.WriteDataEnd();
                var summary = new McapFileSummary
                {
                    Statistics = new McapStatistics { MetadataCount = 1 }
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

        private static MemoryStream BuildDuplicateIndexedMetadataMcap()
        {
            var stream = new MemoryStream();
            using (var writer = new McapWriter(stream, leaveOpen: true))
            {
                writer.WriteMagic();
                writer.WriteHeader("", "review");
                var indexedOffset = (ulong)stream.Position;
                writer.WriteMetadata("indexed", new Dictionary<string, string> { ["k"] = "indexed" });
                var indexedLength = (ulong)stream.Position - indexedOffset;
                writer.WriteMetadata("hidden", new Dictionary<string, string> { ["k"] = "fallback" });
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

        private static MemoryStream BuildNoMetadataWithCorruptDataRecordMcap()
        {
            using var clean = BuildTwoChannelMcap();
            var bytes = clean.ToArray();
            using (var reader = new McapReader(new MemoryStream(bytes)))
            {
                var summary = reader.ReadSummary();
                McapMessage first = null;
                reader.VisitSequentialMessages(
                    summary.DataSectionEndOffset,
                    message => first ??= message);
                Assert.NotNull(first);
                var offset = checked((int)first.SourceOffset);
                for (var i = 0; i < sizeof(ulong); i++)
                    bytes[offset + 1 + i] = byte.MaxValue;
            }

            return new MemoryStream(bytes);
        }

        private static MemoryStream BuildIndexedTieMcap()
        {
            var stream = new MemoryStream();
            using (var recorder = new McapRecorder(
                       stream,
                       null,
                       new McapWriterOptions
                       {
                           UseChunking = true,
                           IndexTypes = McapIndexTypes.Chunk | McapIndexTypes.Message,
                           UseStatistics = true,
                           UseSummaryOffsets = false,
                           EnableCrcs = false,
                           ChunkSizeBytes = 1024
                       },
                       leaveOpen: true))
            {
                recorder.AddChannel(1, "/tie", "json", "mcap.Tie", "jsonschema", "{}");
                recorder.WriteMessagePreservingMcapMetadata(1, 7, 100, 100, new byte[] { 1 });
                recorder.WriteMessagePreservingMcapMetadata(1, 7, 100, 100, new byte[] { 2 });
                recorder.Close();
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

        private sealed class CountingReadStream : MemoryStream
        {
            public CountingReadStream(byte[] buffer)
                : base(buffer, writable: false)
            {
            }

            public int ReadOperations { get; private set; }

            public void ResetReadOperations() => ReadOperations = 0;

            public override int Read(byte[] buffer, int offset, int count)
            {
                ReadOperations++;
                return base.Read(buffer, offset, count);
            }

            public override int ReadByte()
            {
                ReadOperations++;
                return base.ReadByte();
            }
        }
    }
}
