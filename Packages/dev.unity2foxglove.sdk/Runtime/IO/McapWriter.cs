using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Unity.FoxgloveSDK.IO
{
    public class McapWriter : IDisposable
    {
        private readonly Stream _stream;
        private readonly bool _leaveOpen;

        public long Position => _stream.Position;
        public static readonly byte[] Magic = { 0x89, (byte)'M', (byte)'C', (byte)'A', (byte)'P', 0x30, 0x0D, 0x0A };

        public McapWriter(Stream stream, bool leaveOpen = false) { _stream = stream; _leaveOpen = leaveOpen; }

        public void WriteMagic() => _stream.Write(Magic, 0, Magic.Length);

        public void WriteRecord(byte opcode, byte[] content)
        {
            _stream.WriteByte(opcode);
            WriteU64(_stream, (ulong)(content?.Length ?? 0));
            if (content != null && content.Length > 0) _stream.Write(content, 0, content.Length);
        }

        public void WriteHeader(string profile, string library) { var m = new MemoryStream(); WrStr(m, profile); WrStr(m, library); WriteRecord(0x01, m.ToArray()); }
        public void WriteSchema(ushort id, string name, string enc, byte[] data) { var m = new MemoryStream(); WriteU16(m, id); WrStr(m, name); WrStr(m, enc); WrPrefixed(m, data ?? new byte[0]); WriteRecord(0x03, m.ToArray()); }
        public void WriteChannel(ushort id, ushort sid, string topic, string enc, Dictionary<string,string> meta) { var m = new MemoryStream(); WriteU16(m, id); WriteU16(m, sid); WrStr(m, topic); WrStr(m, enc); WrMap(m, meta ?? new()); WriteRecord(0x04, m.ToArray()); }
        public void WriteMessage(ushort ch, uint seq, ulong log, ulong pub, byte[] d) { var m = new MemoryStream(); WriteU16(m, ch); WriteU32(m, seq); WriteU64(m, log); WriteU64(m, pub); if (d != null) m.Write(d, 0, d.Length); WriteRecord(0x05, m.ToArray()); }
        public void WriteChunk(ulong st, ulong et, ulong uncompSz, uint uncompCrc, string comp, ulong compSz, byte[] recs) { var m = new MemoryStream(); WriteU64(m, st); WriteU64(m, et); WriteU64(m, uncompSz); WriteU32(m, uncompCrc); WrStr(m, comp); WriteU64(m, compSz); m.Write(recs ?? new byte[0], 0, recs?.Length ?? 0); WriteRecord(0x06, m.ToArray()); }
        public void WriteMetadata(string name, Dictionary<string,string> meta) { var m = new MemoryStream(); WrStr(m, name); WrMap(m, meta ?? new()); WriteRecord(0x0C, m.ToArray()); }
        public void WriteMetadataIndex(ulong metadataOffset, ulong metadataLength, string name) { var m = new MemoryStream(); WriteU64(m, metadataOffset); WriteU64(m, metadataLength); WrStr(m, name ?? ""); WriteRecord(0x0D, m.ToArray()); }
        public void WriteMessageIndex(ushort ch, List<(ulong,ulong)> entries) { var m = new MemoryStream(); WriteU16(m, ch); var recsLen = (uint)((entries?.Count ?? 0) * 16); WriteU32(m, recsLen); if (entries != null) foreach (var (ts, off) in entries) { WriteU64(m, ts); WriteU64(m, off); } WriteRecord(0x07, m.ToArray()); }
        public void WriteChunkIndex(ulong st, ulong et, ulong chOff, ulong chLen, Dictionary<ushort,ulong> idxOff, ulong idxLen, string comp, ulong compSz, ulong uncompSz) { var m = new MemoryStream(); WriteU64(m, st); WriteU64(m, et); WriteU64(m, chOff); WriteU64(m, chLen); var mioLen = (uint)((idxOff?.Count ?? 0) * 10); WriteU32(m, mioLen); if (idxOff != null) foreach (var (k, v) in idxOff) { WriteU16(m, k); WriteU64(m, v); } WriteU64(m, idxLen); WrStr(m, comp); WriteU64(m, compSz); WriteU64(m, uncompSz); WriteRecord(0x08, m.ToArray()); }
        public void WriteStatistics(ulong msgCount, ushort schCount, uint chCount, uint att, uint meta, uint chuCount, ulong st, ulong et, Dictionary<ushort,ulong> chCounts) { var m = new MemoryStream(); WriteU64(m, msgCount); WriteU16(m, schCount); WriteU32(m, chCount); WriteU32(m, att); WriteU32(m, meta); WriteU32(m, chuCount); WriteU64(m, st); WriteU64(m, et); var cmsLen = (uint)((chCounts?.Count ?? 0) * 10); WriteU32(m, cmsLen); if (chCounts != null) foreach (var (k, v) in chCounts) { WriteU16(m, k); WriteU64(m, v); } WriteRecord(0x0B, m.ToArray()); }
        public void WriteDataEnd() { var m = new MemoryStream(); WriteU32(m, 0); WriteRecord(0x0F, m.ToArray()); }
        public void WriteFooter(ulong sumStart, ulong sumOffStart, uint sumCrc) { var m = new MemoryStream(); WriteU64(m, sumStart); WriteU64(m, sumOffStart); WriteU32(m, sumCrc); WriteRecord(0x02, m.ToArray()); }
        public void WriteSummaryOffset(byte grp, ulong start, ulong len) { var m = new MemoryStream(); m.WriteByte(grp); WriteU64(m, start); WriteU64(m, len); WriteRecord(0x0E, m.ToArray()); }
        public void Flush() => _stream.Flush();
        public void Dispose() { Flush(); if (!_leaveOpen) _stream.Dispose(); }

        // ── Statics for inline usage from McapRecorder ──
        public static void WriteU16(Stream s, ushort v) { s.WriteByte((byte)v); s.WriteByte((byte)(v >> 8)); }
        public static void WriteU32(Stream s, uint v) { s.WriteByte((byte)v); s.WriteByte((byte)(v >> 8)); s.WriteByte((byte)(v >> 16)); s.WriteByte((byte)(v >> 24)); }
        public static void WriteU64(Stream s, ulong v) { s.WriteByte((byte)v); s.WriteByte((byte)(v >> 8)); s.WriteByte((byte)(v >> 16)); s.WriteByte((byte)(v >> 24)); s.WriteByte((byte)(v >> 32)); s.WriteByte((byte)(v >> 40)); s.WriteByte((byte)(v >> 48)); s.WriteByte((byte)(v >> 56)); }
        public static void WrStr(Stream s, string str) { var b = Encoding.UTF8.GetBytes(str ?? ""); WriteU32(s, (uint)b.Length); if (b.Length > 0) s.Write(b, 0, b.Length); }
        public static void WrPrefixed(Stream s, byte[] d) { WriteU32(s, (uint)(d?.Length ?? 0)); if (d != null && d.Length > 0) s.Write(d, 0, d.Length); }
        public static void WrMap(Stream s, Dictionary<string,string> map) { var t = new MemoryStream(); foreach (var kv in map.OrderBy(kv => kv.Key, StringComparer.Ordinal)) { WrStr(t, kv.Key); WrStr(t, kv.Value); } var b = t.ToArray(); WriteU32(s, (uint)b.Length); if (b.Length > 0) s.Write(b, 0, b.Length); }
    }
}
