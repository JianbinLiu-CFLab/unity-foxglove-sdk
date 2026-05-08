// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO
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
        private readonly string _compression;
        private readonly Dictionary<(string name, string enc, string hash), ushort> _sKey = new();
        private readonly Dictionary<(uint clientId, uint chId), ChannelWriteState> _clientChannelWriteState = new();
        private readonly HashSet<(uint clientId, uint chId)> _skippedClientChannels = new();
        private readonly Dictionary<uint, ChannelWriteState> _chMap = new();
        private readonly Dictionary<string, ChannelWriteState> _topicChannelWriteState = new();
        private readonly Dictionary<string, TopicSignature> _topicSignatures = new();
        private readonly List<SchemaRecordState> _schemas = new();
        private readonly List<ChannelRecordState> _channels = new();
        private readonly List<ChunkIndexState> _chunkIdx = new();
        private readonly List<MetadataIndexState> _metaIdx = new();
        private MemoryStream _chunkBuf = new();
        private ushort _nextSid = 1, _nextCid = 1;
        private ulong _chunkSt, _chunkEt;
        private ulong _msgSt = ulong.MaxValue, _msgEt;
        private ulong _msgCount, _chunkCount;
        private uint _metadataCount;
        private bool _closed, _recordingFailed;
        private readonly int _chunkSz;

        /// <summary>
        /// Default chunk size in bytes (1 MiB).
        /// </summary>
        public const int DefaultChunkSizeBytes = 1024 * 1024;

        /// <summary>
        /// Creates a new MCAP recorder writing to the given stream.
        /// Optional compression controls per-chunk compression (e.g. "zstd").
        /// </summary>
        public McapRecorder(Stream stream, IFoxgloveLogger logger = null, int chunkSizeBytes = DefaultChunkSizeBytes, string compression = "")
        {
            _w = new McapWriter(stream ?? throw new ArgumentNullException(nameof(stream)), leaveOpen: true);
            _log = logger ?? new ConsoleLogger();
            _chunkSz = chunkSizeBytes;
            _compression = compression ?? "";
            _w.WriteMagic();
            _w.WriteHeader("", "unity-foxglove-sdk");
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
            if (_recordingFailed) return;
            var normalizedEnc = NormalizeMessageEncoding(enc);
            if (WouldMixTopicSignature(topic, normalizedEnc, sName, sEnc, sContent))
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
            RecordTopicSignature(topic, normalizedEnc, sName, sEnc, sContent);
        }

        /// <summary>
        /// Write a client-published message to the current chunk, lazily creating
        /// the channel record on first use.
        /// </summary>
        public void WriteClientMessage(uint clientId, uint chId, ulong logNs, byte[] payload, string topic,
            string enc = "json", string sName = "", string sEnc = "", string sContent = "")
        {
            if (_recordingFailed) return;
            var key = (clientId, chId);
            if (_skippedClientChannels.Contains(key)) return;
            if (!_clientChannelWriteState.TryGetValue(key, out var map))
            {
                var messageEncoding = NormalizeMessageEncoding(enc);
                if (TryReuseExistingTopicChannel(topic, messageEncoding, sName, sEnc, sContent, out map))
                {
                    _clientChannelWriteState[key] = map;
                }
                else
                {
                    if (WouldMixTopicSignature(topic, messageEncoding, sName, sEnc, sContent))
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
                    RecordTopicSignature(topic, messageEncoding, sName, sEnc, sContent);
                }
            }
            WriteMessageToChannelWriteState(map, logNs, payload);
        }

        // ── Message Writing ──
        private void WriteMessageToChannelWriteState(ChannelWriteState map, ulong logNs, byte[] payload)
        {
            if (_recordingFailed) return;
            var seq = map.Seq++;
            var off = (ulong)_chunkBuf.Position;
            var m = new MemoryStream();
            McapWriter.WriteU16(m, map.McapId);
            McapWriter.WriteU32(m, seq);
            McapWriter.WriteU64(m, logNs);
            McapWriter.WriteU64(m, logNs);
            if (payload != null) m.Write(payload, 0, payload.Length);
            var content = m.ToArray();
            _chunkBuf.WriteByte(0x05);
            McapWriter.WriteU64(_chunkBuf, (ulong)content.Length);
            _chunkBuf.Write(content, 0, content.Length);
            map.Pending.Add((logNs, off));
            if (_msgSt == ulong.MaxValue || logNs < _msgSt) _msgSt = logNs;
            if (logNs > _msgEt) _msgEt = logNs;
            if (_chunkSt == 0 && _chunkEt == 0) { _chunkSt = logNs; _chunkEt = logNs; }
            if (logNs < _chunkSt) _chunkSt = logNs;
            if (logNs > _chunkEt) _chunkEt = logNs;
            _msgCount++;
            if (_chunkBuf.Length >= _chunkSz) FlushChunk();
        }

        /// <summary>
        /// Write a standalone metadata record to the MCAP file.
        /// </summary>
        public void WriteMetadata(string name, string jsonValue)
        {
            if (_recordingFailed) return;
            var off = (ulong)_w.Position;
            _w.WriteMetadata(name, new Dictionary<string, string> { ["value"] = jsonValue });
            var len = (ulong)_w.Position - off;
            _metaIdx.Add(new MetadataIndexState { Offset = off, Length = len, Name = name });
            _metadataCount++;
        }

        /// <summary>
        /// Write a server-side message by Foxglove channel ID to the current chunk.
        /// </summary>
        public void WriteMessage(uint fId, ulong logNs, byte[] payload)
        {
            if (_recordingFailed || !_chMap.TryGetValue(fId, out var map)) return;
            WriteMessageToChannelWriteState(map, logNs, payload);
        }

        // ── Lifecycle ──
        /// <summary>
        /// Finalize the MCAP file: flush the last chunk, write summary groups,
        /// footer, and magic suffix.
        /// </summary>
        public void Close()
        {
            if (_closed || _recordingFailed) return;
            _closed = true;
            FlushChunk();
            _w.WriteDataEnd();

            var sumStart = (ulong)_w.Position;

            // Schema group
            var schemaGrpStart = (ulong)_w.Position;
            foreach (var s in _schemas) _w.WriteSchema(s.Id, s.Name, s.Encoding, s.Data);
            var schemaGrpLen = (ulong)_w.Position - schemaGrpStart;

            // Channel group
            var channelGrpStart = (ulong)_w.Position;
            foreach (var c in _channels) _w.WriteChannel(c.Id, c.SchemaId, c.Topic, c.Encoding, c.Metadata ?? new Dictionary<string, string>());
            var channelGrpLen = (ulong)_w.Position - channelGrpStart;

            // Statistics
            var statsGrpStart = (ulong)_w.Position;
            _w.WriteStatistics(_msgCount, (ushort)_schemas.Count, (uint)_channels.Count, 0, _metadataCount, (uint)_chunkCount, _msgSt, _msgEt,
                AllChannelWriteStates().ToDictionary(m => m.McapId, m => (ulong)m.Seq));
            var statsGrpLen = (ulong)_w.Position - statsGrpStart;

            // ChunkIndex group
            var chunkIdxGrpStart = (ulong)_w.Position;
            foreach (var ci in _chunkIdx)
                _w.WriteChunkIndex(ci.StartTime, ci.EndTime, ci.Offset, ci.Length, ci.MessageIndexOffsets, ci.MessageIndexLength, ci.Compression, ci.CompressedSize, ci.UncompressedSize);
            var chunkIdxGrpLen = (ulong)_w.Position - chunkIdxGrpStart;

            // MetadataIndex group
            var metaIdxGrpStart = (ulong)_w.Position;
            foreach (var mi in _metaIdx)
                _w.WriteMetadataIndex(mi.Offset, mi.Length, mi.Name);
            var metaIdxGrpLen = (ulong)_w.Position - metaIdxGrpStart;

            // SummaryOffset per group
            var sumOffStart = (ulong)_w.Position;
            if (schemaGrpLen > 0) _w.WriteSummaryOffset(0x03, schemaGrpStart, schemaGrpLen);
            if (channelGrpLen > 0) _w.WriteSummaryOffset(0x04, channelGrpStart, channelGrpLen);
            if (statsGrpLen > 0) _w.WriteSummaryOffset(0x0B, statsGrpStart, statsGrpLen);
            if (chunkIdxGrpLen > 0) _w.WriteSummaryOffset(0x08, chunkIdxGrpStart, chunkIdxGrpLen);
            if (metaIdxGrpLen > 0) _w.WriteSummaryOffset(0x0D, metaIdxGrpStart, metaIdxGrpLen);

            _w.WriteFooter(sumStart, sumOffStart, 0);
            _w.WriteMagic();
            _w.Flush();
        }

        /// <summary>
        /// Dispose the recorder and underlying writer and buffer streams.
        /// </summary>
        public void Dispose() { Close(); _w.Dispose(); _chunkBuf.Dispose(); }

        // ── Helpers ──
        IEnumerable<ChannelWriteState> AllChannelWriteStates()
        {
            var seen = new HashSet<ushort>();
            foreach (var m in _chMap.Values) yield return m;
            foreach (var m in _chMap.Values) seen.Add(m.McapId);
            foreach (var m in _clientChannelWriteState.Values)
            {
                if (seen.Add(m.McapId))
                    yield return m;
            }
        }

        /// <summary>
        /// Write the accumulated chunk buffer to the MCAP stream, then flush
        /// per-channel message indexes following the chunk.
        /// </summary>
        void FlushChunk()
        {
            if (_chunkBuf.Length == 0) return;
            var raw = _chunkBuf.ToArray(); _chunkBuf.SetLength(0);
            var rawCrc = Util.Crc32Helper.Compute(raw);
            var compressed = McapCompression.Compress(_compression, raw);
            var off = (ulong)_w.Position;
            _w.WriteChunk(_chunkSt, _chunkEt, (ulong)raw.Length, rawCrc, _compression, (ulong)compressed.Length, compressed);
            var chunkLen = (ulong)_w.Position - off;
            var mio = new Dictionary<ushort, ulong>();
            ulong mioTLen = 0;
            foreach (var map in AllChannelWriteStates())
            {
                if (map.Pending.Count == 0) continue;
                var start = (ulong)_w.Position;
                _w.WriteMessageIndex(map.McapId, map.Pending);
                var len = (ulong)_w.Position - start;
                mio[map.McapId] = start;
                mioTLen += len;
                map.Pending.Clear();
            }
            _chunkIdx.Add(new ChunkIndexState { StartTime = _chunkSt, EndTime = _chunkEt, Offset = off, Length = chunkLen, MessageIndexOffsets = mio, MessageIndexLength = mioTLen, Compression = _compression, CompressedSize = (ulong)compressed.Length, UncompressedSize = (ulong)raw.Length });
            _chunkCount++; _chunkSt = _chunkEt = 0;
        }

        /// <summary>
        /// Mark recording as permanently failed and log an error.
        /// </summary>
        void Fail(string msg) { _recordingFailed = true; _log.LogError($"MCAP: {msg}"); }

        /// <summary>
        /// Compute the Base64 SHA-256 hash of a string.
        /// </summary>
        static string Sha256(string c) { using var h = SHA256.Create(); return Convert.ToBase64String(h.ComputeHash(Encoding.UTF8.GetBytes(c))); }

        // ── Schema Management ──
        ushort GetOrCreateSchema(string sName, string sEnc, string sContent)
        {
            if (string.IsNullOrEmpty(sName) && string.IsNullOrEmpty(sEnc) && string.IsNullOrEmpty(sContent))
                return 0;

            var hash = Sha256(sContent ?? "");
            var key = (sName ?? "", sEnc ?? "", hash);
            if (_sKey.TryGetValue(key, out var sid))
                return sid;

            if (_nextSid == 0) { Fail("Schema ID overflow"); return 0; }
            sid = _nextSid++;
            _sKey[key] = sid;
            var schemaData = Encoding.UTF8.GetBytes(sContent ?? "");
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

        // ── Channel Routing ──
        bool TryReuseExistingTopicChannel(string topic, string enc, string sName, string sEnc,
            string sContent, out ChannelWriteState state)
        {
            state = null;
            var normalizedEnc = NormalizeMessageEncoding(enc);
            if (string.IsNullOrEmpty(topic)) return false;
            if (!_topicChannelWriteState.TryGetValue(topic, out var existingState)) return false;
            if (!_topicSignatures.TryGetValue(topic, out var existing)) return false;

            var incoming = new TopicSignature
            {
                Encoding = normalizedEnc,
                SchemaName = sName ?? "",
                SchemaEncoding = sEnc ?? "",
                Hash = ComputeSchemaHash(sContent, sName, sEnc)
            };

            if (!string.IsNullOrEmpty(sName) &&
                string.IsNullOrEmpty(sContent) &&
                existing.Encoding == normalizedEnc &&
                existing.SchemaName == (sName ?? "") &&
                (string.IsNullOrEmpty(sEnc) || existing.SchemaEncoding == (sEnc ?? "")) &&
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
        bool WouldMixTopicSignature(string topic, string enc, string sName, string sEnc, string sContent)
        {
            if (string.IsNullOrEmpty(topic)) return false;
            var sig = new TopicSignature
            {
                Encoding = NormalizeMessageEncoding(enc),
                SchemaName = sName ?? "",
                SchemaEncoding = sEnc ?? "",
                Hash = ComputeSchemaHash(sContent, sName, sEnc)
            };
            return _topicSignatures.TryGetValue(topic, out var existing) && !existing.Equals(sig);
        }

        /// <summary>
        /// Persist the topic signature on first use so future channels for the
        /// same topic can be validated for compatibility.
        /// </summary>
        void RecordTopicSignature(string topic, string enc, string sName, string sEnc, string sContent)
        {
            if (string.IsNullOrEmpty(topic)) return;
            if (_topicSignatures.ContainsKey(topic)) return;
            _topicSignatures[topic] = new TopicSignature
            {
                Encoding = NormalizeMessageEncoding(enc),
                SchemaName = sName ?? "",
                SchemaEncoding = sEnc ?? "",
                Hash = ComputeSchemaHash(sContent, sName, sEnc)
            };
        }

        // ── Nested State Types ──

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
