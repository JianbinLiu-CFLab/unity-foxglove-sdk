// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO/Mcap
// Purpose: MCAP chunk compression and decompression — delegates to K4os.Compression.LZ4 and ZstdSharp.

using System;
using System.IO;
using K4os.Compression.LZ4;
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
            => Decompress(compression, data, uncompressedSize, (int)McapReader.DefaultChunkUncompressedSizeLimit);

        /// <summary>Decompress MCAP chunk data while bounding the retained output size.</summary>
        public static byte[] Decompress(string compression, byte[] data, int uncompressedSize, int maxOutputBytes)
        {
            if (data == null && compression == "lz4")
                throw new InvalidDataException("LZ4 chunk data is null.");
            if (data == null && compression == "zstd")
                throw new InvalidDataException("Zstd chunk data is null.");
            return Decompress(
                compression,
                new ArraySegment<byte>(data ?? Array.Empty<byte>()),
                uncompressedSize,
                maxOutputBytes);
        }

        /// <summary>Decompress an MCAP chunk segment while bounding the retained output size.</summary>
        public static byte[] Decompress(
            string compression,
            ArraySegment<byte> data,
            int uncompressedSize,
            int maxOutputBytes)
        {
            if (uncompressedSize < 0)
                throw new InvalidDataException("Uncompressed chunk size cannot be negative.");
            if (maxOutputBytes < 0)
                throw new ArgumentOutOfRangeException(nameof(maxOutputBytes), "Maximum decompressed output bytes cannot be negative.");
            if (maxOutputBytes > 0 && uncompressedSize > maxOutputBytes)
                throw new InvalidDataException(
                    $"Uncompressed chunk size {uncompressedSize} exceeds maximum output size {maxOutputBytes}.");

            switch (compression)
            {
                case "":
                    if (data.Count != uncompressedSize)
                        throw new InvalidDataException(
                            $"Uncompressed chunk size mismatch: expected {uncompressedSize}, got {data.Count}");
                    if (data.Count == 0)
                        return Array.Empty<byte>();
                    if (data.Offset == 0 && data.Array != null && data.Count == data.Array.Length)
                        return data.Array;
                    var uncompressedCopy = new byte[data.Count];
                    Buffer.BlockCopy(data.Array, data.Offset, uncompressedCopy, 0, data.Count);
                    return uncompressedCopy;
                case "lz4":
                    if (data.Array == null)
                        throw new InvalidDataException("LZ4 chunk data is null.");
                    using (var ms = new MemoryStream(data.Array, data.Offset, data.Count, writable: false))
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
                    if (data.Array == null)
                        throw new InvalidDataException("Zstd chunk data is null.");
                    using (var decompressor = new Decompressor())
                    {
                        var output = new byte[uncompressedSize];
                        var written = decompressor.Unwrap(
                            data.Array,
                            data.Offset,
                            data.Count,
                            output,
                            0,
                            output.Length);
                        if (written != uncompressedSize)
                            throw new InvalidOperationException($"Zstd decompressed size mismatch: expected {uncompressedSize}, got {written}");
                        return output;
                    }
                default:
                    throw new NotSupportedException($"Unsupported MCAP compression: '{compression}'");
            }
        }

        /// <summary>Compress raw bytes using the specified compression algorithm.</summary>
        public static byte[] Compress(string compression, byte[] data)
        {
            var compressed = Compress(compression, new ArraySegment<byte>(data ?? Array.Empty<byte>()));
            if (compressed.Array == null || compressed.Count == 0)
                return Array.Empty<byte>();
            if (compressed.Offset == 0 && compressed.Count == compressed.Array.Length)
                return compressed.Array;
            var copy = new byte[compressed.Count];
            Buffer.BlockCopy(compressed.Array, compressed.Offset, copy, 0, compressed.Count);
            return copy;
        }

        /// <summary>Compress raw bytes from an existing segment, preserving no-op chunks without copying.</summary>
        public static ArraySegment<byte> Compress(string compression, ArraySegment<byte> data)
            => Compress(compression, data, McapWriterOptions.DefaultLz4CompressionLevel);

        /// <summary>Compress raw bytes from an existing segment with an explicit LZ4 level.</summary>
        public static ArraySegment<byte> Compress(string compression, ArraySegment<byte> data, LZ4Level lz4Level)
        {
            var sourceArray = data.Array ?? Array.Empty<byte>();
            var sourceOffset = data.Array == null ? 0 : data.Offset;
            var sourceCount = data.Array == null ? 0 : data.Count;

            switch (compression)
            {
                case "":
                    return data.Array == null
                        ? new ArraySegment<byte>(Array.Empty<byte>())
                        : data;
                case "lz4":
                    using (var ms = new MemoryStream())
                    {
                        using (var lz4 = LZ4Stream.Encode(ms, lz4Level, leaveOpen: true))
                            lz4.Write(sourceArray, sourceOffset, sourceCount);
                        return new ArraySegment<byte>(ms.ToArray());
                    }
                case "zstd":
                    using (var compressor = new Compressor())
                    {
                        var output = new byte[Compressor.GetCompressBound(sourceCount)];
                        var compressedSize = compressor.Wrap(
                            new ArraySegment<byte>(sourceArray, sourceOffset, sourceCount),
                            new ArraySegment<byte>(output));
                        return new ArraySegment<byte>(output, 0, compressedSize);
                    }
                default:
                    throw new NotSupportedException($"Unsupported MCAP compression: '{compression}'");
            }
        }
    }
}
