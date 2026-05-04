using System;
using K4os.Compression.LZ4;
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
                    var lz4Out = LZ4Pickler.Unpickle(data);
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
                    return LZ4Pickler.Pickle(data, LZ4Level.L00_FAST);
                case "zstd":
                    using (var compressor = new Compressor())
                        return compressor.Wrap(data).ToArray();
                default:
                    throw new NotSupportedException($"Unsupported MCAP compression: '{compression}'");
            }
        }
    }
}
