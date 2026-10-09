// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO/Mcap/Recording
// Purpose: High-level MCAP recorder that wraps McapWriter. Handles chunk
// management, schema/channel deduplication, metadata indexes, compression,
// and final summary/statistics output on close. Attaches to FoxgloveSession
// via dual-write hooks so live publish data is simultaneously recorded.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Util;
using ZstdSharp;

namespace Unity.FoxgloveSDK.IO
{
    public partial class McapRecorder
    {
        /// <summary>
        /// Finalize the MCAP file: flush the last chunk, write summary groups,
        /// footer, and magic suffix. Calling <see cref="Dispose"/> after a
        /// successful close is safe and does not write additional MCAP bytes.
        /// </summary>
        public void Close()
        {
            lock (_lock)
            {
                if (_closed) return;

                if (_failedChunkStartPosition.HasValue)
                {
                    var failure = _chunkFlushFailure ??
                                  new IOException("An earlier MCAP chunk flush failed.");
                    if (TryRecoverAfterFailedChunkFlush(_failedChunkStartPosition.Value, failure))
                    {
                        _closed = true;
                        return;
                    }

                    _closed = true;
                    throw new IOException("MCAP recorder could not recover after an earlier chunk flush failure.", failure);
                }

                var flushStartPosition = _writer.Position;
                try
                {
                    FlushChunk();
                }
                catch (Exception ex)
                {
                    var failedChunkStart = _failedChunkStartPosition ?? flushStartPosition;
                    if (TryRecoverAfterFailedChunkFlush(failedChunkStart, ex))
                    {
                        _closed = true;
                        return;
                    }

                    _closed = true;
                    throw;
                }

                try
                {
                    var dataSectionCrc = _options.EnableDataCrcs
                        ? _writer.ComputeCrc32FromStartToCurrent()
                        : 0;
                    _writer.WriteDataEnd(dataSectionCrc);

                    McapSummarySerializer.WriteSummaryAndFooter(
                        _writer,
                        BuildFinalSummary(includeStatistics: true),
                        _options.UseSummaryOffsets,
                        _options.EnableCrcs);
                    _writer.WriteMagic();
                    _writer.Flush();
                }
                finally
                {
                    _closed = true;
                }
            }
        }

        private McapFileSummary BuildFinalSummary(bool includeStatistics)
        {
            var summary = new McapFileSummary();
            if (_options.RepeatSchemas)
            {
                foreach (var schema in _schemas)
                {
                    summary.Schemas.Add(new McapSchema
                    {
                        Id = schema.Id,
                        Name = schema.Name,
                        Encoding = schema.Encoding,
                        Data = schema.Data
                    });
                }
            }

            if (_options.RepeatChannels)
            {
                foreach (var channel in _channels)
                {
                    summary.Channels.Add(new McapChannel
                    {
                        Id = channel.Id,
                        SchemaId = channel.SchemaId,
                        Topic = channel.Topic,
                        MessageEncoding = channel.Encoding,
                        Metadata = channel.Metadata ?? CreateEmptyChannelMetadata()
                    });
                }
            }

            if (includeStatistics && _options.UseStatistics)
            {
                summary.Statistics = new McapStatistics
                {
                    MessageCount = _msgCount,
                    SchemaCount = (ushort)_schemas.Count,
                    ChannelCount = (uint)_channels.Count,
                    AttachmentCount = _attachmentCount,
                    MetadataCount = _metadataCount,
                    ChunkCount = (uint)_chunkCount,
                    MessageStartTime = _msgCount > 0 ? _msgSt : 0,
                    MessageEndTime = _msgCount > 0 ? _msgEt : 0,
                    ChannelMessageCounts = BuildChannelMessageCounts()
                };
            }

            if (_options.HasIndex(McapIndexTypes.Metadata))
            {
                foreach (var metadata in _metaIdx)
                {
                    summary.MetadataIndexes.Add(new McapMetadataIndex
                    {
                        Offset = metadata.Offset,
                        Length = metadata.Length,
                        Name = metadata.Name
                    });
                }
            }

            if (_options.HasIndex(McapIndexTypes.Attachment))
                summary.AttachmentIndexes.AddRange(_attachmentIdx);

            if (_options.UseChunking && _options.HasIndex(McapIndexTypes.Chunk))
            {
                foreach (var chunk in _chunkIdx)
                {
                    summary.ChunkIndexes.Add(new McapChunkIndex
                    {
                        MessageStartTime = chunk.StartTime,
                        MessageEndTime = chunk.EndTime,
                        ChunkStartOffset = chunk.Offset,
                        ChunkLength = chunk.Length,
                        MessageIndexOffsets = chunk.MessageIndexOffsets,
                        MessageIndexLength = chunk.MessageIndexLength,
                        Compression = chunk.Compression,
                        CompressedSize = chunk.CompressedSize,
                        UncompressedSize = chunk.UncompressedSize
                    });
                }
            }

            return summary;
        }

        private void WriteRecoverableTrailerAfterDroppedChunk()
        {
            _writer.WriteDataEnd(0);
            McapSummarySerializer.WriteSummaryAndFooter(
                _writer,
                BuildFinalSummary(includeStatistics: false),
                _options.UseSummaryOffsets,
                _options.EnableCrcs);
            _writer.WriteMagic();
            _writer.Flush();
        }

        private bool TryRecoverAfterFailedChunkFlush(long flushStartPosition, Exception flushError)
        {
            if (!_writer.CanSeek)
                return false;

            try
            {
                _writer.TruncateToPosition(flushStartPosition);

                _log.LogWarning(
                    $"MCAP recorder dropped an incomplete chunk; writing a recoverable indexed trailer without final statistics: {flushError.Message}");
                WriteRecoverableTrailerAfterDroppedChunk();
                return true;
            }
            catch (Exception recoveryError)
            {
                _log.LogWarning(
                    $"MCAP recorder could not recover after a failed final chunk flush; file may be incomplete: {recoveryError.Message}");
                return false;
            }
        }

        /// <summary>
        /// Dispose the recorder and underlying writer and buffer streams.
        /// </summary>
        public void Dispose()
        {
            try
            {
                Close();
            }
            catch (Exception ex)
            {
                _log.LogWarning($"MCAP recorder close failed during dispose; file may be incomplete: {ex.Message}");
                lock (_lock)
                {
                    _closed = true;
                }
            }

            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;
                try
                {
                    _writer.Dispose();
                }
                catch (Exception ex)
                {
                    _log.LogWarning($"MCAP recorder writer dispose failed during shutdown: {ex.Message}");
                }

                try
                {
                    _chunkBuf.Dispose();
                }
                catch (Exception ex)
                {
                    _log.LogWarning($"MCAP recorder chunk buffer dispose failed during shutdown: {ex.Message}");
                }

                try
                {
                    _compressionBuf.Dispose();
                }
                catch (Exception ex)
                {
                    _log.LogWarning($"MCAP recorder compression buffer dispose failed during shutdown: {ex.Message}");
                }

                try
                {
                    _zstdCompressor?.Dispose();
                }
                catch (Exception ex)
                {
                    _log.LogWarning($"MCAP recorder zstd compressor dispose failed during shutdown: {ex.Message}");
                }

                _zstdCompressor = null;
                _zstdCompressionBuffer = null;
            }
        }

        // Helpers
        // Caller must hold _lock. The returned list is an instance scratch buffer
        // and must not be retained after the locked operation finishes.
        private List<ChannelWriteState> FillAndGetScratchChannelWriteStates()
        {
            System.Diagnostics.Debug.Assert(Monitor.IsEntered(_lock));
            _seenChannelIds.Clear();
            _allChannelWriteStates.Clear();
            foreach (var m in _serverChannelWriteStates.Values)
            {
                if (_seenChannelIds.Add(m.McapId))
                    _allChannelWriteStates.Add(m);
            }

            foreach (var m in _clientChannelWriteState.Values)
            {
                if (_seenChannelIds.Add(m.McapId))
                    _allChannelWriteStates.Add(m);
            }

            return _allChannelWriteStates;
        }

    }
}
