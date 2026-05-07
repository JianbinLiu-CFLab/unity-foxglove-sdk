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
        private readonly Dictionary<(uint clientId, uint chId), ChMap> _clientChMap = new();
        private readonly HashSet<(uint clientId, uint chId)> _skippedClientChannels = new();
        private readonly Dictionary<uint, ChMap> _chMap = new();
        private readonly Dictionary<string, TopicSignature> _topicSignatures = new();
        private readonly List<SchemaRec> _schemas = new();
        private readonly List<ChannelRec> _channels = new();
        private readonly List<ChunkIdx> _chunkIdx = new();
        private readonly List<MetaIdx> _metaIdx = new();
        private MemoryStream _chunkBuf = new();
        private ushort _nextSid = 1, _nextCid = 1;
        private ulong _chunkSt, _chunkEt;
        private ulong _msgSt = ulong.MaxValue, _msgEt;
        private ulong _msgCount, _chunkCount;
        private uint _metadataCount;
        private bool _closed, _failed;
        private readonly int _chunkSz;

        public const int DefaultChunkSizeBytes = 1024 * 1024;

        public McapRecorder(Stream stream, IFoxgloveLogger logger = null, int chunkSizeBytes = DefaultChunkSizeBytes, string compression = "")
        {
            _w = new McapWriter(stream ?? throw new ArgumentNullException(nameof(stream)), leaveOpen: true);
            _log = logger ?? new ConsoleLogger();
            _chunkSz = chunkSizeBytes;
            _compression = compression ?? "";
            _w.WriteMagic();
            _w.WriteHeader("", "unity-foxglove-sdk");
        }

        public string CoordinateMode { get; set; }

        public void AddChannel(uint fId, string topic, string enc, string sName, string sEnc, string sContent)
        {
            if (_failed) return;
            if (WouldMixTopicSignature(topic, enc, sName, sEnc, sContent))
            {
                _log.LogWarning(
                    $"MCAP: skipping server channel for topic '{topic}' because its signature is incompatible with an existing recorded channel.");
                return;
            }
            var sid = GetOrCreateSchema(sName, sEnc, sContent);
            if (_failed) return;
            if (_nextCid == 0) { Fail("Channel ID overflow"); return; }
            var mCid = _nextCid++;
            _chMap[fId] = new ChMap { McapId = mCid, Topic = topic };

            var meta = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(CoordinateMode))
                meta["coordinate_mode"] = CoordinateMode;
            _w.WriteChannel(mCid, sid, topic, enc, meta);
            _channels.Add(new ChannelRec { Id = mCid, Sid = sid, Topic = topic, Enc = enc, Meta = new Dictionary<string, string>(meta) });
            RecordTopicSignature(topic, enc, sName, sEnc, sContent);
        }

        public void WriteClientMessage(uint clientId, uint chId, ulong logNs, byte[] payload, string topic,
            string enc = "json", string sName = "", string sEnc = "", string sContent = "")
        {
            if (_failed) return;
            var key = (clientId, chId);
            if (_skippedClientChannels.Contains(key)) return;
            if (!_clientChMap.TryGetValue(key, out var map))
            {
                if (WouldMixTopicSignature(topic, enc, sName, sEnc, sContent))
                {
                    _skippedClientChannels.Add(key);
                    _log.LogWarning(
                        $"MCAP: skipping client-published topic '{topic}' because its schema signature is incompatible with an existing recorded channel.");
                    return;
                }

                var sid = GetOrCreateSchema(sName, sEnc, sContent);
                if (_failed) return;
                if (_nextCid == 0) { Fail("Channel ID overflow"); return; }
                var mcapId = _nextCid++;
                map = new ChMap { McapId = mcapId, Topic = topic };
                _clientChMap[key] = map;
                var meta = string.IsNullOrEmpty(CoordinateMode)
                    ? new Dictionary<string, string>()
                    : new Dictionary<string, string> { ["coordinate_mode"] = CoordinateMode };
                var messageEncoding = string.IsNullOrEmpty(enc) ? "json" : enc;
                _w.WriteChannel(mcapId, sid, topic, messageEncoding, meta);
                _channels.Add(new ChannelRec { Id = mcapId, Sid = sid, Topic = topic, Enc = messageEncoding, Meta = meta });
                RecordTopicSignature(topic, enc, sName, sEnc, sContent);
            }
            WriteMessageToChMap(map, logNs, payload);
        }

        private void WriteMessageToChMap(ChMap map, ulong logNs, byte[] payload)
        {
            if (_failed) return;
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

        public void WriteMetadata(string name, string jsonValue)
        {
            if (_failed) return;
            var off = (ulong)_w.Position;
            _w.WriteMetadata(name, new Dictionary<string, string> { ["value"] = jsonValue });
            var len = (ulong)_w.Position - off;
            _metaIdx.Add(new MetaIdx { Off = off, Len = len, Name = name });
            _metadataCount++;
        }

        public void WriteMessage(uint fId, ulong logNs, byte[] payload)
        {
            if (_failed || !_chMap.TryGetValue(fId, out var map)) return;
            WriteMessageToChMap(map, logNs, payload);
        }

        public void Close()
        {
            if (_closed || _failed) return;
            _closed = true;
            FlushChunk();
            _w.WriteDataEnd();

            var sumStart = (ulong)_w.Position;

            // Schema group
            var schemaGrpStart = (ulong)_w.Position;
            foreach (var s in _schemas) _w.WriteSchema(s.Id, s.Name, s.Enc, s.Data);
            var schemaGrpLen = (ulong)_w.Position - schemaGrpStart;

            // Channel group
            var channelGrpStart = (ulong)_w.Position;
            foreach (var c in _channels) _w.WriteChannel(c.Id, c.Sid, c.Topic, c.Enc, c.Meta ?? new Dictionary<string, string>());
            var channelGrpLen = (ulong)_w.Position - channelGrpStart;

            // Statistics
            var statsGrpStart = (ulong)_w.Position;
            _w.WriteStatistics(_msgCount, (ushort)_schemas.Count, (uint)_channels.Count, 0, _metadataCount, (uint)_chunkCount, _msgSt, _msgEt,
                AllChMaps().ToDictionary(m => m.McapId, m => (ulong)m.Seq));
            var statsGrpLen = (ulong)_w.Position - statsGrpStart;

            // ChunkIndex group
            var chunkIdxGrpStart = (ulong)_w.Position;
            foreach (var ci in _chunkIdx)
                _w.WriteChunkIndex(ci.St, ci.Et, ci.Off, ci.Len, ci.Mio, ci.MioLen, ci.Comp, ci.CompSz, ci.UncompSz);
            var chunkIdxGrpLen = (ulong)_w.Position - chunkIdxGrpStart;

            // MetadataIndex group
            var metaIdxGrpStart = (ulong)_w.Position;
            foreach (var mi in _metaIdx)
                _w.WriteMetadataIndex(mi.Off, mi.Len, mi.Name);
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

        public void Dispose() { Close(); _w.Dispose(); _chunkBuf.Dispose(); }

        IEnumerable<ChMap> AllChMaps()
        {
            foreach (var m in _chMap.Values) yield return m;
            foreach (var m in _clientChMap.Values) yield return m;
        }

        void FlushChunk()
        {
            if (_chunkBuf.Length == 0) return;
            var raw = _chunkBuf.ToArray(); _chunkBuf.SetLength(0);
            var compressed = McapCompression.Compress(_compression, raw);
            var off = (ulong)_w.Position;
            _w.WriteChunk(_chunkSt, _chunkEt, (ulong)raw.Length, 0, _compression, (ulong)compressed.Length, compressed);
            var chunkLen = (ulong)_w.Position - off;
            var mio = new Dictionary<ushort, ulong>();
            ulong mioTLen = 0;
            foreach (var map in AllChMaps())
            {
                if (map.Pending.Count == 0) continue;
                var start = (ulong)_w.Position;
                _w.WriteMessageIndex(map.McapId, map.Pending);
                var len = (ulong)_w.Position - start;
                mio[map.McapId] = start;
                mioTLen += len;
                map.Pending.Clear();
            }
            _chunkIdx.Add(new ChunkIdx { St = _chunkSt, Et = _chunkEt, Off = off, Len = chunkLen, Mio = mio, MioLen = mioTLen, Comp = _compression, CompSz = (ulong)compressed.Length, UncompSz = (ulong)raw.Length });
            _chunkCount++; _chunkSt = _chunkEt = 0;
        }

        void Fail(string msg) { _failed = true; _log.LogError($"MCAP: {msg}"); }
        static string Sha256(string c) { using var h = SHA256.Create(); return Convert.ToBase64String(h.ComputeHash(Encoding.UTF8.GetBytes(c))); }

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
            _schemas.Add(new SchemaRec { Id = sid, Name = key.Item1, Enc = key.Item2, Data = schemaData });
            return sid;
        }

        struct TopicSignature : IEquatable<TopicSignature>
        {
            public string Encoding;
            public string SchemaName;
            public string SchemaEncoding;
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

        bool WouldMixTopicSignature(string topic, string enc, string sName, string sEnc, string sContent)
        {
            if (string.IsNullOrEmpty(topic)) return false;
            var sig = new TopicSignature
            {
                Encoding = enc ?? "",
                SchemaName = sName ?? "",
                SchemaEncoding = sEnc ?? "",
                Hash = ComputeSchemaHash(sContent, sName, sEnc)
            };
            return _topicSignatures.TryGetValue(topic, out var existing) && !existing.Equals(sig);
        }

        void RecordTopicSignature(string topic, string enc, string sName, string sEnc, string sContent)
        {
            if (string.IsNullOrEmpty(topic)) return;
            if (_topicSignatures.ContainsKey(topic)) return;
            _topicSignatures[topic] = new TopicSignature
            {
                Encoding = enc ?? "",
                SchemaName = sName ?? "",
                SchemaEncoding = sEnc ?? "",
                Hash = ComputeSchemaHash(sContent, sName, sEnc)
            };
        }

        class ChMap { public ushort McapId; public string Topic; public uint Seq; public List<(ulong, ulong)> Pending = new(); }
        struct SchemaRec { public ushort Id; public string Name, Enc; public byte[] Data; }
        struct ChannelRec { public ushort Id, Sid; public string Topic, Enc; public Dictionary<string, string> Meta; }
        struct ChunkIdx { public ulong St, Et, Off, Len, MioLen, CompSz, UncompSz; public string Comp; public Dictionary<ushort, ulong> Mio; }
        struct MetaIdx { public ulong Off, Len; public string Name; }
    }
}
