using System;
using System.Collections.Generic;
using System.Text;
using Unity.FoxgloveSDK.IO;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Test-only sequential MCAP reader for verification of generated files.
    /// Uses McapBinaryReader for LE helpers.
    /// </summary>
    public static class McapRecordReader
    {
        public struct McapRecord
        {
            public byte Opcode;
            public byte[] Content;
        }

        public static (bool hasLeadingMagic, List<McapRecord> records, bool hasTrailingMagic) Parse(byte[] data)
        {
            var records = new List<McapRecord>();
            var offset = 0;
            var hasLeadingMagic = false;
            var hasTrailingMagic = false;

            if (offset + 8 <= data.Length)
            {
                hasLeadingMagic = McapBinaryReader.MatchesMagic(data, offset);
                if (hasLeadingMagic) offset += 8;
            }

            while (offset + 9 <= data.Length)
            {
                var opcode = data[offset++];
                var len = McapBinaryReader.ReadU64LE(data, ref offset);
                if (offset + (int)len > data.Length) break;
                var content = new byte[len];
                if (len > 0) Buffer.BlockCopy(data, offset, content, 0, (int)len);
                offset += (int)len;
                records.Add(new McapRecord { Opcode = opcode, Content = content });
            }

            if (offset + 8 <= data.Length)
            {
                hasTrailingMagic = McapBinaryReader.MatchesMagic(data, offset);
            }

            return (hasLeadingMagic, records, hasTrailingMagic);
        }

        // ── Decode helpers ──

        public static (string profile, string library) DecodeHeader(byte[] content)
        {
            var off = 0;
            return (McapBinaryReader.ReadString(content, ref off), McapBinaryReader.ReadString(content, ref off));
        }

        public static (ushort id, string name, string encoding, byte[] data) DecodeSchema(byte[] content)
        {
            var off = 0;
            var id = McapBinaryReader.ReadU16LE(content, ref off);
            var name = McapBinaryReader.ReadString(content, ref off);
            var encoding = McapBinaryReader.ReadString(content, ref off);
            var data = McapBinaryReader.ReadPrefixed(content, ref off);
            return (id, name, encoding, data);
        }

        public static (ushort id, ushort schemaId, string topic, string encoding) DecodeChannel(byte[] content)
        {
            var off = 0;
            var id = McapBinaryReader.ReadU16LE(content, ref off);
            var schemaId = McapBinaryReader.ReadU16LE(content, ref off);
            var topic = McapBinaryReader.ReadString(content, ref off);
            var encoding = McapBinaryReader.ReadString(content, ref off);
            return (id, schemaId, topic, encoding);
        }

        public static (ushort channelId, uint sequence, ulong logTime, ulong publishTime, byte[] data) DecodeMessage(byte[] content)
        {
            var off = 0;
            var chId = McapBinaryReader.ReadU16LE(content, ref off);
            var seq = McapBinaryReader.ReadU32LE(content, ref off);
            var logTime = McapBinaryReader.ReadU64LE(content, ref off);
            var pubTime = McapBinaryReader.ReadU64LE(content, ref off);
            var data = new byte[content.Length - off];
            Buffer.BlockCopy(content, off, data, 0, content.Length - off);
            return (chId, seq, logTime, pubTime, data);
        }

        public static (ulong startTime, ulong endTime, ulong uncompressedSize, uint crc, string compression, ulong compressedSize, byte[] records) DecodeChunk(byte[] content)
        {
            var off = 0;
            var st = McapBinaryReader.ReadU64LE(content, ref off);
            var et = McapBinaryReader.ReadU64LE(content, ref off);
            var size = McapBinaryReader.ReadU64LE(content, ref off);
            var crc = McapBinaryReader.ReadU32LE(content, ref off);
            var comp = McapBinaryReader.ReadString(content, ref off);
            var compSize = McapBinaryReader.ReadU64LE(content, ref off);
            var recs = new byte[content.Length - off];
            Buffer.BlockCopy(content, off, recs, 0, recs.Length);
            return (st, et, size, crc, comp, compSize, recs);
        }

        public static (ushort channelId, List<(ulong, ulong)> entries) DecodeMessageIndex(byte[] content)
        {
            var off = 0;
            var chId = McapBinaryReader.ReadU16LE(content, ref off);
            var recsLen = McapBinaryReader.ReadU32LE(content, ref off);
            var count = recsLen / 16;
            var entries = new List<(ulong, ulong)>();
            for (uint i = 0; i < count; i++)
            {
                var ts = McapBinaryReader.ReadU64LE(content, ref off);
                var o = McapBinaryReader.ReadU64LE(content, ref off);
                entries.Add((ts, o));
            }
            return (chId, entries);
        }

        public static (ulong summaryStart, ulong summaryOffsetStart, uint summaryCrc) DecodeFooter(byte[] content)
        {
            var off = 0;
            return (McapBinaryReader.ReadU64LE(content, ref off), McapBinaryReader.ReadU64LE(content, ref off), McapBinaryReader.ReadU32LE(content, ref off));
        }
    }
}
