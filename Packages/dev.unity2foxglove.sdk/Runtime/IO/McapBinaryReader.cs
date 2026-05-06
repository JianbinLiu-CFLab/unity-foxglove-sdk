// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO
// Purpose: Low-level little-endian binary read helpers for MCAP record parsing.

using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Unity.FoxgloveSDK.IO
{
    /// <summary>
    /// Low-level LE binary read helpers for MCAP records.
    /// Shared by Runtime McapReader and Tests McapRecordReader.
    /// </summary>
    public static class McapBinaryReader
    {
        public static ushort ReadU16LE(byte[] buf, ref int off)
        {
            if (off + 2 > buf.Length) throw new InvalidDataException($"Truncated U16 at offset {off}");
            var v = (ushort)(buf[off] | (buf[off + 1] << 8)); off += 2; return v;
        }

        public static uint ReadU32LE(byte[] buf, ref int off)
        {
            if (off + 4 > buf.Length) throw new InvalidDataException($"Truncated U32 at offset {off}");
            var v = (uint)(buf[off] | (buf[off + 1] << 8) | (buf[off + 2] << 16) | (buf[off + 3] << 24)); off += 4; return v;
        }

        public static ulong ReadU64LE(byte[] buf, ref int off)
        {
            if (off + 8 > buf.Length) throw new InvalidDataException($"Truncated U64 at offset {off}");
            var v = (ulong)buf[off] | ((ulong)buf[off + 1] << 8) | ((ulong)buf[off + 2] << 16) | ((ulong)buf[off + 3] << 24)
                  | ((ulong)buf[off + 4] << 32) | ((ulong)buf[off + 5] << 40) | ((ulong)buf[off + 6] << 48) | ((ulong)buf[off + 7] << 56);
            off += 8; return v;
        }

        public static string ReadString(byte[] buf, ref int off)
        {
            if (off + 4 > buf.Length) throw new InvalidDataException($"Truncated string length at offset {off}");
            var len = ReadU32LE(buf, ref off);
            if (off + (int)len > buf.Length) throw new InvalidDataException($"Truncated string data at offset {off}");
            var s = Encoding.UTF8.GetString(buf, off, (int)len);
            off += (int)len;
            return s;
        }

        public static byte[] ReadPrefixed(byte[] buf, ref int off)
        {
            if (off + 4 > buf.Length) throw new InvalidDataException($"Truncated prefixed length at offset {off}");
            var len = ReadU32LE(buf, ref off);
            if (off + (int)len > buf.Length) throw new InvalidDataException($"Truncated prefixed data at offset {off}");
            var data = new byte[len];
            System.Buffer.BlockCopy(buf, off, data, 0, (int)len);
            off += (int)len;
            return data;
        }

        public static Dictionary<string, string> ReadMap(byte[] buf, ref int off)
        {
            var map = new Dictionary<string, string>();
            if (off + 4 > buf.Length) throw new InvalidDataException($"Truncated map length at offset {off}");
            var totalBytes = ReadU32LE(buf, ref off);
            if (off + (int)totalBytes > buf.Length) throw new InvalidDataException($"Truncated map data at offset {off}");
            var end = off + (int)totalBytes;
            while (off < end)
            {
                var k = ReadString(buf, ref off);
                var v = ReadString(buf, ref off);
                map[k] = v;
            }
            return map;
        }

        public static bool MatchesMagic(byte[] buf, int off)
        {
            var magic = McapWriter.Magic;
            if (off + magic.Length > buf.Length) return false;
            for (var i = 0; i < magic.Length; i++)
                if (buf[off + i] != magic[i]) return false;
            return true;
        }
    }
}
