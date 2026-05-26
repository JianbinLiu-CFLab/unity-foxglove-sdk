// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 134-8 regression coverage for MCAP length-prefix bounds.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.FoxgloveSDK.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase134_8Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 134-8: MCAP Writer And Recording Pipeline ===");
            _passed = 0;

            ValidLengthPrefixesStillDecode();
            OversizedStringLengthPrefixesThrowInvalidDataException();
            OversizedPrefixedByteLengthsThrowInvalidDataException();
            OversizedMapLengthsThrowInvalidDataException();
            MapReaderDoesNotEscapeDeclaredMapBounds();
            NonSeekableRecorderStreamFailsBeforeWriting();
            CompressionRejectsNullCompressedPayloads();
            WriterOptionsNormalizeUpperBoundsAndLz4Policy();
            InvalidProtobufSchemaDoesNotAllocateSchemaOrChannel();
            AttachmentCrcValidityIsReaderOwned();

            Console.WriteLine($"Phase 134-8: {_passed} checks passed.");
        }

        private static void ValidLengthPrefixesStillDecode()
        {
            var stringBuffer = BuildPrefixed(Encoding.UTF8.GetBytes("ok"));
            var stringOffset = 0;
            Check(McapBinaryReader.ReadString(stringBuffer, ref stringOffset) == "ok",
                "134-8A-1: valid string length prefix decodes");
            Check(stringOffset == stringBuffer.Length,
                "134-8A-2: valid string advances offset");

            var bytesBuffer = BuildPrefixed(new byte[] { 1, 2, 3 });
            var bytesOffset = 0;
            var bytes = McapBinaryReader.ReadPrefixed(bytesBuffer, ref bytesOffset);
            Check(bytes.Length == 3 && bytes[0] == 1 && bytes[2] == 3,
                "134-8A-3: valid prefixed bytes decode");
            Check(bytesOffset == bytesBuffer.Length,
                "134-8A-4: valid prefixed bytes advance offset");

            var mapBody = new List<byte>();
            mapBody.AddRange(BuildPrefixed(Encoding.UTF8.GetBytes("k")));
            mapBody.AddRange(BuildPrefixed(Encoding.UTF8.GetBytes("v")));
            var mapBuffer = BuildPrefixed(mapBody.ToArray());
            var mapOffset = 0;
            var map = McapBinaryReader.ReadMap(mapBuffer, ref mapOffset);
            Check(map.Count == 1 && map["k"] == "v",
                "134-8A-5: valid map length prefix decodes");
            Check(mapOffset == mapBuffer.Length,
                "134-8A-6: valid map advances offset");
        }

        private static void OversizedStringLengthPrefixesThrowInvalidDataException()
        {
            foreach (var length in BadLengths())
            {
                var buffer = BuildLengthOnly(length);
                var offset = 0;
                Check(ThrowsInvalidData(() => McapBinaryReader.ReadString(buffer, ref offset)),
                    $"134-8B: string length {length} throws InvalidDataException");
            }
        }

        private static void OversizedPrefixedByteLengthsThrowInvalidDataException()
        {
            foreach (var length in BadLengths())
            {
                var buffer = BuildLengthOnly(length);
                var offset = 0;
                Check(ThrowsInvalidData(() => McapBinaryReader.ReadPrefixed(buffer, ref offset)),
                    $"134-8C: prefixed byte length {length} throws InvalidDataException");
            }
        }

        private static void OversizedMapLengthsThrowInvalidDataException()
        {
            foreach (var length in BadLengths())
            {
                var buffer = BuildLengthOnly(length);
                var offset = 0;
                Check(ThrowsInvalidData(() => McapBinaryReader.ReadMap(buffer, ref offset)),
                    $"134-8D: map length {length} throws InvalidDataException");
            }
        }

        private static void MapReaderDoesNotEscapeDeclaredMapBounds()
        {
            var body = new List<byte>();
            body.AddRange(BuildPrefixed(Encoding.UTF8.GetBytes("k")));
            body.AddRange(BuildPrefixed(Encoding.UTF8.GetBytes("v")));
            var buffer = new byte[4 + body.Count];
            WriteU32LE(buffer, 0, 5);
            Buffer.BlockCopy(body.ToArray(), 0, buffer, 4, body.Count);
            var offset = 0;
            Check(ThrowsInvalidData(() => McapBinaryReader.ReadMap(buffer, ref offset)),
                "134-8E: map key/value reads cannot consume bytes outside declared map length");
        }

        private static void NonSeekableRecorderStreamFailsBeforeWriting()
        {
            var stream = new NonSeekableMemoryStream();
            Check(Throws<NotSupportedException>(() => new McapRecorder(stream)),
                "134-8F-1: recorder rejects non-seekable streams before writing header bytes");
            Check(stream.Length == 0,
                "134-8F-2: rejected non-seekable stream remains untouched");
        }

        private static void CompressionRejectsNullCompressedPayloads()
        {
            Check(ThrowsInvalidData(() => McapCompression.Decompress("lz4", null, 0)),
                "134-8G-1: lz4 decompression rejects null compressed data");
            Check(ThrowsInvalidData(() => McapCompression.Decompress("zstd", null, 0)),
                "134-8G-2: zstd decompression rejects null compressed data");
        }

        private static void WriterOptionsNormalizeUpperBoundsAndLz4Policy()
        {
            var oversized = McapWriterOptions.Normalize(new McapWriterOptions { ChunkSizeBytes = int.MaxValue });
            Check(oversized.ChunkSizeBytes == McapWriterOptions.MaxChunkSizeBytes,
                "134-8H-1: writer options clamp oversized chunk size");
            var defaults = McapWriterOptions.Normalize(null);
            Check(defaults.Lz4CompressionLevel == McapWriterOptions.DefaultLz4CompressionLevel,
                "134-8H-2: writer options expose explicit default lz4 compression policy");
        }

        private static void InvalidProtobufSchemaDoesNotAllocateSchemaOrChannel()
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
            Check(summary.Schemas.Count == 0 && summary.Channels.Count == 0,
                "134-8I: invalid protobuf schema content fails before allocating schema/channel ids");
        }

        private static void AttachmentCrcValidityIsReaderOwned()
        {
            var property = typeof(McapAttachment).GetProperty(nameof(McapAttachment.CrcValid));
            Check(property != null && property.SetMethod != null && property.SetMethod.IsAssembly,
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

        private static bool ThrowsInvalidData(Action action)
        {
            try
            {
                action();
                return false;
            }
            catch (InvalidDataException)
            {
                return true;
            }
        }

        private static bool Throws<T>(Action action) where T : Exception
        {
            try
            {
                action();
                return false;
            }
            catch (T)
            {
                return true;
            }
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new Exception("[FAIL] " + label);

            _passed++;
            Console.WriteLine("[PASS] " + label);
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
