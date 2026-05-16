// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO/Mcap
// Purpose: Low-level MCAP binary writer - writes MCAP opcodes, records,
// and static LE helpers for fields used by McapRecorder.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.FoxgloveSDK.Util;

namespace Unity.FoxgloveSDK.IO
{
    /// <summary>
    /// Low-level binary writer for MCAP files.
    /// Encodes individual MCAP records (Header, Footer, Schema, Channel, Message, Chunk, Metadata, Statistics, Indexes, SummaryOffsets, DataEnd) and provides static LE helpers for writing primitive fields.
    /// </summary>
    public class McapWriter : IDisposable
    {
        private readonly Stream _stream;
        private readonly bool _leaveOpen;
        private bool _disposed;

        /// <summary>Current byte position in the underlying stream.</summary>
        public long Position => _stream.Position;
        /// <summary>MCAP magic bytes: <c>{ 0x89, M, C, A, P, 0x30, 0x0D, 0x0A }</c>.</summary>
        private static readonly byte[] MagicBytes = { 0x89, (byte)'M', (byte)'C', (byte)'A', (byte)'P', 0x30, 0x0D, 0x0A };

        /// <summary>
        /// MCAP magic bytes. Returns a defensive copy so callers cannot mutate
        /// the process-wide writer/reader constant.
        /// </summary>
        public static byte[] Magic => (byte[])MagicBytes.Clone();

        /// <summary>Internal non-allocating view of the immutable magic bytes.</summary>
        internal static ReadOnlySpan<byte> MagicSpan => MagicBytes;

        /// <summary>Length of the MCAP magic prefix/suffix in bytes.</summary>
        internal const int MagicLength = 8;
        /// <summary>Length of an MCAP record header: opcode byte plus uint64 content length.</summary>
        internal const int RecordHeaderLength = 1 + 8;
        /// <summary>Length of the Footer record content: summary_start, summary_offset_start, summary_crc.</summary>
        internal const int FooterContentLength = 8 + 8 + 4;
        /// <summary>CRC32 field size in bytes.</summary>
        internal const int Crc32SizeBytes = 4;
        /// <summary>Bytes included after summary data when calculating MCAP summary_crc.</summary>
        internal const int FooterCrcPrefixLength = 1 + 8 + 8 + 8;

        internal const byte OpcodeHeader = 0x01;
        internal const byte OpcodeFooter = 0x02;
        internal const byte OpcodeSchema = 0x03;
        internal const byte OpcodeChannel = 0x04;
        internal const byte OpcodeMessage = 0x05;
        internal const byte OpcodeChunk = 0x06;
        internal const byte OpcodeMessageIndex = 0x07;
        internal const byte OpcodeChunkIndex = 0x08;
        internal const byte OpcodeAttachment = 0x09;
        internal const byte OpcodeAttachmentIndex = 0x0A;
        internal const byte OpcodeStatistics = 0x0B;
        internal const byte OpcodeMetadata = 0x0C;
        internal const byte OpcodeMetadataIndex = 0x0D;
        internal const byte OpcodeSummaryOffset = 0x0E;
        internal const byte OpcodeDataEnd = 0x0F;

        /// <summary>Create an MCAP writer that wraps the given stream.</summary>
        public McapWriter(Stream stream, bool leaveOpen = false) { _stream = stream; _leaveOpen = leaveOpen; }

        /// <summary>Write the MCAP magic bytes at the start of the file.</summary>
        public void WriteMagic() => _stream.Write(MagicBytes, 0, MagicBytes.Length);

        /// <summary>Write a raw MCAP record: 1-byte opcode, 8-byte LE length prefix, then the content payload.</summary>
        public void WriteRecord(byte opcode, byte[] content)
        {
            _stream.WriteByte(opcode);
            WriteU64(_stream, (ulong)(content?.Length ?? 0));
            if (content != null && content.Length > 0) _stream.Write(content, 0, content.Length);
        }

        /// <summary>Write the MCAP Header record (opcode <c>0x01</c>).</summary>
        public void WriteHeader(string profile, string library) { var m = new MemoryStream(); WriteString(m, profile); WriteString(m, library); WriteRecord(OpcodeHeader, m.ToArray()); }
        /// <summary>Write a Schema record (opcode <c>0x03</c>).</summary>
        public void WriteSchema(ushort id, string name, string encoding, byte[] data) { var m = new MemoryStream(); WriteU16(m, id); WriteString(m, name); WriteString(m, encoding); WriteLengthPrefixedBytes(m, data ?? new byte[0]); WriteRecord(OpcodeSchema, m.ToArray()); }
        /// <summary>Write a Channel record (opcode <c>0x04</c>).</summary>
        public void WriteChannel(ushort channelId, ushort schemaId, string topic, string encoding, Dictionary<string,string> meta) { var m = new MemoryStream(); WriteU16(m, channelId); WriteU16(m, schemaId); WriteString(m, topic); WriteString(m, encoding); WriteStringMap(m, meta ?? new()); WriteRecord(OpcodeChannel, m.ToArray()); }
        /// <summary>Write a Message record (opcode <c>0x05</c>).</summary>
        public void WriteMessage(ushort channelId, uint seq, ulong logTime, ulong publishTime, byte[] data) { var m = new MemoryStream(); WriteU16(m, channelId); WriteU32(m, seq); WriteU64(m, logTime); WriteU64(m, publishTime); if (data != null) m.Write(data, 0, data.Length); WriteRecord(OpcodeMessage, m.ToArray()); }
        /// <summary>Write a Chunk record (opcode <c>0x06</c>). Chunks contain compressed records plus start/end times, sizes, and checksums.</summary>
        public void WriteChunk(ulong startTime, ulong endTime, ulong uncompressedSize, uint uncompressedCrc, string compression, ulong compressedSize, byte[] records)
            => WriteChunk(startTime, endTime, uncompressedSize, uncompressedCrc, compression, compressedSize,
                new ArraySegment<byte>(records ?? Array.Empty<byte>()));
        /// <summary>Write a Chunk record from an existing byte segment, avoiding a caller-side copy.</summary>
        public void WriteChunk(ulong startTime, ulong endTime, ulong uncompressedSize, uint uncompressedCrc, string compression, ulong compressedSize, ArraySegment<byte> records) { var m = new MemoryStream(); WriteU64(m, startTime); WriteU64(m, endTime); WriteU64(m, uncompressedSize); WriteU32(m, uncompressedCrc); WriteString(m, compression); WriteU64(m, compressedSize); if (records.Array != null && records.Count > 0) m.Write(records.Array, records.Offset, records.Count); WriteRecord(OpcodeChunk, m.ToArray()); }
        /// <summary>Write a Metadata record (opcode <c>0x0C</c>).</summary>
        public void WriteMetadata(string name, Dictionary<string,string> meta) { var m = new MemoryStream(); WriteString(m, name); WriteStringMap(m, meta ?? new()); WriteRecord(OpcodeMetadata, m.ToArray()); }
        /// <summary>Write a Metadata Index record (opcode <c>0x0D</c>).</summary>
        public void WriteMetadataIndex(ulong metadataOffset, ulong metadataLength, string name) { var m = new MemoryStream(); WriteU64(m, metadataOffset); WriteU64(m, metadataLength); WriteString(m, name ?? ""); WriteRecord(OpcodeMetadataIndex, m.ToArray()); }
        /// <summary>Write a Message Index record (opcode <c>0x07</c>).</summary>
        public void WriteMessageIndex(ushort channelId, List<(ulong,ulong)> entries) { var m = new MemoryStream(); WriteU16(m, channelId); var recordsLength = (uint)((entries?.Count ?? 0) * 16); WriteU32(m, recordsLength); if (entries != null) foreach (var (timestamp, offset) in entries) { WriteU64(m, timestamp); WriteU64(m, offset); } WriteRecord(OpcodeMessageIndex, m.ToArray()); }
        /// <summary>Write a Chunk Index record (opcode <c>0x08</c>).</summary>
        public void WriteChunkIndex(ulong startTime, ulong endTime, ulong chunkOffset, ulong chunkLength, Dictionary<ushort,ulong> messageIndexOffsets, ulong messageIndexLength, string compression, ulong compressedSize, ulong uncompressedSize) { var m = new MemoryStream(); WriteU64(m, startTime); WriteU64(m, endTime); WriteU64(m, chunkOffset); WriteU64(m, chunkLength); var mioLength = (uint)((messageIndexOffsets?.Count ?? 0) * 10); WriteU32(m, mioLength); if (messageIndexOffsets != null) foreach (var (k, v) in messageIndexOffsets) { WriteU16(m, k); WriteU64(m, v); } WriteU64(m, messageIndexLength); WriteString(m, compression); WriteU64(m, compressedSize); WriteU64(m, uncompressedSize); WriteRecord(OpcodeChunkIndex, m.ToArray()); }
        /// <summary>Write a Statistics record (opcode <c>0x0B</c>).</summary>
        public void WriteStatistics(ulong messageCount, ushort schemaCount, uint channelCount, uint attachmentCount, uint metadataCount, uint chunkCount, ulong startTime, ulong endTime, Dictionary<ushort,ulong> channelMessageCounts) { var m = new MemoryStream(); WriteU64(m, messageCount); WriteU16(m, schemaCount); WriteU32(m, channelCount); WriteU32(m, attachmentCount); WriteU32(m, metadataCount); WriteU32(m, chunkCount); WriteU64(m, startTime); WriteU64(m, endTime); var cmsLength = (uint)((channelMessageCounts?.Count ?? 0) * 10); WriteU32(m, cmsLength); if (channelMessageCounts != null) foreach (var (k, v) in channelMessageCounts) { WriteU16(m, k); WriteU64(m, v); } WriteRecord(OpcodeStatistics, m.ToArray()); }
        /// <summary>Write the Data End record (opcode <c>0x0F</c>, 4-byte LE zero CRC).</summary>
        public void WriteDataEnd() { var m = new MemoryStream(); WriteU32(m, 0); WriteRecord(OpcodeDataEnd, m.ToArray()); }
        /// <summary>Write the Footer record (opcode <c>0x02</c>). Contains summary offsets and CRC.</summary>
        public void WriteFooter(ulong summaryStart, ulong summaryOffsetStart, uint summaryCrc) { var m = new MemoryStream(); WriteU64(m, summaryStart); WriteU64(m, summaryOffsetStart); WriteU32(m, summaryCrc); WriteRecord(OpcodeFooter, m.ToArray()); }
        /// <summary>Write a Summary Offset record (opcode <c>0x0E</c>).</summary>
        public void WriteSummaryOffset(byte groupOpcode, ulong start, ulong length) { var m = new MemoryStream(); m.WriteByte(groupOpcode); WriteU64(m, start); WriteU64(m, length); WriteRecord(OpcodeSummaryOffset, m.ToArray()); }
        /// <summary>Write an Attachment record (opcode <c>0x09</c>) and return its index for summary registration.</summary>
        public McapAttachmentIndex WriteAttachment(ulong logTime, ulong createTime, string name, string mediaType, byte[] data)
        {
            var off = (ulong)_stream.Position;
            var m = new MemoryStream();
            WriteU64(m, logTime);
            WriteU64(m, createTime);
            WriteString(m, name);
            WriteString(m, mediaType);
            WriteU64(m, (ulong)(data?.Length ?? 0));
            if (data != null && data.Length > 0) m.Write(data, 0, data.Length);
            var content = m.ToArray();
            var crc = Crc32Helper.Compute(content);
            var crcBytes = new byte[Crc32SizeBytes];
            crcBytes[0] = (byte)crc; crcBytes[1] = (byte)(crc >> 8); crcBytes[2] = (byte)(crc >> 16); crcBytes[3] = (byte)(crc >> 24);
            var fullContent = new byte[content.Length + Crc32SizeBytes];
            Buffer.BlockCopy(content, 0, fullContent, 0, content.Length);
            Buffer.BlockCopy(crcBytes, 0, fullContent, content.Length, Crc32SizeBytes);
            WriteRecord(OpcodeAttachment, fullContent);
            var totalLen = (ulong)(RecordHeaderLength + fullContent.Length);
            return new McapAttachmentIndex
            {
                Offset = off,
                Length = totalLen,
                LogTime = logTime,
                CreateTime = createTime,
                DataSize = (ulong)(data?.Length ?? 0),
                Name = name ?? "",
                MediaType = mediaType ?? ""
            };
        }
        /// <summary>Write an Attachment Index record (opcode <c>0x0A</c>).</summary>
        public void WriteAttachmentIndex(McapAttachmentIndex index)
        {
            var m = new MemoryStream();
            WriteU64(m, index.Offset);
            WriteU64(m, index.Length);
            WriteU64(m, index.LogTime);
            WriteU64(m, index.CreateTime);
            WriteU64(m, index.DataSize);
            WriteString(m, index.Name);
            WriteString(m, index.MediaType);
            WriteRecord(OpcodeAttachmentIndex, m.ToArray());
        }
        /// <summary>Write raw bytes directly to the underlying stream without MCAP opcode/length framing.</summary>
        public void WriteBytes(byte[] data) { if (data != null && data.Length > 0) _stream.Write(data, 0, data.Length); }
        /// <summary>Flush the underlying stream.</summary>
        public void Flush()
        {
            if (_disposed)
                return;
            _stream.Flush();
        }
        /// <summary>Flush and, unless <c>leaveOpen</c> was set, dispose the underlying stream.</summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _stream.Flush();
            if (!_leaveOpen)
                _stream.Dispose();
        }

        // Static helpers used by recorder, reader, and tests.

        /// <summary>
        /// Build the footer prefix included in MCAP summary_crc calculation:
        /// footer opcode, footer content length, summary_start, and
        /// summary_offset_start. The summary_crc field itself is excluded.
        /// </summary>
        internal static byte[] BuildFooterCrcPrefix(ulong summaryStart, ulong summaryOffsetStart)
        {
            var prefix = new byte[FooterCrcPrefixLength];
            prefix[0] = OpcodeFooter;
            WriteU64(prefix, 1, (ulong)FooterContentLength);
            WriteU64(prefix, 9, summaryStart);
            WriteU64(prefix, 17, summaryOffsetStart);
            return prefix;
        }

        /// <summary>Write a 16-bit unsigned integer in little-endian byte order.</summary>
        public static void WriteU16(Stream s, ushort v) { s.WriteByte((byte)v); s.WriteByte((byte)(v >> 8)); }
        /// <summary>Write a 32-bit unsigned integer in little-endian byte order.</summary>
        public static void WriteU32(Stream s, uint v) { s.WriteByte((byte)v); s.WriteByte((byte)(v >> 8)); s.WriteByte((byte)(v >> 16)); s.WriteByte((byte)(v >> 24)); }
        /// <summary>Write a 64-bit unsigned integer in little-endian byte order.</summary>
        public static void WriteU64(Stream s, ulong v) { s.WriteByte((byte)v); s.WriteByte((byte)(v >> 8)); s.WriteByte((byte)(v >> 16)); s.WriteByte((byte)(v >> 24)); s.WriteByte((byte)(v >> 32)); s.WriteByte((byte)(v >> 40)); s.WriteByte((byte)(v >> 48)); s.WriteByte((byte)(v >> 56)); }
        private static void WriteU64(byte[] buffer, int offset, ulong v) { buffer[offset] = (byte)v; buffer[offset + 1] = (byte)(v >> 8); buffer[offset + 2] = (byte)(v >> 16); buffer[offset + 3] = (byte)(v >> 24); buffer[offset + 4] = (byte)(v >> 32); buffer[offset + 5] = (byte)(v >> 40); buffer[offset + 6] = (byte)(v >> 48); buffer[offset + 7] = (byte)(v >> 56); }
        /// <summary>Write a UTF-8 string with a 4-byte LE length prefix.</summary>
        public static void WriteString(Stream stream, string value) { var b = Encoding.UTF8.GetBytes(value ?? ""); WriteU32(stream, (uint)b.Length); if (b.Length > 0) stream.Write(b, 0, b.Length); }
        /// <summary>Write raw bytes with a 4-byte LE length prefix.</summary>
        public static void WriteLengthPrefixedBytes(Stream stream, byte[] data) { WriteU32(stream, (uint)(data?.Length ?? 0)); if (data != null && data.Length > 0) stream.Write(data, 0, data.Length); }
        /// <summary>Write a string-to-string map: key-value pairs sorted by key, with a 4-byte LE total-length prefix.</summary>
        public static void WriteStringMap(Stream stream, Dictionary<string,string> map) { var t = new MemoryStream(); foreach (var kv in map.OrderBy(kv => kv.Key, StringComparer.Ordinal)) { WriteString(t, kv.Key); WriteString(t, kv.Value); } var b = t.ToArray(); WriteU32(stream, (uint)b.Length); if (b.Length > 0) stream.Write(b, 0, b.Length); }

        /// <summary>[Obsolete] Use <see cref="WriteString"/>.</summary>
        [Obsolete("Use WriteString.")]
        public static void WrStr(Stream s, string str) => WriteString(s, str);
        /// <summary>[Obsolete] Use <see cref="WriteLengthPrefixedBytes"/>.</summary>
        [Obsolete("Use WriteLengthPrefixedBytes.")]
        public static void WrPrefixed(Stream s, byte[] d) => WriteLengthPrefixedBytes(s, d);
        /// <summary>[Obsolete] Use <see cref="WriteStringMap"/>.</summary>
        [Obsolete("Use WriteStringMap.")]
        public static void WrMap(Stream s, Dictionary<string,string> map) => WriteStringMap(s, map);
    }
}
