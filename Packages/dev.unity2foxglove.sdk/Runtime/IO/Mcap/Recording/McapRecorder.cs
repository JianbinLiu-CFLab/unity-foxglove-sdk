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
using System.Security.Cryptography;
using System.Text;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Util;

namespace Unity.FoxgloveSDK.IO
{
    /// <summary>
    /// MCAP recorder that attaches to a FoxgloveSession via dual-write hooks.
    /// Manages chunk lifecycle, schema/channel deduplication, metadata
    /// indexes, and final summary/statistics output on close.
    /// </summary>
    public class McapRecorder : IDisposable
    {
        private readonly McapWriter _w;
        private readonly IFoxgloveLogger _log;
        private readonly McapWriterOptions _options;
        private readonly string _compression;
        private readonly Dictionary<(string name, string enc, string hash), ushort> _sKey = new();
        private readonly Dictionary<(uint clientId, uint chId), ChannelWriteState> _clientChannelWriteState = new();
        private readonly HashSet<(uint clientId, uint chId)> _skippedClientChannels = new();
        private readonly Dictionary<uint, ChannelWriteState> _chMap = new();
        private readonly Dictionary<string, ChannelWriteState> _topicChannelWriteState = new();
        private readonly Dictionary<string, TopicSignature> _topicSignatures = new();
        private readonly HashSet<ushort> _seenChannelIds = new();
        private readonly List<ChannelWriteState> _allChannelWriteStates = new();
        private readonly List<SchemaRecordState> _schemas = new();
        private readonly List<ChannelRecordState> _channels = new();
        private readonly List<ChunkIndexState> _chunkIdx = new();
        private readonly List<MetadataIndexState> _metaIdx = new();
        private readonly List<McapAttachmentIndex> _attachmentIdx = new();
        private uint _attachmentCount;
        private MemoryStream _chunkBuf = new();
        private readonly object _lock = new object();
        private ushort _nextSid = 1, _nextCid = 1;
        private ulong _chunkSt, _chunkEt;
        private ulong _msgSt = ulong.MaxValue, _msgEt;
        private ulong _msgCount, _chunkCount;
        private uint _metadataCount;
        private bool _chunkHasMessages;
        private bool _closed, _recordingFailed, _disposed;
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
            _w = new McapWriter(stream, leaveOpen);
            _chunkSz = _options.ChunkSizeBytes;
            _compression = _options.Compression;
            try
            {
                _w.WriteMagic();
                _w.WriteHeader("", "unity-foxglove-sdk");
            }
            catch
            {
                _w.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Coordinate mode metadata value applied to new channels (e.g. "ros2", "fixed_frame").
        /// </summary>
        public string CoordinateMode { get; set; }

        /// <summary>
        /// Register a server-side channel and write its MCAP channel record immediately.
        /// </summary>
        public void AddChannel(uint fId, string topic, string enc, string sName, string sEnc, string sContent)
        {
            lock (_lock)
            {
                if (_recordingFailed || _closed) return;
                if (_chMap.ContainsKey(fId))
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
                if (_nextCid == 0) { Fail("Channel ID overflow"); return; }
                var mCid = _nextCid++;
                var state = new ChannelWriteState { McapId = mCid, Topic = topic };
                _chMap[fId] = state;
                if (!_topicChannelWriteState.ContainsKey(topic))
                    _topicChannelWriteState[topic] = state;

                var meta = new Dictionary<string, string>();
                if (!string.IsNullOrEmpty(CoordinateMode))
                    meta["coordinate_mode"] = CoordinateMode;
                _w.WriteChannel(mCid, sid, topic, normalizedEnc, meta);
                _channels.Add(new ChannelRecordState { Id = mCid, SchemaId = sid, Topic = topic, Encoding = normalizedEnc, Metadata = new Dictionary<string, string>(meta) });
                RecordTopicSignature(topic, signature);
            }
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
                    if (TryReuseExistingTopicChannel(topic, signature, sContent, out map))
                    {
                        _clientChannelWriteState[key] = map;
                    }
                    else
                    {
                        if (WouldMixTopicSignature(topic, signature))
                        {
                            _skippedClientChannels.Add(key);
                            _log.LogWarning(
                                $"MCAP: skipping client-published topic '{topic}' because its schema signature is incompatible with an existing recorded channel.");
                            return;
                        }

                        var sid = GetOrCreateSchema(sName, sEnc, sContent);
                        if (_recordingFailed) return;
                        if (_nextCid == 0) { Fail("Channel ID overflow"); return; }
                        var mcapId = _nextCid++;
                        map = new ChannelWriteState { McapId = mcapId, Topic = topic };
                        _clientChannelWriteState[key] = map;
                        var meta = string.IsNullOrEmpty(CoordinateMode)
                            ? new Dictionary<string, string>()
                            : new Dictionary<string, string> { ["coordinate_mode"] = CoordinateMode };
                        _w.WriteChannel(mcapId, sid, topic, messageEncoding, meta);
                        _channels.Add(new ChannelRecordState { Id = mcapId, SchemaId = sid, Topic = topic, Encoding = messageEncoding, Metadata = meta });
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
            var payloadLength = payload?.Length ?? 0;
            if (!_options.UseChunking)
            {
                // MCAP publish_time intentionally mirrors log_time for Unity live recording.
                _w.WriteMessage(map.McapId, seq, logNs, logNs, payload);
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
            _chunkBuf.WriteByte(McapWriter.OpcodeMessage);
            McapWriter.WriteU64(_chunkBuf, (ulong)contentLength);
            McapWriter.WriteU16(_chunkBuf, map.McapId);
            McapWriter.WriteU32(_chunkBuf, seq);
            McapWriter.WriteU64(_chunkBuf, logNs);
            McapWriter.WriteU64(_chunkBuf, logNs);
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
                var off = (ulong)_w.Position;
                _w.WriteMetadata(name, new Dictionary<string, string> { ["value"] = jsonValue });
                var len = (ulong)_w.Position - off;
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
                var index = _w.WriteAttachment(logTimeNs, createTimeNs, name, mediaType, data, _options.EnableCrcs);
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
                if (_recordingFailed || _closed || !_chMap.TryGetValue(fId, out var map)) return;
                WriteMessageToChannelWriteState(map, logNs, payload);
            }
        }

        // Lifecycle
        /// <summary>
        /// Finalize the MCAP file: flush the last chunk, write summary groups,
        /// footer, and magic suffix.
        /// </summary>
        public void Close()
        {
            lock (_lock)
            {
                if (_closed) return;
                var flushStartPosition = _w.Position;
                try
                {
                    FlushChunk();
                }
                catch (Exception ex)
                {
                    if (TryRecoverAfterFailedFinalChunkFlush(flushStartPosition, ex))
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
                        ? _w.ComputeCrc32FromStartToCurrent()
                        : 0;
                    _w.WriteDataEnd(dataSectionCrc);

                    McapSummarySerializer.WriteSummaryAndFooter(
                        _w,
                        BuildFinalSummary(includeStatistics: true),
                        _options.UseSummaryOffsets,
                        _options.EnableCrcs);
                    _w.WriteMagic();
                    _w.Flush();
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
                        Metadata = channel.Metadata ?? new Dictionary<string, string>()
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
                    ChannelMessageCounts = AllChannelWriteStates().ToDictionary(m => m.McapId, m => (ulong)m.Seq)
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

        private void WriteRecoverableTrailerAfterDroppedFinalChunk()
        {
            _w.WriteDataEnd(0);
            McapSummarySerializer.WriteSummaryAndFooter(
                _w,
                BuildFinalSummary(includeStatistics: false),
                _options.UseSummaryOffsets,
                _options.EnableCrcs);
            _w.WriteMagic();
            _w.Flush();
        }

        private bool TryRecoverAfterFailedFinalChunkFlush(long flushStartPosition, Exception flushError)
        {
            if (!_w.CanSeek)
                return false;

            try
            {
                if (_w.Position != flushStartPosition)
                    _w.TruncateToPosition(flushStartPosition);

                _log.LogWarning(
                    $"MCAP recorder dropped the final unflushed chunk during close; writing a recoverable indexed trailer without final-chunk statistics: {flushError.Message}");
                WriteRecoverableTrailerAfterDroppedFinalChunk();
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
                    _w.Dispose();
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
            }
        }

        // Helpers
        // Caller must hold _lock. The returned list is an instance scratch buffer
        // and must not be retained after the locked operation finishes.
        List<ChannelWriteState> AllChannelWriteStates()
        {
            _seenChannelIds.Clear();
            _allChannelWriteStates.Clear();
            foreach (var m in _chMap.Values)
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
            try
            {
                if (!_chunkBuf.TryGetBuffer(out var raw))
                    throw new InvalidOperationException("MCAP chunk buffer is not publicly visible.");
                var rawCrc = _options.EnableCrcs
                    ? Util.Crc32Helper.Compute(new ReadOnlySpan<byte>(raw.Array, raw.Offset, raw.Count))
                    : 0;
                var compressed = McapCompression.Compress(_compression, raw, _options.Lz4CompressionLevel);
                var off = (ulong)_w.Position;
                _w.WriteChunk(_chunkSt, _chunkEt, (ulong)raw.Count, rawCrc, _compression, (ulong)compressed.Count, compressed);
                var chunkLen = (ulong)_w.Position - off;
                var mio = new Dictionary<ushort, ulong>();
                ulong mioTLen = 0;
                foreach (var map in AllChannelWriteStates())
                {
                    if (map.Pending.Count == 0) continue;
                    if (_options.HasIndex(McapIndexTypes.Message))
                    {
                        var start = (ulong)_w.Position;
                        _w.WriteMessageIndex(map.McapId, map.Pending);
                        var len = (ulong)_w.Position - start;
                        mio[map.McapId] = start;
                        mioTLen += len;
                    }
                }
                if (_options.HasIndex(McapIndexTypes.Chunk))
                    _chunkIdx.Add(new ChunkIndexState { StartTime = _chunkSt, EndTime = _chunkEt, Offset = off, Length = chunkLen, MessageIndexOffsets = mio, MessageIndexLength = mioTLen, Compression = _compression, CompressedSize = (ulong)compressed.Count, UncompressedSize = (ulong)raw.Count });
                _chunkCount++;
                ResetActiveChunkState();
            }
            catch (Exception ex)
            {
                ResetActiveChunkState();
                Fail("Chunk flush failed: " + ex.Message);
                throw;
            }
        }

        private void ResetActiveChunkState()
        {
            _chunkBuf.SetLength(0);
            foreach (var map in AllChannelWriteStates())
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

        /// <summary>
        /// Compute the Base64 SHA-256 hash of a string.
        /// </summary>
        static string Sha256(string c) { using var h = SHA256.Create(); return Convert.ToBase64String(h.ComputeHash(Encoding.UTF8.GetBytes(c))); }

        // Schema management
        ushort GetOrCreateSchema(string sName, string sEnc, string sContent)
        {
            if (string.IsNullOrEmpty(sName) && string.IsNullOrEmpty(sEnc) && string.IsNullOrEmpty(sContent))
                return 0;

            var hash = Sha256(sContent ?? "");
            var key = (sName ?? "", sEnc ?? "", hash);
            if (_sKey.TryGetValue(key, out var sid))
                return sid;

            byte[] schemaData;
            try
            {
                schemaData = sEnc == "protobuf"
                    ? Convert.FromBase64String(sContent ?? "")
                    : Encoding.UTF8.GetBytes(sContent ?? "");
            }
            catch (FormatException ex)
            {
                Fail("Invalid protobuf schema content: " + ex.Message);
                return 0;
            }

            if (_nextSid == 0) { Fail("Schema ID overflow"); return 0; }
            sid = _nextSid++;
            _sKey[key] = sid;
            _w.WriteSchema(sid, key.Item1, key.Item2, schemaData);
            _schemas.Add(new SchemaRecordState { Id = sid, Name = key.Item1, Encoding = key.Item2, Data = schemaData });
            return sid;
        }

        /// <summary>
        /// Immutable signature combining encoding, schema name, schema encoding,
        /// and content hash. Used to detect incompatible topic schema conflicts.
        /// </summary>
        struct TopicSignature : IEquatable<TopicSignature>
        {
            /// <summary>Message encoding (e.g. "json", "protobuf").</summary>
            public string Encoding;
            /// <summary>Schema name.</summary>
            public string SchemaName;
            /// <summary>Schema encoding (e.g. "jsonschema").</summary>
            public string SchemaEncoding;
            /// <summary>Hex-encoded SHA-256 hash of schema content.</summary>
            public string Hash;

            public bool Equals(TopicSignature other) =>
                Encoding == other.Encoding &&
                SchemaName == other.SchemaName &&
                SchemaEncoding == other.SchemaEncoding &&
                Hash == other.Hash;

            public override bool Equals(object obj) =>
                obj is TopicSignature other && Equals(other);

            public override int GetHashCode() =>
                HashCode.Combine(Encoding, SchemaName, SchemaEncoding, Hash);
        }

        /// <summary>
        /// Compute a hex-encoded SHA-256 hash from schema name, encoding, and
        /// content, separated by null characters.
        /// </summary>
        static string ComputeSchemaHash(string schemaContent, string schemaName, string schemaEncoding)
        {
            // For schemaless channels, the signature components are all empty.
            // We treat empty schemaContent as an empty hash.
            var content = schemaContent ?? "";
            if (content.Length == 0) return "";
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(schemaName + "\0" + schemaEncoding + "\0" + content));
            return BitConverter.ToString(bytes).Replace("-", "");
        }

        /// <summary>
        /// Normalize an encoding string to a default of "json" when empty or null.
        /// </summary>
        static string NormalizeMessageEncoding(string enc) =>
            string.IsNullOrEmpty(enc) ? "json" : enc;

        static TopicSignature CreateTopicSignature(string enc, string sName, string sEnc, string sContent) =>
            new()
            {
                Encoding = NormalizeMessageEncoding(enc),
                SchemaName = sName ?? "",
                SchemaEncoding = sEnc ?? "",
                Hash = ComputeSchemaHash(sContent, sName, sEnc)
            };

        // Channel routing
        bool TryReuseExistingTopicChannel(
            string topic,
            TopicSignature incoming,
            string sContent,
            out ChannelWriteState state)
        {
            state = null;
            if (string.IsNullOrEmpty(topic)) return false;
            if (!_topicChannelWriteState.TryGetValue(topic, out var existingState)) return false;
            if (!_topicSignatures.TryGetValue(topic, out var existing)) return false;

            if (!string.IsNullOrEmpty(incoming.SchemaName) &&
                string.IsNullOrEmpty(sContent) &&
                existing.Encoding == incoming.Encoding &&
                existing.SchemaName == incoming.SchemaName &&
                (string.IsNullOrEmpty(incoming.SchemaEncoding) || existing.SchemaEncoding == incoming.SchemaEncoding) &&
                !string.IsNullOrEmpty(existing.Hash))
            {
                state = existingState;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Check whether an incoming topic signature conflicts with a previously
        /// recorded signature for the same topic.
        /// </summary>
        bool WouldMixTopicSignature(string topic, TopicSignature signature)
        {
            if (string.IsNullOrEmpty(topic)) return false;
            return _topicSignatures.TryGetValue(topic, out var existing) && !existing.Equals(signature);
        }

        /// <summary>
        /// Persist the topic signature on first use so future channels for the
        /// same topic can be validated for compatibility.
        /// </summary>
        void RecordTopicSignature(string topic, TopicSignature signature)
        {
            if (string.IsNullOrEmpty(topic)) return;
            if (_topicSignatures.ContainsKey(topic)) return;
            _topicSignatures[topic] = signature;
        }

        // Nested state types

        /// <summary>
        /// Per-channel write accumulator tracking MCAP channel ID, sequence
        /// number, and pending index entries for the current chunk.
        /// </summary>
        class ChannelWriteState
        {
            /// <summary>MCAP channel ID.</summary>
            public ushort McapId;
            /// <summary>Topic name.</summary>
            public string Topic;
            /// <summary>Per-channel message sequence number.</summary>
            public uint Seq;
            /// <summary>Pending (log-time, chunk-offset) entries for the chunk message index.</summary>
            public List<(ulong LogTime, ulong Offset)> Pending = new();
        }

        /// <summary>
        /// Schema record captured for the summary section.
        /// </summary>
        struct SchemaRecordState
        {
            /// <summary>Schema ID.</summary>
            public ushort Id;
            /// <summary>Schema name.</summary>
            public string Name;
            /// <summary>Schema encoding (e.g. "jsonschema", "protobuf").</summary>
            public string Encoding;
            /// <summary>Raw schema content bytes.</summary>
            public byte[] Data;
        }

        /// <summary>
        /// Channel record captured for the summary section.
        /// </summary>
        struct ChannelRecordState
        {
            /// <summary>Channel ID.</summary>
            public ushort Id;
            /// <summary>Referenced schema ID.</summary>
            public ushort SchemaId;
            /// <summary>Topic name.</summary>
            public string Topic;
            /// <summary>Message encoding string.</summary>
            public string Encoding;
            /// <summary>Optional metadata key-value pairs.</summary>
            public Dictionary<string, string> Metadata;
        }

        /// <summary>
        /// Chunk index entry backed up for the summary section.
        /// </summary>
        struct ChunkIndexState
        {
            /// <summary>Earliest log time in the chunk.</summary>
            public ulong StartTime;
            /// <summary>Latest log time in the chunk.</summary>
            public ulong EndTime;
            /// <summary>File offset of the chunk record.</summary>
            public ulong Offset;
            /// <summary>Chunk record length in bytes.</summary>
            public ulong Length;
            /// <summary>Total size of the message index records following the chunk.</summary>
            public ulong MessageIndexLength;
            /// <summary>Compressed chunk data size in bytes.</summary>
            public ulong CompressedSize;
            /// <summary>Uncompressed chunk data size in bytes.</summary>
            public ulong UncompressedSize;
            /// <summary>Compression algorithm name (empty for none).</summary>
            public string Compression;
            /// <summary>Per-channel offset map into the message index records.</summary>
            public Dictionary<ushort, ulong> MessageIndexOffsets;
        }

        /// <summary>
        /// Metadata index entry backed up for the summary section.
        /// </summary>
        struct MetadataIndexState
        {
            /// <summary>File offset of the metadata record.</summary>
            public ulong Offset;
            /// <summary>Metadata record byte length.</summary>
            public ulong Length;
            /// <summary>Metadata name.</summary>
            public string Name;
        }
    }
}
