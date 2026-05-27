// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validate Phase 134-35 MCAP test helper hardening.

using System;
using System.IO;
using System.Text;
using Unity.FoxgloveSDK.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase134_35Validation
    {
        private static int _passCount;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 134-35 Tests ---");
            _passCount = 0;

            TestRecordReaderRejectsTruncatedLength();
            TestRecordReaderRejectsOversizedLength();
            TestRecordReaderStillAcceptsZeroLengthRecord();
            TestDecodeChannelConsumesMetadata();
            TestDecodeChunkHonorsDeclaredCompressedSize();
            TestDecodeChunkRejectsTrailingBytes();
            TestDecodeChunkRejectsOversizedCompressedSize();
            TestMcapInspectorUsesBoundedRecordLengths();

            Console.WriteLine($"Phase 134-35: {_passCount} checks passed.");
        }

        private static void TestRecordReaderRejectsTruncatedLength()
        {
            var data = BuildRecordOnly(McapWriter.OpcodeHeader, 10);
            ExpectInvalidData(
                () => McapRecordReader.Parse(data),
                "remaining buffer",
                "134-35A-1: truncated MCAP record length is rejected clearly");
        }

        private static void TestRecordReaderRejectsOversizedLength()
        {
            var data = BuildRecordOnly(McapWriter.OpcodeHeader, (ulong)int.MaxValue + 1UL);
            ExpectInvalidData(
                () => McapRecordReader.Parse(data),
                "test parser limit",
                "134-35A-2: MCAP record length above int.MaxValue is rejected clearly");
        }

        private static void TestRecordReaderStillAcceptsZeroLengthRecord()
        {
            var data = BuildRecordOnly(McapWriter.OpcodeHeader, 0);
            var parsed = McapRecordReader.Parse(data);
            Check(parsed.records.Count == 1, "134-35A-3: zero-length record still parses");
            Check(parsed.records[0].Content.Length == 0, "134-35A-3b: zero-length record content is empty");
        }

        private static void TestDecodeChannelConsumesMetadata()
        {
            var content = BuildChannelContent(("coordinate_mode", "unity"));
            var decoded = McapRecordReader.DecodeChannelWithMetadata(content);
            Check(decoded.id == 7 && decoded.schemaId == 3, "134-35B-1: channel id and schema id decode");
            Check(decoded.topic == "/scene" && decoded.encoding == "json", "134-35B-2: channel topic and encoding decode");
            Check(decoded.metadata.TryGetValue("coordinate_mode", out var mode) && mode == "unity",
                "134-35B-3: channel metadata map is decoded");
        }

        private static void TestDecodeChunkHonorsDeclaredCompressedSize()
        {
            var compressedRecords = new byte[] { 1, 2, 3, 4 };
            var decoded = McapRecordReader.DecodeChunk(BuildChunkContent(compressedRecords));
            Check(decoded.compressedSize == (ulong)compressedRecords.Length,
                "134-35C-1: chunk compressed size is decoded");
            Check(decoded.records.Length == compressedRecords.Length && decoded.records[3] == 4,
                "134-35C-2: chunk records copy exactly declared compressed bytes");
        }

        private static void TestDecodeChunkRejectsTrailingBytes()
        {
            var content = BuildChunkContent(new byte[] { 1, 2 }, trailingBytes: 1);
            ExpectInvalidData(
                () => McapRecordReader.DecodeChunk(content),
                "trailing",
                "134-35C-3: chunk decoder rejects bytes after declared compressed payload");
        }

        private static void TestDecodeChunkRejectsOversizedCompressedSize()
        {
            var content = BuildChunkContent(new byte[] { 1, 2 }, compressedSizeOverride: (ulong)int.MaxValue + 1UL);
            ExpectInvalidData(
                () => McapRecordReader.DecodeChunk(content),
                "test parser limit",
                "134-35C-4: chunk decoder rejects compressed size above int.MaxValue");
        }

        private static void TestMcapInspectorUsesBoundedRecordLengths()
        {
            var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "Scripts", "mcap", "McapInspector.cs"));
            Check(source.Contains("TryGetRecordEnd", StringComparison.Ordinal)
                  && !source.Contains("off + (int)len", StringComparison.Ordinal)
                  && !source.Contains("off += (int)len", StringComparison.Ordinal),
                "134-35D-1: McapInspector avoids unchecked int casts for MCAP record lengths");
            Check(source.Contains("MaxInspectorPayloadBytes", StringComparison.Ordinal)
                  && source.Contains("JsonException", StringComparison.Ordinal)
                  && source.Contains("sceneCount", StringComparison.Ordinal),
                "134-35D-2: McapInspector bounds payload inspection and isolates scene counters");
        }

        private static byte[] BuildRecordOnly(byte opcode, ulong length)
        {
            var data = new byte[9];
            var offset = 0;
            data[offset++] = opcode;
            WriteU64LE(data, ref offset, length);
            return data;
        }

        private static void WriteU64LE(byte[] data, ref int offset, ulong value)
        {
            for (var i = 0; i < 8; i++)
                data[offset++] = (byte)(value >> (8 * i));
        }

        private static byte[] BuildChannelContent((string key, string value) metadata)
        {
            using var stream = new MemoryStream();
            WriteU16LE(stream, 7);
            WriteU16LE(stream, 3);
            WriteString(stream, "/scene");
            WriteString(stream, "json");

            using var map = new MemoryStream();
            WriteString(map, metadata.key);
            WriteString(map, metadata.value);
            var mapBytes = map.ToArray();
            WriteU32LE(stream, (uint)mapBytes.Length);
            stream.Write(mapBytes, 0, mapBytes.Length);
            return stream.ToArray();
        }

        private static byte[] BuildChunkContent(byte[] compressedRecords, ulong? compressedSizeOverride = null, int trailingBytes = 0)
        {
            using var stream = new MemoryStream();
            WriteU64LE(stream, 10);
            WriteU64LE(stream, 20);
            WriteU64LE(stream, 30);
            WriteU32LE(stream, 0);
            WriteString(stream, string.Empty);
            WriteU64LE(stream, compressedSizeOverride ?? (ulong)compressedRecords.Length);
            stream.Write(compressedRecords, 0, compressedRecords.Length);
            for (var i = 0; i < trailingBytes; i++)
                stream.WriteByte(0xff);
            return stream.ToArray();
        }

        private static void WriteU16LE(Stream stream, ushort value)
        {
            stream.WriteByte((byte)value);
            stream.WriteByte((byte)(value >> 8));
        }

        private static void WriteU32LE(Stream stream, uint value)
        {
            for (var i = 0; i < 4; i++)
                stream.WriteByte((byte)(value >> (8 * i)));
        }

        private static void WriteU64LE(Stream stream, ulong value)
        {
            for (var i = 0; i < 8; i++)
                stream.WriteByte((byte)(value >> (8 * i)));
        }

        private static void WriteString(Stream stream, string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            WriteU32LE(stream, (uint)bytes.Length);
            stream.Write(bytes, 0, bytes.Length);
        }

        private static void ExpectInvalidData(Action action, string expectedMessageToken, string label)
        {
            try
            {
                action();
            }
            catch (InvalidDataException ex)
            {
                Check(ex.Message.IndexOf(expectedMessageToken, StringComparison.OrdinalIgnoreCase) >= 0, label);
                return;
            }

            throw new Exception($"[FAIL] {label}");
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "Packages", "dev.unity2foxglove.sdk"))
                    && Directory.Exists(Path.Combine(dir.FullName, "Scripts")))
                    return dir.FullName;
                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Could not find repository root for Phase134-35 validation.");
        }

        private static void Check(bool condition, string label)
        {
            if (condition)
            {
                _passCount++;
                Console.WriteLine($"[PASS] {label}");
                return;
            }

            throw new Exception($"[FAIL] {label}");
        }
    }
}
