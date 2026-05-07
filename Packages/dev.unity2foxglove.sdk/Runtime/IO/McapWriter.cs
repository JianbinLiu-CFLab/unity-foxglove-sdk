// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO
// Purpose: Low-level MCAP binary writer — writes MCAP opcodes, records,
// and static LE helpers for fields used by McapRecorder.

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
        public void WriteSchema(ushort id, string name, string encoding, byte[] data) { var m = new MemoryStream(); WriteU16(m, id); WrStr(m, name); WrStr(m, encoding); WrPrefixed(m, data ?? new byte[0]); WriteRecord(0x03, m.ToArray()); }
        public void WriteChannel(ushort channelId, ushort schemaId, string topic, string encoding, Dictionary<string,string> meta) { var m = new MemoryStream(); WriteU16(m, channelId); WriteU16(m, schemaId); WrStr(m, topic); WrStr(m, encoding); WrMap(m, meta ?? new()); WriteRecord(0x04, m.ToArray()); }
        public void WriteMessage(ushort channelId, uint seq, ulong logTime, ulong publishTime, byte[] data) { var m = new MemoryStream(); WriteU16(m, channelId); WriteU32(m, seq); WriteU64(m, logTime); WriteU64(m, publishTime); if (data != null) m.Write(data, 0, data.Length); WriteRecord(0x05, m.ToArray()); }
        public void WriteChunk(ulong startTime, ulong endTime, ulong uncompressedSize, uint uncompressedCrc, string compression, ulong compressedSize, byte[] records) { var m = new MemoryStream(); WriteU64(m, startTime); WriteU64(m, endTime); WriteU64(m, uncompressedSize); WriteU32(m, uncompressedCrc); WrStr(m, compression); WriteU64(m, compressedSize); m.Write(records ?? new byte[0], 0, records?.Length ?? 0); WriteRecord(0x06, m.ToArray()); }
        public void WriteMetadata(string name, Dictionary<string,string> meta) { var m = new MemoryStream(); WrStr(m, name); WrMap(m, meta ?? new()); WriteRecord(0x0C, m.ToArray()); }
        public void WriteMetadataIndex(ulong metadataOffset, ulong metadataLength, string name) { var m = new MemoryStream(); WriteU64(m, metadataOffset); WriteU64(m, metadataLength); WrStr(m, name ?? ""); WriteRecord(0x0D, m.ToArray()); }
        public void WriteMessageIndex(ushort channelId, List<(ulong,ulong)> entries) { var m = new MemoryStream(); WriteU16(m, channelId); var recordsLength = (uint)((entries?.Count ?? 0) * 16); WriteU32(m, recordsLength); if (entries != null) foreach (var (timestamp, offset) in entries) { WriteU64(m, timestamp); WriteU64(m, offset); } WriteRecord(0x07, m.ToArray()); }
        public void WriteChunkIndex(ulong startTime, ulong endTime, ulong chunkOffset, ulong chunkLength, Dictionary<ushort,ulong> messageIndexOffsets, ulong messageIndexLength, string compression, ulong compressedSize, ulong uncompressedSize) { var m = new MemoryStream(); WriteU64(m, startTime); WriteU64(m, endTime); WriteU64(m, chunkOffset); WriteU64(m, chunkLength); var mioLength = (uint)((messageIndexOffsets?.Count ?? 0) * 10); WriteU32(m, mioLength); if (messageIndexOffsets != null) foreach (var (k, v) in messageIndexOffsets) { WriteU16(m, k); WriteU64(m, v); } WriteU64(m, messageIndexLength); WrStr(m, compression); WriteU64(m, compressedSize); WriteU64(m, uncompressedSize); WriteRecord(0x08, m.ToArray()); }
        public void WriteStatistics(ulong messageCount, ushort schemaCount, uint channelCount, uint attachmentCount, uint metadataCount, uint chunkCount, ulong startTime, ulong endTime, Dictionary<ushort,ulong> channelMessageCounts) { var m = new MemoryStream(); WriteU64(m, messageCount); WriteU16(m, schemaCount); WriteU32(m, channelCount); WriteU32(m, attachmentCount); WriteU32(m, metadataCount); WriteU32(m, chunkCount); WriteU64(m, startTime); WriteU64(m, endTime); var cmsLength = (uint)((channelMessageCounts?.Count ?? 0) * 10); WriteU32(m, cmsLength); if (channelMessageCounts != null) foreach (var (k, v) in channelMessageCounts) { WriteU16(m, k); WriteU64(m, v); } WriteRecord(0x0B, m.ToArray()); }
        public void WriteDataEnd() { var m = new MemoryStream(); WriteU32(m, 0); WriteRecord(0x0F, m.ToArray()); }
        public void WriteFooter(ulong summaryStart, ulong summaryOffsetStart, uint summaryCrc) { var m = new MemoryStream(); WriteU64(m, summaryStart); WriteU64(m, summaryOffsetStart); WriteU32(m, summaryCrc); WriteRecord(0x02, m.ToArray()); }
        public void WriteSummaryOffset(byte groupOpcode, ulong start, ulong length) { var m = new MemoryStream(); m.WriteByte(groupOpcode); WriteU64(m, start); WriteU64(m, length); WriteRecord(0x0E, m.ToArray()); }
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
