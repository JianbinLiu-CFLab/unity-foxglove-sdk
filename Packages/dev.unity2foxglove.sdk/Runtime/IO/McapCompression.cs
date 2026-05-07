// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO
// Purpose: MCAP chunk compression and decompression — delegates to K4os.Compression.LZ4 and ZstdSharp.

using System;
using System.IO;
using K4os.Compression.LZ4.Streams;
using ZstdSharp;

namespace Unity.FoxgloveSDK.IO
{
    /// <summary>
    /// MCAP chunk compression and decompression helpers.
    /// Supports <c>""</c> (no-op), <c>"lz4"</c> (via K4os.Compression.LZ4), and <c>"zstd"</c> (via ZstdSharp).
    /// </summary>
    public static class McapCompression
    {
        /// <summary>Decompress MCAP chunk data using the specified compression algorithm.</summary>
        public static byte[] Decompress(string compression, byte[] data, int uncompressedSize)
        {
            switch (compression)
            {
                case "":
                    return data;
                case "lz4":
                    using (var ms = new MemoryStream(data))
                    using (var lz4 = LZ4Stream.Decode(ms, leaveOpen: false))
                    {
                        var buf = new byte[uncompressedSize];
                        var total = 0;
                        while (total < uncompressedSize)
                        {
                            var read = lz4.Read(buf, total, uncompressedSize - total);
                            if (read == 0) break;
                            total += read;
                        }
                        if (total != uncompressedSize)
                            throw new InvalidOperationException($"LZ4 decompressed size mismatch: expected {uncompressedSize}, got {total}");
                        return buf;
                    }
                case "zstd":
                    using (var decompressor = new Decompressor())
                    {
                        var span = decompressor.Unwrap(data, uncompressedSize);
                        if (span.Length != uncompressedSize)
                            throw new InvalidOperationException($"Zstd decompressed size mismatch: expected {uncompressedSize}, got {span.Length}");
                        return span.ToArray();
                    }
                default:
                    throw new NotSupportedException($"Unsupported MCAP compression: '{compression}'");
            }
        }

        /// <summary>Compress raw bytes using the specified compression algorithm.</summary>
        public static byte[] Compress(string compression, byte[] data)
        {
            switch (compression)
            {
                case "":
                    return data;
                case "lz4":
                    using (var ms = new MemoryStream())
                    {
                        using (var lz4 = LZ4Stream.Encode(ms, K4os.Compression.LZ4.LZ4Level.L12_MAX, leaveOpen: true))
                            lz4.Write(data, 0, data.Length);
                        return ms.ToArray();
                    }
                case "zstd":
                    using (var compressor = new Compressor())
                        return compressor.Wrap(data).ToArray();
                default:
                    throw new NotSupportedException($"Unsupported MCAP compression: '{compression}'");
            }
        }
    }
}
