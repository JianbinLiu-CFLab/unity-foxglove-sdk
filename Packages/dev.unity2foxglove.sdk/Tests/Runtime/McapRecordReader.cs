using System;
using System.Collections.Generic;
using System.Text;
using Unity.FoxgloveSDK.IO;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Test-only sequential MCAP reader for verification of generated files.
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
                hasLeadingMagic = MatchesMagic(data, offset);
                if (hasLeadingMagic) offset += 8;
            }

            while (offset + 9 <= data.Length)
            {
                var opcode = data[offset++];
                var len = ReadU64LE(data, ref offset);
                if (offset + (int)len > data.Length) break;
                var content = new byte[len];
                if (len > 0) Buffer.BlockCopy(data, offset, content, 0, (int)len);
                offset += (int)len;
                records.Add(new McapRecord { Opcode = opcode, Content = content });
            }

            if (offset + 8 <= data.Length)
            {
                hasTrailingMagic = MatchesMagic(data, offset);
            }

            return (hasLeadingMagic, records, hasTrailingMagic);
        }

        public static bool MatchesMagic(byte[] buf, int offset)
        {
            var magic = IO.McapWriter.Magic;
            for (var i = 0; i < magic.Length; i++)
                if (buf[offset + i] != magic[i]) return false;
            return true;
        }

        // ── Decode helpers ──

        public static (string profile, string library) DecodeHeader(byte[] content)
        {
            var off = 0;
            return (ReadString(content, ref off), ReadString(content, ref off));
        }

        public static (ushort id, string name, string encoding, byte[] data) DecodeSchema(byte[] content)
        {
            var off = 0;
            var id = ReadU16LE(content, ref off);
            var name = ReadString(content, ref off);
            var encoding = ReadString(content, ref off);
            var dataLen = ReadU32LE(content, ref off);
            var data = new byte[dataLen];
            Buffer.BlockCopy(content, off, data, 0, (int)dataLen);
            return (id, name, encoding, data);
        }

        public static (ushort id, ushort schemaId, string topic, string encoding) DecodeChannel(byte[] content)
        {
            var off = 0;
            var id = ReadU16LE(content, ref off);
            var schemaId = ReadU16LE(content, ref off);
            var topic = ReadString(content, ref off);
            var encoding = ReadString(content, ref off);
            return (id, schemaId, topic, encoding);
        }

        public static (ushort channelId, uint sequence, ulong logTime, ulong publishTime, byte[] data) DecodeMessage(byte[] content)
        {
            var off = 0;
            var chId = ReadU16LE(content, ref off);
            var seq = ReadU32LE(content, ref off);
            var logTime = ReadU64LE(content, ref off);
            var pubTime = ReadU64LE(content, ref off);
            var data = new byte[content.Length - off];
            Buffer.BlockCopy(content, off, data, 0, content.Length - off);
            return (chId, seq, logTime, pubTime, data);
        }

        public static (ulong startTime, ulong endTime, ulong uncompressedSize, uint crc, string compression, ulong compressedSize, byte[] records) DecodeChunk(byte[] content)
        {
            var off = 0;
            var st = ReadU64LE(content, ref off);
            var et = ReadU64LE(content, ref off);
            var size = ReadU64LE(content, ref off);
            var crc = ReadU32LE(content, ref off);
            var comp = ReadString(content, ref off);
            var compSize = ReadU64LE(content, ref off);
            var recs = new byte[content.Length - off];
            Buffer.BlockCopy(content, off, recs, 0, recs.Length);
            return (st, et, size, crc, comp, compSize, recs);
        }

        public static (ushort channelId, List<(ulong, ulong)> entries) DecodeMessageIndex(byte[] content)
        {
            var off = 0;
            var chId = ReadU16LE(content, ref off);
            var recsLen = ReadU32LE(content, ref off);
            var count = recsLen / 16;
            var entries = new List<(ulong, ulong)>();
            for (uint i = 0; i < count; i++)
            {
                var ts = ReadU64LE(content, ref off);
                var o = ReadU64LE(content, ref off);
                entries.Add((ts, o));
            }
            return (chId, entries);
        }

        public static (ulong summaryStart, ulong summaryOffsetStart, uint summaryCrc) DecodeFooter(byte[] content)
        {
            var off = 0;
            return (ReadU64LE(content, ref off), ReadU64LE(content, ref off), ReadU32LE(content, ref off));
        }

        // ── Low-level LE readers ──

        public static ushort ReadU16LE(byte[] buf, ref int offset) { var v = (ushort)(buf[offset] | (buf[offset + 1] << 8)); offset += 2; return v; }
        public static uint ReadU32LE(byte[] buf, ref int offset) { var v = (uint)(buf[offset] | (buf[offset + 1] << 8) | (buf[offset + 2] << 16) | (buf[offset + 3] << 24)); offset += 4; return v; }
        public static ulong ReadU64LE(byte[] buf, ref int offset)
        {
            var v = (ulong)buf[offset] | ((ulong)buf[offset + 1] << 8) | ((ulong)buf[offset + 2] << 16) | ((ulong)buf[offset + 3] << 24)
                  | ((ulong)buf[offset + 4] << 32) | ((ulong)buf[offset + 5] << 40) | ((ulong)buf[offset + 6] << 48) | ((ulong)buf[offset + 7] << 56);
            offset += 8; return v;
        }

        public static string ReadString(byte[] buf, ref int offset)
        {
            var len = ReadU32LE(buf, ref offset);
            var s = Encoding.UTF8.GetString(buf, offset, (int)len);
            offset += (int)len;
            return s;
        }
    }
}
