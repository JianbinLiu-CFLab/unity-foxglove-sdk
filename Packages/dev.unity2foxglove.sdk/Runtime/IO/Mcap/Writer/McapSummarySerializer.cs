// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO/Mcap
// Purpose: MCAP summary/footer serializer used by recorder finalization and amendment paths.

using System;
using System.IO;
using Unity.FoxgloveSDK.Util;

namespace Unity.FoxgloveSDK.IO
{
    internal static class McapSummarySerializer
    {
        public static void WriteSummaryAndFooter(
            McapWriter destination,
            McapFileSummary summary,
            bool writeSummaryOffsets,
            bool enableSummaryCrc)
        {
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));

            var summaryStart = (ulong)destination.Position;
            using var summaryBuilder = new MemoryStream();
            using var summaryWriter = new McapWriter(summaryBuilder, leaveOpen: true);

            var schemaGroup = WriteGroup(summaryBuilder, () =>
            {
                for (var i = 0; i < summary.Schemas.Count; i++)
                {
                    var schema = summary.Schemas[i];
                    summaryWriter.WriteSchema(schema.Id, schema.Name, schema.Encoding, schema.Data);
                }
            });

            var channelGroup = WriteGroup(summaryBuilder, () =>
            {
                for (var i = 0; i < summary.Channels.Count; i++)
                {
                    var channel = summary.Channels[i];
                    summaryWriter.WriteChannel(
                        channel.Id,
                        channel.SchemaId,
                        channel.Topic,
                        channel.MessageEncoding,
                        channel.Metadata);
                }
            });

            var statisticsGroup = WriteGroup(summaryBuilder, () =>
            {
                var statistics = summary.Statistics;
                if (statistics != null)
                {
                    summaryWriter.WriteStatistics(
                        statistics.MessageCount,
                        statistics.SchemaCount,
                        statistics.ChannelCount,
                        statistics.AttachmentCount,
                        statistics.MetadataCount,
                        statistics.ChunkCount,
                        statistics.MessageStartTime,
                        statistics.MessageEndTime,
                        statistics.ChannelMessageCounts);
                }
            });

            var metadataGroup = WriteGroup(summaryBuilder, () =>
            {
                for (var i = 0; i < summary.MetadataIndexes.Count; i++)
                {
                    var index = summary.MetadataIndexes[i];
                    summaryWriter.WriteMetadataIndex(index.Offset, index.Length, index.Name);
                }
            });

            var attachmentGroup = WriteGroup(summaryBuilder, () =>
            {
                for (var i = 0; i < summary.AttachmentIndexes.Count; i++)
                    summaryWriter.WriteAttachmentIndex(summary.AttachmentIndexes[i]);
            });

            var chunkGroup = WriteGroup(summaryBuilder, () =>
            {
                for (var i = 0; i < summary.ChunkIndexes.Count; i++)
                {
                    var chunk = summary.ChunkIndexes[i];
                    summaryWriter.WriteChunkIndex(
                        chunk.MessageStartTime,
                        chunk.MessageEndTime,
                        chunk.ChunkStartOffset,
                        chunk.ChunkLength,
                        chunk.MessageIndexOffsets,
                        chunk.MessageIndexLength,
                        chunk.Compression,
                        chunk.CompressedSize,
                        chunk.UncompressedSize);
                }
            });

            var summaryOffsetStart = 0UL;
            if (writeSummaryOffsets)
            {
                summaryOffsetStart = summaryStart + (ulong)summaryBuilder.Position;
                WriteSummaryOffset(summaryWriter, summaryStart, McapWriter.OpcodeSchema, schemaGroup);
                WriteSummaryOffset(summaryWriter, summaryStart, McapWriter.OpcodeChannel, channelGroup);
                WriteSummaryOffset(summaryWriter, summaryStart, McapWriter.OpcodeStatistics, statisticsGroup);
                WriteSummaryOffset(summaryWriter, summaryStart, McapWriter.OpcodeMetadataIndex, metadataGroup);
                WriteSummaryOffset(summaryWriter, summaryStart, McapWriter.OpcodeAttachmentIndex, attachmentGroup);
                WriteSummaryOffset(summaryWriter, summaryStart, McapWriter.OpcodeChunkIndex, chunkGroup);
            }

            summaryWriter.Flush();
            if (!summaryBuilder.TryGetBuffer(out var summaryData))
                throw new InvalidOperationException("MCAP summary buffer is not publicly visible.");

            var hasSummary = summaryData.Count > 0;
            var footerSummaryStart = hasSummary ? summaryStart : 0UL;
            if (!hasSummary || !writeSummaryOffsets)
                summaryOffsetStart = 0;

            var footerPrefix = McapWriter.BuildFooterCrcPrefix(footerSummaryStart, summaryOffsetStart);
            var summaryCrc = 0u;
            if (enableSummaryCrc)
            {
                var crc = Crc32Helper.Initialize();
                crc = Crc32Helper.Update(
                    crc,
                    new ReadOnlySpan<byte>(summaryData.Array, summaryData.Offset, summaryData.Count));
                crc = Crc32Helper.Update(crc, footerPrefix);
                summaryCrc = Crc32Helper.Finalize(crc);
            }

            destination.WriteBytes(summaryData);
            destination.WriteFooter(footerSummaryStart, summaryOffsetStart, summaryCrc);
        }

        private static (ulong Start, ulong Length) WriteGroup(MemoryStream stream, Action emit)
        {
            var start = (ulong)stream.Position;
            emit();
            return (start, (ulong)stream.Position - start);
        }

        private static void WriteSummaryOffset(
            McapWriter writer,
            ulong summaryStart,
            byte opcode,
            (ulong Start, ulong Length) group)
        {
            if (group.Length > 0)
                writer.WriteSummaryOffset(opcode, summaryStart + group.Start, group.Length);
        }
    }
}
