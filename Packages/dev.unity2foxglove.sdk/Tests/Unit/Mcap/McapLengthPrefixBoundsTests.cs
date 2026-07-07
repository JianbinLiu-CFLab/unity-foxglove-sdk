// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: MCAP length-prefix bounds behavior (migrated from Phase134_8Validation).

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.FoxgloveSDK.IO;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests
{
    /// <summary>
    /// MCAP reader length-prefix bounds and writer-option normalization.
    /// Ported from Phase134_8Validation (checks 134-8A .. 134-8J).
    /// </summary>
    [Trait("Phase", "134-8")]
    [Trait("Domain", "Mcap")]
    public class McapLengthPrefixBoundsTests
    {
        [Fact]
        public void ValidLengthPrefixesStillDecode()
        {
            var stringBuffer = BuildPrefixed(Encoding.UTF8.GetBytes("ok"));
            var stringOffset = 0;
            Assert.True(McapBinaryReader.ReadString(stringBuffer, ref stringOffset) == "ok",
                "134-8A-1: valid string length prefix decodes");
            Assert.True(stringOffset == stringBuffer.Length,
                "134-8A-2: valid string advances offset");

            var bytesBuffer = BuildPrefixed(new byte[] { 1, 2, 3 });
            var bytesOffset = 0;
            var bytes = McapBinaryReader.ReadPrefixed(bytesBuffer, ref bytesOffset);
            Assert.True(bytes.Length == 3 && bytes[0] == 1 && bytes[2] == 3,
                "134-8A-3: valid prefixed bytes decode");
            Assert.True(bytesOffset == bytesBuffer.Length,
                "134-8A-4: valid prefixed bytes advance offset");

            var mapBody = new List<byte>();
            mapBody.AddRange(BuildPrefixed(Encoding.UTF8.GetBytes("k")));
            mapBody.AddRange(BuildPrefixed(Encoding.UTF8.GetBytes("v")));
            var mapBuffer = BuildPrefixed(mapBody.ToArray());
            var mapOffset = 0;
            var map = McapBinaryReader.ReadMap(mapBuffer, ref mapOffset);
            Assert.True(map.Count == 1 && map["k"] == "v",
                "134-8A-5: valid map length prefix decodes");
            Assert.True(mapOffset == mapBuffer.Length,
                "134-8A-6: valid map advances offset");
        }

        [Fact]
        public void OversizedStringLengthPrefixesThrowInvalidDataException()
        {
            foreach (var length in BadLengths())
            {
                var buffer = BuildLengthOnly(length);
                var offset = 0;
                Assert.Throws<InvalidDataException>(() => McapBinaryReader.ReadString(buffer, ref offset));
            }
        }

        [Fact]
        public void OversizedPrefixedByteLengthsThrowInvalidDataException()
        {
            foreach (var length in BadLengths())
            {
                var buffer = BuildLengthOnly(length);
                var offset = 0;
                Assert.Throws<InvalidDataException>(() => McapBinaryReader.ReadPrefixed(buffer, ref offset));
            }
        }

        [Fact]
        public void OversizedMapLengthsThrowInvalidDataException()
        {
            foreach (var length in BadLengths())
            {
                var buffer = BuildLengthOnly(length);
                var offset = 0;
                Assert.Throws<InvalidDataException>(() => McapBinaryReader.ReadMap(buffer, ref offset));
            }
        }

        [Fact]
        public void MapReaderDoesNotEscapeDeclaredMapBounds()
        {
            var body = new List<byte>();
            body.AddRange(BuildPrefixed(Encoding.UTF8.GetBytes("k")));
            body.AddRange(BuildPrefixed(Encoding.UTF8.GetBytes("v")));
            var buffer = new byte[4 + body.Count];
            WriteU32LE(buffer, 0, 5);
            Buffer.BlockCopy(body.ToArray(), 0, buffer, 4, body.Count);
            var offset = 0;
            Assert.Throws<InvalidDataException>(() => McapBinaryReader.ReadMap(buffer, ref offset));
        }

        [Fact]
        public void NonSeekableRecorderStreamFailsBeforeWriting()
        {
            var stream = new NonSeekableMemoryStream();
            Assert.Throws<NotSupportedException>(() => new McapRecorder(stream));
            Assert.True(stream.Length == 0,
                "134-8F-2: rejected non-seekable stream remains untouched");
        }

        [Fact]
        public void CompressionRejectsNullCompressedPayloads()
        {
            Assert.Throws<InvalidDataException>(() => McapCompression.Decompress("lz4", null, 0));
            Assert.Throws<InvalidDataException>(() => McapCompression.Decompress("zstd", null, 0));
        }

        [Fact]
        public void CompressionRejectsNullUncompressedPayloadWithClearMessage()
        {
            var ex = Assert.Throws<InvalidDataException>(() => McapCompression.Decompress("", null, 1, 1024));

            Assert.Contains("null", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void CompressionDocumentsZeroMaxOutputAsUnbounded()
        {
            var payload = new byte[] { 1, 2, 3, 4 };
            var result = McapCompression.Decompress("", payload, payload.Length, maxOutputBytes: 0);

            Assert.Equal(payload, result);
        }

        [Fact]
        public void CompressionSizeMismatchesThrowInvalidDataException()
        {
            var payload = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

            var lz4 = McapCompression.Compress("lz4", payload);
            Assert.Throws<InvalidDataException>(() => McapCompression.Decompress("lz4", lz4, payload.Length + 2));

            var zstd = McapCompression.Compress("zstd", payload);
            Assert.Throws<InvalidDataException>(() => McapCompression.Decompress("zstd", zstd, payload.Length + 2));
        }

        [Fact]
        public void WriterOptionsNormalizeUpperBoundsAndLz4Policy()
        {
            var oversized = McapWriterOptions.Normalize(new McapWriterOptions { ChunkSizeBytes = int.MaxValue });
            Assert.True(oversized.ChunkSizeBytes == McapWriterOptions.MaxChunkSizeBytes,
                "134-8H-1: writer options clamp oversized chunk size");
            var defaults = McapWriterOptions.Normalize(null);
            Assert.True(defaults.Lz4CompressionLevel == McapWriterOptions.DefaultLz4CompressionLevel,
                "134-8H-2: writer options expose explicit default lz4 compression policy");
        }

        [Fact]
        public void InvalidProtobufSchemaDoesNotAllocateSchemaOrChannel()
        {
            using var ms = new MemoryStream();
            using (var recorder = new McapRecorder(ms))
            {
                recorder.AddChannel(1, "/bad", "protobuf", "Bad", "protobuf", "not valid base64");
                recorder.WriteMessage(1, 0, new byte[] { 1 });
                recorder.Close();
            }

            ms.Position = 0;
            var summary = new McapReader(ms).ReadSummary();
            Assert.True(summary.Schemas.Count == 0 && summary.Channels.Count == 0,
                "134-8I: invalid protobuf schema content fails before allocating schema/channel ids");
        }

        [Fact]
        public void AttachmentCrcValidityIsReaderOwned()
        {
            var property = typeof(McapAttachment).GetProperty(nameof(McapAttachment.CrcValid));
            Assert.True(property != null && property.SetMethod != null && property.SetMethod.IsAssembly,
                "134-8J: attachment CRC validity is mutable only inside the runtime assembly");
        }

        private static IEnumerable<uint> BadLengths()
        {
            yield return int.MaxValue;
            yield return (uint)int.MaxValue + 1U;
            yield return uint.MaxValue;
        }

        private static byte[] BuildLengthOnly(uint length)
        {
            var buffer = new byte[4];
            WriteU32LE(buffer, 0, length);
            return buffer;
        }

        private static byte[] BuildPrefixed(byte[] payload)
        {
            payload ??= Array.Empty<byte>();
            var buffer = new byte[4 + payload.Length];
            WriteU32LE(buffer, 0, (uint)payload.Length);
            Buffer.BlockCopy(payload, 0, buffer, 4, payload.Length);
            return buffer;
        }

        private static void WriteU32LE(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }

        private sealed class NonSeekableMemoryStream : MemoryStream
        {
            public override bool CanSeek => false;
            public override long Position
            {
                get => base.Position;
                set => throw new NotSupportedException();
            }

            public override long Seek(long offset, SeekOrigin loc) => throw new NotSupportedException();
        }
    }
}
