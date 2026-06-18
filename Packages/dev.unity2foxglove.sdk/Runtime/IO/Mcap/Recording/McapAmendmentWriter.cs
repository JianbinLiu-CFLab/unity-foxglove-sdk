// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO/Mcap/Recording
// Purpose: Post-recording MCAP metadata and attachment amendment writer.

using System;
using System.Collections.Generic;
using System.IO;
using Unity.FoxgloveSDK.Util;

namespace Unity.FoxgloveSDK.IO
{
    public sealed class McapAmendmentWriter : IDisposable
    {
        private readonly FileStream _stream;
        private readonly McapWriter _writer;
        private readonly McapFileSummary _summary;
        private readonly McapTrailerInfo _trailer;
        private readonly List<PendingMetadata> _metadata = new List<PendingMetadata>();
        private readonly List<PendingAttachment> _attachments = new List<PendingAttachment>();
        private bool _closed;
        private bool _disposed;

        public McapAmendmentWriter(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentException("MCAP file path is required.", nameof(filePath));

            _stream = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            try
            {
                var reader = new McapReader(_stream);
                _summary = reader.ReadSummary();
                _trailer = reader.ReadTrailerInfo();
                _writer = new McapWriter(_stream, leaveOpen: true);
            }
            catch
            {
                _stream.Dispose();
                throw;
            }
        }

        public void AddAttachment(
            string name,
            string mediaType,
            byte[] data,
            ulong logTimeNs,
            ulong createTimeNs = 0)
        {
            ThrowIfClosedOrDisposed();
            _attachments.Add(new PendingAttachment
            {
                Name = name ?? string.Empty,
                MediaType = mediaType ?? string.Empty,
                Data = data == null ? Array.Empty<byte>() : (byte[])data.Clone(),
                LogTimeNs = logTimeNs,
                CreateTimeNs = createTimeNs
            });
        }

        public void AddMetadata(string name, Dictionary<string, string> metadata)
        {
            ThrowIfClosedOrDisposed();
            _metadata.Add(new PendingMetadata
            {
                Name = name ?? string.Empty,
                Metadata = metadata == null
                    ? new Dictionary<string, string>()
                    : new Dictionary<string, string>(metadata, StringComparer.Ordinal)
            });
        }

        public void Close()
        {
            ThrowIfDisposed();
            if (_closed)
                return;

            if (_metadata.Count == 0 && _attachments.Count == 0)
            {
                _stream.Flush();
                _closed = true;
                return;
            }

            _stream.Seek(ToSeekOffset(_trailer.DataEndOffset, "DataEnd"), SeekOrigin.Begin);
            _stream.SetLength(ToSeekOffset(_trailer.DataEndOffset, "DataEnd"));

            var newMetadataIndexes = new List<McapMetadataIndex>();
            for (var i = 0; i < _metadata.Count; i++)
            {
                var item = _metadata[i];
                var offset = (ulong)_writer.Position;
                _writer.WriteMetadata(item.Name, item.Metadata);
                var length = (ulong)_writer.Position - offset;
                newMetadataIndexes.Add(new McapMetadataIndex
                {
                    Offset = offset,
                    Length = length,
                    Name = item.Name
                });
            }

            var newAttachmentIndexes = new List<McapAttachmentIndex>();
            for (var i = 0; i < _attachments.Count; i++)
            {
                var item = _attachments[i];
                newAttachmentIndexes.Add(_writer.WriteAttachment(
                    item.LogTimeNs,
                    item.CreateTimeNs,
                    item.Name,
                    item.MediaType,
                    item.Data));
            }

            _writer.WriteDataEnd(0);
            WriteSummaryAndFooter(newMetadataIndexes, newAttachmentIndexes);
            _writer.WriteMagic();
            _writer.Flush();
            _closed = true;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            try
            {
                if (!_closed)
                    Close();
            }
            finally
            {
                try
                {
                    _writer?.Dispose();
                }
                finally
                {
                    _stream.Dispose();
                    _disposed = true;
                }
            }
        }

        private void WriteSummaryAndFooter(
            IReadOnlyList<McapMetadataIndex> newMetadataIndexes,
            IReadOnlyList<McapAttachmentIndex> newAttachmentIndexes)
        {
            var summaryStart = (ulong)_writer.Position;
            using var summaryBuilder = new MemoryStream();
            using var summaryWriter = new McapWriter(summaryBuilder, leaveOpen: true);

            var schemaGroupStart = (ulong)summaryBuilder.Position;
            for (var i = 0; i < _summary.Schemas.Count; i++)
            {
                var schema = _summary.Schemas[i];
                summaryWriter.WriteSchema(schema.Id, schema.Name, schema.Encoding, schema.Data);
            }
            var schemaGroupLength = (ulong)summaryBuilder.Position - schemaGroupStart;

            var channelGroupStart = (ulong)summaryBuilder.Position;
            for (var i = 0; i < _summary.Channels.Count; i++)
            {
                var channel = _summary.Channels[i];
                summaryWriter.WriteChannel(
                    channel.Id,
                    channel.SchemaId,
                    channel.Topic,
                    channel.MessageEncoding,
                    channel.Metadata ?? new Dictionary<string, string>());
            }
            var channelGroupLength = (ulong)summaryBuilder.Position - channelGroupStart;

            var statsGroupStart = (ulong)summaryBuilder.Position;
            var statistics = _summary.Statistics;
            if (statistics != null)
            {
                summaryWriter.WriteStatistics(
                    statistics.MessageCount,
                    (ushort)_summary.Schemas.Count,
                    (uint)_summary.Channels.Count,
                    checked(statistics.AttachmentCount + (uint)newAttachmentIndexes.Count),
                    checked(statistics.MetadataCount + (uint)newMetadataIndexes.Count),
                    statistics.ChunkCount,
                    statistics.MessageStartTime,
                    statistics.MessageEndTime,
                    statistics.ChannelMessageCounts ?? new Dictionary<ushort, ulong>());
            }
            var statsGroupLength = (ulong)summaryBuilder.Position - statsGroupStart;

            var allMetadataIndexes = new List<McapMetadataIndex>(_summary.MetadataIndexes.Count + newMetadataIndexes.Count);
            allMetadataIndexes.AddRange(_summary.MetadataIndexes);
            allMetadataIndexes.AddRange(newMetadataIndexes);
            var metadataGroupStart = (ulong)summaryBuilder.Position;
            for (var i = 0; i < allMetadataIndexes.Count; i++)
            {
                var index = allMetadataIndexes[i];
                summaryWriter.WriteMetadataIndex(index.Offset, index.Length, index.Name);
            }
            var metadataGroupLength = (ulong)summaryBuilder.Position - metadataGroupStart;

            var allAttachmentIndexes = new List<McapAttachmentIndex>(_summary.AttachmentIndexes.Count + newAttachmentIndexes.Count);
            allAttachmentIndexes.AddRange(_summary.AttachmentIndexes);
            allAttachmentIndexes.AddRange(newAttachmentIndexes);
            var attachmentGroupStart = (ulong)summaryBuilder.Position;
            for (var i = 0; i < allAttachmentIndexes.Count; i++)
                summaryWriter.WriteAttachmentIndex(allAttachmentIndexes[i]);
            var attachmentGroupLength = (ulong)summaryBuilder.Position - attachmentGroupStart;

            var chunkGroupStart = (ulong)summaryBuilder.Position;
            for (var i = 0; i < _summary.ChunkIndexes.Count; i++)
            {
                var chunk = _summary.ChunkIndexes[i];
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
            var chunkGroupLength = (ulong)summaryBuilder.Position - chunkGroupStart;

            var summaryOffsetStart = summaryStart + (ulong)summaryBuilder.Position;
            if (schemaGroupLength > 0)
                summaryWriter.WriteSummaryOffset(McapWriter.OpcodeSchema, summaryStart + schemaGroupStart, schemaGroupLength);
            if (channelGroupLength > 0)
                summaryWriter.WriteSummaryOffset(McapWriter.OpcodeChannel, summaryStart + channelGroupStart, channelGroupLength);
            if (statsGroupLength > 0)
                summaryWriter.WriteSummaryOffset(McapWriter.OpcodeStatistics, summaryStart + statsGroupStart, statsGroupLength);
            if (metadataGroupLength > 0)
                summaryWriter.WriteSummaryOffset(McapWriter.OpcodeMetadataIndex, summaryStart + metadataGroupStart, metadataGroupLength);
            if (attachmentGroupLength > 0)
                summaryWriter.WriteSummaryOffset(McapWriter.OpcodeAttachmentIndex, summaryStart + attachmentGroupStart, attachmentGroupLength);
            if (chunkGroupLength > 0)
                summaryWriter.WriteSummaryOffset(McapWriter.OpcodeChunkIndex, summaryStart + chunkGroupStart, chunkGroupLength);

            summaryWriter.Flush();
            if (!summaryBuilder.TryGetBuffer(out var summaryData))
                throw new InvalidOperationException("MCAP summary buffer is not publicly visible.");

            var footerPrefix = McapWriter.BuildFooterCrcPrefix(summaryStart, summaryOffsetStart);
            var crc = Crc32Helper.Initialize();
            crc = Crc32Helper.Update(crc, new ReadOnlySpan<byte>(summaryData.Array, summaryData.Offset, summaryData.Count));
            crc = Crc32Helper.Update(crc, footerPrefix);
            var summaryCrc = Crc32Helper.Finalize(crc);

            _writer.WriteBytes(summaryData);
            _writer.WriteFooter(summaryStart, summaryOffsetStart, summaryCrc);
        }

        private void ThrowIfClosedOrDisposed()
        {
            ThrowIfDisposed();
            if (_closed)
                throw new InvalidOperationException("MCAP amendment writer is already closed.");
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(McapAmendmentWriter));
        }

        private static long ToSeekOffset(ulong offset, string context)
        {
            if (offset > long.MaxValue)
                throw new InvalidDataException($"MCAP {context} offset {offset} exceeds seekable range.");

            return (long)offset;
        }

        private sealed class PendingMetadata
        {
            public string Name;
            public Dictionary<string, string> Metadata;
        }

        private sealed class PendingAttachment
        {
            public string Name;
            public string MediaType;
            public byte[] Data;
            public ulong LogTimeNs;
            public ulong CreateTimeNs;
        }
    }
}
