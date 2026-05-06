// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO
// Purpose: MCAP chunk compression and decompression — delegates to IonKiwi.lz4 and ZstdSharp.

using System;
using IonKiwi.lz4;
using ZstdSharp;

namespace Unity.FoxgloveSDK.IO
{
    public static class McapCompression
    {
        public static byte[] Decompress(string compression, byte[] data, int uncompressedSize)
        {
            switch (compression)
            {
                case "":
                    return data;
                case "lz4":
                    var lz4Out = LZ4Utility.Decompress(data);
                    if (lz4Out.Length != uncompressedSize)
                        throw new InvalidOperationException($"LZ4 decompressed size mismatch: expected {uncompressedSize}, got {lz4Out.Length}");
                    return lz4Out;
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

        public static byte[] Compress(string compression, byte[] data)
        {
            switch (compression)
            {
                case "":
                    return data;
                case "lz4":
                    return LZ4Utility.Compress(data, LZ4FrameBlockMode.Linked, LZ4FrameBlockSize.Max64KB, LZ4FrameChecksumMode.None, null, false);
                case "zstd":
                    using (var compressor = new Compressor())
                        return compressor.Wrap(data).ToArray();
                default:
                    throw new NotSupportedException($"Unsupported MCAP compression: '{compression}'");
            }
        }
    }
}
