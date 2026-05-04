using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Unity.FoxgloveSDK.Core;

namespace Unity.FoxgloveSDK.IO
{
    public class McapRecorder : IDisposable
    {
        private readonly McapWriter _w;
        private readonly IFoxgloveLogger _log;
        private readonly string _compression;
        private readonly Dictionary<(string name, string enc, string hash), ushort> _sKey = new();
        private readonly Dictionary<(uint clientId, uint chId), ChMap> _clientChMap = new();
        private readonly Dictionary<uint, ChMap> _chMap = new();
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
            ushort sid = 0;
            if (!string.IsNullOrEmpty(sName) || !string.IsNullOrEmpty(sEnc) || !string.IsNullOrEmpty(sContent))
            {
                var hash = Sha256(sContent ?? "");
                var k = (sName ?? "", sEnc ?? "", hash);
                if (!_sKey.TryGetValue(k, out sid))
                {
                    if (_nextSid == 0) { Fail("Schema ID overflow"); return; }
                    sid = _nextSid++;
                    _sKey[k] = sid;
                    var schemaData = Encoding.UTF8.GetBytes(sContent ?? "");
                    _w.WriteSchema(sid, k.Item1, k.Item2, schemaData);
                    _schemas.Add(new SchemaRec { Id = sid, Name = k.Item1, Enc = k.Item2, Data = schemaData });
                }
            }
            if (_nextCid == 0) { Fail("Channel ID overflow"); return; }
            var mCid = _nextCid++;
            _chMap[fId] = new ChMap { McapId = mCid, Topic = topic };

            var meta = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(CoordinateMode))
                meta["coordinate_mode"] = CoordinateMode;
            _w.WriteChannel(mCid, sid, topic, enc, meta);
            _channels.Add(new ChannelRec { Id = mCid, Sid = sid, Topic = topic, Enc = enc, Meta = new Dictionary<string, string>(meta) });
        }

        public void WriteClientMessage(uint clientId, uint chId, ulong logNs, byte[] payload, string topic)
        {
            if (_failed) return;
            var key = (clientId, chId);
            if (!_clientChMap.TryGetValue(key, out var map))
            {
                if (_nextCid == 0) { Fail("Channel ID overflow"); return; }
                var mcapId = _nextCid++;
                map = new ChMap { McapId = mcapId, Topic = topic };
                _clientChMap[key] = map;
                var meta = string.IsNullOrEmpty(CoordinateMode)
                    ? new Dictionary<string, string>()
                    : new Dictionary<string, string> { ["coordinate_mode"] = CoordinateMode };
                _w.WriteChannel(mcapId, 0, topic, "json", meta);
                _channels.Add(new ChannelRec { Id = mcapId, Sid = 0, Topic = topic, Enc = "json", Meta = meta });
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

        class ChMap { public ushort McapId; public string Topic; public uint Seq; public List<(ulong, ulong)> Pending = new(); }
        struct SchemaRec { public ushort Id; public string Name, Enc; public byte[] Data; }
        struct ChannelRec { public ushort Id, Sid; public string Topic, Enc; public Dictionary<string, string> Meta; }
        struct ChunkIdx { public ulong St, Et, Off, Len, MioLen, CompSz, UncompSz; public string Comp; public Dictionary<ushort, ulong> Mio; }
        struct MetaIdx { public ulong Off, Len; public string Name; }
    }
}
