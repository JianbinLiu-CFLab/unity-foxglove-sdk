// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO/Mcap
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
        /// <summary>Read a 16-bit unsigned integer in little-endian byte order, advancing <c>off</c> by 2.</summary>
        public static ushort ReadU16LE(byte[] buf, ref int off)
        {
            if (off + 2 > buf.Length) throw new InvalidDataException($"Truncated U16 at offset {off}");
            var v = (ushort)(buf[off] | (buf[off + 1] << 8)); off += 2; return v;
        }

        /// <summary>Read a 32-bit unsigned integer in little-endian byte order, advancing <c>off</c> by 4.</summary>
        public static uint ReadU32LE(byte[] buf, ref int off)
        {
            if (off + 4 > buf.Length) throw new InvalidDataException($"Truncated U32 at offset {off}");
            var v = (uint)(buf[off] | (buf[off + 1] << 8) | (buf[off + 2] << 16) | (buf[off + 3] << 24)); off += 4; return v;
        }

        /// <summary>Read a 64-bit unsigned integer in little-endian byte order, advancing <c>off</c> by 8.</summary>
        public static ulong ReadU64LE(byte[] buf, ref int off)
        {
            if (off + 8 > buf.Length) throw new InvalidDataException($"Truncated U64 at offset {off}");
            var v = (ulong)buf[off] | ((ulong)buf[off + 1] << 8) | ((ulong)buf[off + 2] << 16) | ((ulong)buf[off + 3] << 24)
                  | ((ulong)buf[off + 4] << 32) | ((ulong)buf[off + 5] << 40) | ((ulong)buf[off + 6] << 48) | ((ulong)buf[off + 7] << 56);
            off += 8; return v;
        }

        /// <summary>Read a UTF-8 string prefixed by a 4-byte LE length, advancing <c>off</c> accordingly.</summary>
        public static string ReadString(byte[] buf, ref int off)
        {
            if (off + 4 > buf.Length) throw new InvalidDataException($"Truncated string length at offset {off}");
            var len = ReadSupportedLength(buf, ref off, "string");
            EnsureAvailable(buf, off, len, "string data");
            var s = Encoding.UTF8.GetString(buf, off, len);
            off += len;
            return s;
        }

        /// <summary>Read raw bytes prefixed by a 4-byte LE length, advancing <c>off</c> accordingly.</summary>
        public static byte[] ReadPrefixed(byte[] buf, ref int off)
        {
            if (off + 4 > buf.Length) throw new InvalidDataException($"Truncated prefixed length at offset {off}");
            var len = ReadSupportedLength(buf, ref off, "prefixed");
            EnsureAvailable(buf, off, len, "prefixed data");
            var data = new byte[len];
            System.Buffer.BlockCopy(buf, off, data, 0, len);
            off += len;
            return data;
        }

        /// <summary>Read a string-to-string map: 4-byte LE total-length prefix followed by alternating key/value pairs.</summary>
        public static Dictionary<string, string> ReadMap(byte[] buf, ref int off)
        {
            var map = new Dictionary<string, string>();
            if (off + 4 > buf.Length) throw new InvalidDataException($"Truncated map length at offset {off}");
            var totalBytes = ReadSupportedLength(buf, ref off, "map");
            EnsureAvailable(buf, off, totalBytes, "map data");
            var end = off + totalBytes;
            while (off < end)
            {
                var k = ReadStringWithin(buf, ref off, end, "map key");
                var v = ReadStringWithin(buf, ref off, end, "map value");
                map[k] = v;
            }

            if (off != end)
                throw new InvalidDataException("Map reader ended outside declared map length.");

            return map;
        }

        private static string ReadStringWithin(byte[] buf, ref int off, int end, string valueName)
        {
            EnsureAvailableWithin(buf, off, 4, end, valueName + " length");
            var len = ReadSupportedLength(buf, ref off, valueName);
            EnsureAvailableWithin(buf, off, len, end, valueName + " data");
            var value = Encoding.UTF8.GetString(buf, off, len);
            off += len;
            return value;
        }

        private static int ReadSupportedLength(byte[] buf, ref int off, string valueName)
        {
            var len = ReadU32LE(buf, ref off);
            if (len > int.MaxValue)
                throw new InvalidDataException($"{valueName} length exceeds supported size.");
            return (int)len;
        }

        private static void EnsureAvailable(byte[] buf, int off, int len, string valueName)
        {
            if (len > buf.Length - off)
                throw new InvalidDataException($"Truncated {valueName} at offset {off}");
        }

        private static void EnsureAvailableWithin(byte[] buf, int off, int len, int end, string valueName)
        {
            if (off < 0 || end < off || end > buf.Length || len > end - off)
                throw new InvalidDataException($"Truncated {valueName} at offset {off}");
        }

        /// <summary>Check whether the bytes at <c>off</c> match the MCAP magic bytes.</summary>
        public static bool MatchesMagic(byte[] buf, int off)
        {
            var magic = McapConstants.MagicSpan;
            if (buf == null || off < 0 || off > buf.Length - magic.Length) return false;
            for (var i = 0; i < magic.Length; i++)
                if (buf[off + i] != magic[i]) return false;
            return true;
        }
    }
}
