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
                    var lz4Out = new byte[uncompressedSize];
                    var decoded = LZ4Codec.Decode(data, 0, data.Length, lz4Out, 0, lz4Out.Length);
                    if (decoded != uncompressedSize)
                        throw new InvalidOperationException($"LZ4 decompressed size mismatch: expected {uncompressedSize}, got {decoded}");
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
    }
}
