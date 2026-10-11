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
        /// Write the accumulated chunk buffer to the MCAP stream, then flush
        /// per-channel message indexes following the chunk.
        /// </summary>
        void FlushChunk()
        {
            if (!_options.UseChunking) return;
            if (_chunkBuf.Length == 0) return;
            var flushStartPosition = _writer.Position;
            try
            {
                if (!_chunkBuf.TryGetBuffer(out var raw))
                    throw new InvalidOperationException("MCAP chunk buffer is not publicly visible.");
                var rawCrc = _options.EnableCrcs
                    ? Util.Crc32Helper.Compute(new ReadOnlySpan<byte>(raw.Array, raw.Offset, raw.Count))
                    : 0;
                var zstdCompressor = _compression == "zstd"
                    ? _zstdCompressor ??= new Compressor()
                    : null;
                var compressed = McapCompression.Compress(
                    _compression,
                    raw,
                    _options.Lz4CompressionLevel,
                    _compressionBuf,
                    zstdCompressor,
                    ref _zstdCompressionBuffer);
                var off = (ulong)_writer.Position;
                _writer.WriteChunk(_chunkSt, _chunkEt, (ulong)raw.Count, rawCrc, _compression, (ulong)compressed.Count, compressed);
                var chunkLen = (ulong)_writer.Position - off;
                var channelStates = FillAndGetScratchChannelWriteStates();
                var mio = _messageIndexOffsetsScratch;
                mio.Clear();
                ulong mioTLen = 0;
                foreach (var map in channelStates)
                {
                    if (map.Pending.Count == 0) continue;
                    if (_options.HasIndex(McapIndexTypes.Message))
                    {
                        var start = (ulong)_writer.Position;
                        _writer.WriteMessageIndex(map.McapId, map.Pending);
                        var len = (ulong)_writer.Position - start;
                        mio[map.McapId] = start;
                        mioTLen += len;
                    }
                }
                if (_options.HasIndex(McapIndexTypes.Chunk))
                {
                    _chunkIdx.Add(new ChunkIndexState
                    {
                        StartTime = _chunkSt,
                        EndTime = _chunkEt,
                        Offset = off,
                        Length = chunkLen,
                        MessageIndexOffsets = new Dictionary<ushort, ulong>(mio),
                        MessageIndexLength = mioTLen,
                        Compression = _compression,
                        CompressedSize = (ulong)compressed.Count,
                        UncompressedSize = (ulong)raw.Count
                    });
                }
                _chunkCount++;
                ResetActiveChunkState(channelStates);
            }
            catch (Exception ex)
            {
                _failedChunkStartPosition ??= flushStartPosition;
                _chunkFlushFailure ??= ex;
                ResetActiveChunkState();
                Fail("Chunk flush failed: " + ex.Message);
                throw;
            }
        }

        private void ResetActiveChunkState(List<ChannelWriteState> channelStates = null)
        {
            _chunkBuf.SetLength(0);
            foreach (var map in channelStates ?? FillAndGetScratchChannelWriteStates())
                map.Pending.Clear();
            _chunkSt = 0;
            _chunkEt = 0;
            _chunkHasMessages = false;
        }

        private void FlushChunkBeforeLargeWriteIfNeeded(int nextRecordLength)
        {
            if (_chunkBuf.Length > 0 && _chunkBuf.Length + nextRecordLength >= _chunkSz)
                FlushChunk();
        }

        internal static bool IsChunkedMessageRecordWithinLimit(
            int payloadLength,
            ulong chunkUncompressedSizeLimit)
        {
            if (payloadLength < 0)
                return false;

            const ulong messageRecordOverhead =
                McapWriter.RecordHeaderLength + 2UL + 4UL + 8UL + 8UL;
            return (ulong)payloadLength <= chunkUncompressedSizeLimit
                   && (ulong)payloadLength + messageRecordOverhead <= chunkUncompressedSizeLimit;
        }

        private void HandleTopLevelRecordWriteFailure(
            long recordStartPosition,
            Exception writeError,
            string recordName)
        {
            try
            {
                _writer.TruncateToPosition(recordStartPosition);
            }
            catch (Exception rollbackError)
            {
                _closed = true;
                Fail($"{recordName} write and rollback failed: {rollbackError.Message}");
                throw new IOException(
                    $"MCAP recorder could not roll back an incomplete {recordName} record.",
                    new AggregateException(writeError, rollbackError));
            }

            // A top-level record failure has already been rolled back to its
            // record boundary. Keep chunk-failure recovery reserved for an
            // actual FlushChunk failure so Close() still flushes any valid
            // messages buffered before this unrelated record was attempted.
            Fail($"{recordName} write failed: {writeError.Message}");
        }

        /// <summary>
        /// Mark recording as permanently failed and log an error.
        /// </summary>
        void Fail(string msg) { _recordingFailed = true; _log.LogError($"MCAP: {msg}"); }

        private Dictionary<ushort, ulong> BuildChannelMessageCounts()
        {
            var channelStates = FillAndGetScratchChannelWriteStates();
            var counts = new Dictionary<ushort, ulong>(channelStates.Count);
            foreach (var state in channelStates)
                counts[state.McapId] = state.MsgCount;
            return counts;
        }

        private Dictionary<string, string> CreateChannelMetadata(McapChannelDirection direction)
        {
            var coordinateMode = direction == McapChannelDirection.Output
                ? OutputCoordinateMode
                : InputCoordinateMode;
            var metadata = new Dictionary<string, string>
            {
                [DataDirectionMetadataKey] = direction == McapChannelDirection.Output ? "output" : "input"
            };
            if (!string.IsNullOrEmpty(coordinateMode))
                metadata[CoordinateModeMetadataKey] = coordinateMode;
            return metadata;
        }

        private static Dictionary<string, string> SnapshotChannelMetadata(Dictionary<string, string> metadata)
        {
            return metadata == null || metadata.Count == 0
                ? CreateEmptyChannelMetadata()
                : new Dictionary<string, string>(metadata);
        }

        private static Dictionary<string, string> CreateEmptyChannelMetadata() => new Dictionary<string, string>();

    }
}
