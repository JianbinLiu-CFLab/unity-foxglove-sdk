// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Utilities
// Purpose: IEEE 802.3 CRC32 implementation for MCAP chunk integrity
// verification. Used by McapRecorder (generation) and McapReader
// (validation).

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Util
{
    /// <summary>
    /// IEEE 802.3 CRC32 (polynomial <c>0xEDB88320</c>, reflected lookup)
    /// used by the MCAP format for chunk and footer checksums.
    /// </summary>
    public static class Crc32Helper
    {
        private static readonly uint[] _table = BuildTable();

        /// <summary>
        /// Computes the CRC32 checksum of the given byte span.
        /// Matches <c>System.IO.Hashing.Crc32</c> output and the MCAP spec
        /// reference implementation.
        /// </summary>
        public static uint Compute(ReadOnlySpan<byte> data)
        {
            return Finalize(Update(Initialize(), data));
        }

        /// <summary>
        /// Computes the CRC32 checksum of the given byte array.
        /// </summary>
        public static uint Compute(byte[] data)
        {
            return Compute(new ReadOnlySpan<byte>(data));
        }

        /// <summary>
        /// Computes the CRC32 checksum of exactly <paramref name="length"/>
        /// bytes from the stream's current position.
        /// </summary>
        public static uint Compute(Stream stream, long length)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));

            uint crc = Initialize();
            var buffer = new byte[64 * 1024];
            var remaining = length;
            while (remaining > 0)
            {
                var toRead = (int)Math.Min(buffer.Length, remaining);
                var read = stream.Read(buffer, 0, toRead);
                if (read <= 0)
                    throw new EndOfStreamException("Unexpected end of stream while computing CRC32.");

                crc = Update(crc, new ReadOnlySpan<byte>(buffer, 0, read));
                remaining -= read;
            }

            return Finalize(crc);
        }

        /// <summary>Initializes an incremental CRC32 state.</summary>
        public static uint Initialize() => 0xFFFFFFFF;

        /// <summary>Updates an incremental CRC32 state with more bytes.</summary>
        public static uint Update(uint crc, ReadOnlySpan<byte> data)
        {
            foreach (var b in data)
                crc = (crc >> 8) ^ _table[(crc ^ b) & 0xFF];
            return crc;
        }

        /// <summary>Finalizes an incremental CRC32 state.</summary>
        public static uint Finalize(uint crc) => crc ^ 0xFFFFFFFF;

        private static uint[] BuildTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint crc = i;
                for (int j = 0; j < 8; j++)
                    crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
                table[i] = crc;
            }
            return table;
        }
    }
}
