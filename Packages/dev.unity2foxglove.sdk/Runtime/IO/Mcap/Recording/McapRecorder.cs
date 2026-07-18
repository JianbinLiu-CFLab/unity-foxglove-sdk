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
using System.Threading;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Util;
using ZstdSharp;

namespace Unity.FoxgloveSDK.IO
{
    /// <summary>
    /// MCAP recorder that attaches to a FoxgloveSession via dual-write hooks.
    /// Manages chunk lifecycle, schema/channel deduplication, metadata
    /// indexes, and final summary/statistics output on close.
    /// </summary>
    public partial class McapRecorder : IDisposable
    {
        private readonly McapWriter _writer;
        private readonly IFoxgloveLogger _log;
        private readonly McapWriterOptions _options;
        private readonly string _compression;
        private readonly Dictionary<(string name, string enc, string hash), ushort> _schemaIdsBySignature = new();
        private readonly Dictionary<(uint clientId, uint chId), ChannelWriteState> _clientChannelWriteState = new();
        private readonly HashSet<(uint clientId, uint chId)> _skippedClientChannels = new();
        private readonly Dictionary<uint, ChannelWriteState> _serverChannelWriteStates = new();
        private readonly Dictionary<(string topic, McapChannelDirection direction), ChannelWriteState> _topicChannelWriteState = new();
        private readonly Dictionary<string, TopicSignature> _topicSignatures = new();
        private readonly HashSet<ushort> _seenChannelIds = new();
        private readonly List<ChannelWriteState> _allChannelWriteStates = new();
        private readonly Dictionary<ushort, ulong> _messageIndexOffsetsScratch = new();
        private readonly List<SchemaRecordState> _schemas = new();
        private readonly List<ChannelRecordState> _channels = new();
        private readonly List<ChunkIndexState> _chunkIdx = new();
        private readonly List<MetadataIndexState> _metaIdx = new();
        private readonly List<McapAttachmentIndex> _attachmentIdx = new();
        private uint _attachmentCount;
        private readonly MemoryStream _chunkBuf;
        private readonly MemoryStream _compressionBuf = new();
        private readonly byte[] _messageRecordHeader = new byte[McapWriter.RecordHeaderLength + 2 + 4 + 8 + 8];
        private readonly object _lock = new object();
        private Compressor _zstdCompressor;
        private byte[] _zstdCompressionBuffer;
        private ushort _nextSchemaId = 1;
        private ushort _nextChannelId = 1;
        private ulong _chunkSt, _chunkEt;
        private ulong _msgSt = ulong.MaxValue, _msgEt;
        private ulong _msgCount, _chunkCount;
        private uint _metadataCount;
        private bool _chunkHasMessages;
        private bool _closed, _recordingFailed, _disposed;
        private long? _failedChunkStartPosition;
        private Exception _chunkFlushFailure;
        private readonly int _chunkSz;

        /// <summary>
        /// Default chunk size in bytes (1 MiB).
        /// </summary>
        public const int DefaultChunkSizeBytes = McapWriterOptions.DefaultChunkSizeBytes;

        /// <summary>
        /// Creates a new MCAP recorder writing to the given stream.
        /// Optional compression controls per-chunk compression (e.g. "zstd").
        /// </summary>
        public McapRecorder(Stream stream, IFoxgloveLogger logger = null, int chunkSizeBytes = DefaultChunkSizeBytes, string compression = "", bool leaveOpen = true)
            : this(stream, logger, new McapWriterOptions { ChunkSizeBytes = chunkSizeBytes, Compression = compression }, leaveOpen)
        {
        }

        /// <summary>
        /// Creates a new MCAP recorder with advanced writer options.
        /// </summary>
        public McapRecorder(Stream stream, IFoxgloveLogger logger, McapWriterOptions options, bool leaveOpen = true)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));
            if (!stream.CanSeek)
                throw new NotSupportedException("MCAP recorder requires a seekable output stream.");

            _log = logger ?? new ConsoleLogger();
            _options = McapWriterOptions.Normalize(options);
            _writer = new McapWriter(stream, leaveOpen);
            _chunkSz = _options.ChunkSizeBytes;
            _chunkBuf = new MemoryStream(_chunkSz);
            _compression = _options.Compression;
            try
            {
                _writer.WriteMagic();
                _writer.WriteHeader("", "unity-foxglove-sdk");
            }
            catch
            {
                _writer.Dispose();
                throw;
            }
        }

        /// <summary>Metadata key for the external coordinate convention of a channel payload.</summary>
        public const string CoordinateModeMetadataKey = "coordinate_mode";

        /// <summary>Metadata key for the data direction represented by a channel payload.</summary>
        public const string DataDirectionMetadataKey = "unity2foxglove.direction";

        /// <summary>
        /// Compatibility coordinate setting. Reading returns the output convention;
        /// assigning it configures both directions for legacy one-value callers.
        /// </summary>
        public string CoordinateMode
        {
            get => OutputCoordinateMode;
            set
            {
                OutputCoordinateMode = value;
                InputCoordinateMode = value;
            }
        }

        /// <summary>Coordinate convention of new Unity-to-external output channels.</summary>
        public string OutputCoordinateMode { get; set; }

        /// <summary>Coordinate convention of new external-to-Unity input channels.</summary>
        public string InputCoordinateMode { get; set; }

        /// <summary>
        /// Register a server-side channel and write its MCAP channel record immediately.
        /// </summary>
        public void AddChannel(uint fId, string topic, string enc, string sName, string sEnc, string sContent)
        {
            AddChannelCore(fId, topic, enc, sName, sEnc, sContent, null);
        }

        internal void AddChannelPreservingMcapId(uint fId, ushort mcapChannelId, string topic, string enc, string sName, string sEnc, string sContent)
        {
            AddChannelCore(fId, topic, enc, sName, sEnc, sContent, mcapChannelId);
        }

        private void AddChannelCore(uint fId, string topic, string enc, string sName, string sEnc, string sContent, ushort? explicitMcapChannelId)
        {
            lock (_lock)
            {
                if (_recordingFailed || _closed) return;
                if (_serverChannelWriteStates.ContainsKey(fId))
                {
                    _log.LogWarning(
                        $"MCAP: ignoring duplicate server channel id {fId} for topic '{topic}' because the channel id is already registered.");
                    return;
                }

                var normalizedEnc = NormalizeMessageEncoding(enc);
                var signature = CreateTopicSignature(normalizedEnc, sName, sEnc, sContent);
                if (WouldMixTopicSignature(topic, signature))
                {
                    _log.LogWarning(
                        $"MCAP: skipping server channel for topic '{topic}' because its signature is incompatible with an existing recorded channel.");
                    return;
                }
                var sid = GetOrCreateSchema(sName, sEnc, sContent);
                if (_recordingFailed) return;
                ushort mCid;
                if (explicitMcapChannelId.HasValue)
                {
                    mCid = explicitMcapChannelId.Value;
                    if (mCid == 0 || IsMcapChannelIdRegistered(mCid))
                    {
                        Fail("MCAP channel ID collision");
                        return;
                    }

                    if (mCid >= _nextChannelId)
                        _nextChannelId = (ushort)(mCid == ushort.MaxValue ? 0 : mCid + 1);
                }
                else
                {
                    if (_nextChannelId == 0) { Fail("Channel ID overflow"); return; }
                    mCid = _nextChannelId++;
                }
                var state = new ChannelWriteState { McapId = mCid, SchemaId = sid, Topic = topic };
                _serverChannelWriteStates[fId] = state;
                if (!_topicChannelWriteState.ContainsKey((topic, McapChannelDirection.Output)))
                    _topicChannelWriteState[(topic, McapChannelDirection.Output)] = state;

                var meta = CreateChannelMetadata(McapChannelDirection.Output);
                _writer.WriteChannel(mCid, sid, topic, normalizedEnc, meta);
                _channels.Add(new ChannelRecordState { Id = mCid, SchemaId = sid, Topic = topic, Encoding = normalizedEnc, Metadata = SnapshotChannelMetadata(meta) });
                RecordTopicSignature(topic, signature);
            }
        }

        private bool IsMcapChannelIdRegistered(ushort mcapChannelId)
        {
            foreach (var state in _serverChannelWriteStates.Values)
                if (state.McapId == mcapChannelId)
                    return true;
            foreach (var state in _clientChannelWriteState.Values)
                if (state.McapId == mcapChannelId)
                    return true;
            return false;
        }

        /// <summary>
        /// Write a client-published message to the current chunk, lazily creating
        /// the channel record on first use.
        /// </summary>
        public void WriteClientMessage(uint clientId, uint chId, ulong logNs, byte[] payload, string topic,
            string enc = "json", string sName = "", string sEnc = "", string sContent = "")
        {
            lock (_lock)
            {
                if (_recordingFailed || _closed) return;
                var key = (clientId, chId);
                if (_skippedClientChannels.Contains(key)) return;
                if (!_clientChannelWriteState.TryGetValue(key, out var map))
                {
                    var messageEncoding = NormalizeMessageEncoding(enc);
                    var signature = CreateTopicSignature(messageEncoding, sName, sEnc, sContent);
                    if (TryReuseExistingTopicChannel(
                            topic,
                            McapChannelDirection.Input,
                            signature,
                            sContent,
                            out map))
                    {
                        _clientChannelWriteState[key] = map;
                    }
                    else
                    {
                        if (WouldMixTopicSignature(topic, signature)
                            && !TryGetCompatibleTopicSchemaId(topic, signature, sContent, out _))
                        {
                            _skippedClientChannels.Add(key);
                            _log.LogWarning(
                                $"MCAP: skipping client-published topic '{topic}' because its schema signature is incompatible with an existing recorded channel.");
                            return;
                        }

                        var sid = TryGetCompatibleTopicSchemaId(topic, signature, sContent, out var compatibleSchemaId)
                            ? compatibleSchemaId
                            : GetOrCreateSchema(sName, sEnc, sContent);
                        if (_recordingFailed) return;
                        if (_nextChannelId == 0) { Fail("Channel ID overflow"); return; }
                        var mcapId = _nextChannelId++;
                        map = new ChannelWriteState { McapId = mcapId, SchemaId = sid, Topic = topic };
                        _clientChannelWriteState[key] = map;
                        if (!_topicChannelWriteState.ContainsKey((topic, McapChannelDirection.Input)))
                            _topicChannelWriteState[(topic, McapChannelDirection.Input)] = map;
                        var meta = CreateChannelMetadata(McapChannelDirection.Input);
                        _writer.WriteChannel(mcapId, sid, topic, messageEncoding, meta);
                        _channels.Add(new ChannelRecordState { Id = mcapId, SchemaId = sid, Topic = topic, Encoding = messageEncoding, Metadata = SnapshotChannelMetadata(meta) });
                        RecordTopicSignature(topic, signature);
                    }
                }
                WriteMessageToChannelWriteState(map, logNs, payload);
            }
        }

        // Message writing
        private void WriteMessageToChannelWriteState(ChannelWriteState map, ulong logNs, byte[] payload)
        {
            if (_recordingFailed || _closed) return;
            var seq = map.Seq++;
            map.MsgCount++;
            var payloadLength = payload?.Length ?? 0;
            if (!_options.UseChunking)
            {
                // MCAP publish_time intentionally mirrors log_time for Unity live recording.
                _writer.WriteMessage(map.McapId, seq, logNs, logNs, payload);
                TrackMessageTimes(logNs);
                return;
            }

            const int messagePrefixLength = 2 + 4 + 8 + 8;
            if (payloadLength > int.MaxValue - messagePrefixLength - McapWriter.RecordHeaderLength)
            {
                Fail("Message payload is too large for a single MCAP record.");
                return;
            }

            var contentLength = checked(messagePrefixLength + payloadLength);
            var recordLength = checked(McapWriter.RecordHeaderLength + contentLength);
            FlushChunkBeforeLargeWriteIfNeeded(recordLength);
            var off = (ulong)_chunkBuf.Position;
            var header = _messageRecordHeader;
            header[0] = McapWriter.OpcodeMessage;
            McapWriter.WriteU64(header, 1, (ulong)contentLength);
            McapWriter.WriteU16(header, McapWriter.RecordHeaderLength, map.McapId);
            McapWriter.WriteU32(header, McapWriter.RecordHeaderLength + 2, seq);
            McapWriter.WriteU64(header, McapWriter.RecordHeaderLength + 2 + 4, logNs);
            McapWriter.WriteU64(header, McapWriter.RecordHeaderLength + 2 + 4 + 8, logNs);
            _chunkBuf.Write(header, 0, header.Length);
            if (payloadLength > 0)
                _chunkBuf.Write(payload, 0, payloadLength);
            map.Pending.Add((logNs, off));
            if (_msgSt == ulong.MaxValue || logNs < _msgSt) _msgSt = logNs;
            if (logNs > _msgEt) _msgEt = logNs;
            if (!_chunkHasMessages)
            {
                _chunkSt = logNs;
                _chunkEt = logNs;
                _chunkHasMessages = true;
            }
            else
            {
                if (logNs < _chunkSt) _chunkSt = logNs;
                if (logNs > _chunkEt) _chunkEt = logNs;
            }
            _msgCount++;
            if (_chunkBuf.Length >= _chunkSz) FlushChunk();
        }

        private void TrackMessageTimes(ulong logNs)
        {
            if (_msgSt == ulong.MaxValue || logNs < _msgSt) _msgSt = logNs;
            if (logNs > _msgEt) _msgEt = logNs;
            _msgCount++;
        }

        /// <summary>
        /// Write a standalone metadata record to the MCAP file.
        /// </summary>
        public void WriteMetadata(string name, string jsonValue)
        {
            lock (_lock)
            {
                if (_recordingFailed || _closed) return;
                var off = (ulong)_writer.Position;
                _writer.WriteMetadata(name, new Dictionary<string, string> { ["value"] = jsonValue });
                var len = (ulong)_writer.Position - off;
                _metaIdx.Add(new MetadataIndexState { Offset = off, Length = len, Name = name });
                _metadataCount++;
            }
        }

        /// <summary>
        /// Write an attachment outside chunks. Flushes the active chunk first.
        /// Safe no-op if recording failed or already closed.
        /// </summary>
        public void AddAttachment(string name, string mediaType, byte[] data, ulong logTimeNs, ulong createTimeNs = 0)
        {
            lock (_lock)
            {
                if (_recordingFailed || _closed) return;
                FlushChunk();
                var index = _writer.WriteAttachment(logTimeNs, createTimeNs, name, mediaType, data, _options.EnableCrcs);
                _attachmentIdx.Add(index);
                _attachmentCount++;
            }
        }

        /// <summary>
        /// Write a server-side message by Foxglove channel ID to the current chunk.
        /// </summary>
        public void WriteMessage(uint fId, ulong logNs, byte[] payload)
        {
            lock (_lock)
            {
                if (_recordingFailed || _closed || !_serverChannelWriteStates.TryGetValue(fId, out var map)) return;
                WriteMessageToChannelWriteState(map, logNs, payload);
            }
        }

        // Lifecycle
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
