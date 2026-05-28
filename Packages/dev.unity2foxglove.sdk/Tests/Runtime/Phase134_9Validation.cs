// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 134-9 regression coverage for MCAP reader/indexing edge cases.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.FoxgloveSDK.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase134_9Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 134-9: MCAP Readers And Indexing ===");
            _passed = 0;

            SummaryOffsetOutsideSummarySectionThrows();
            SummaryRecordCrossingFooterThrows();
            WrongSummaryOpcodeIsSkippedWithoutCursorDrift();
            StreamingDataEndCrcMismatchThrowsWhenValidationEnabled();
            StreamingDataEndCrcMismatchCanBeIgnoredWhenValidationDisabled();
            TruncatedMessageContentThrowsInvalidData();
            ChunkIndexVectorLengthMustBeMultipleOfPairSize();
            StatisticsVectorLengthMustBeMultipleOfPairSize();
            ReadChunkRecordsValidatesIndexedChunkLength();
            SummarylessChunkCrcCanBeDisabled();
            IndexedReaderThrowsAfterDispose();
            FileOrderMaxMessagesKeepsFirstMatches();
            StreamingFileOrderMaxMessagesKeepsFirstMatches();
            MalformedMetadataMapLengthThrowsInvalidData();
            SecondHeaderInDataSectionThrows();

            Console.WriteLine($"Phase 134-9: {_passed} checks passed.");
        }

        private static void SummaryOffsetOutsideSummarySectionThrows()
        {
            using var stream = CreateMcapWithSummaryOffsetBeforeSummaryStart();
            Check(ThrowsInvalidData(() => new McapReader(stream).ReadSummary()),
                "134-9A-1: summary_offset_start before summary section is rejected");
        }

        private static void SummaryRecordCrossingFooterThrows()
        {
            using var stream = CreateSummaryRecordCrossingFooterMcap();
            Check(ThrowsInvalidData(() => new McapReader(stream).ReadSummary()),
                "134-9A-2: summary record crossing footer is rejected");
        }

        private static void WrongSummaryOpcodeIsSkippedWithoutCursorDrift()
        {
            using var stream = CreateSummaryWithWrongOpcodeMcap();
            var summary = new McapReader(stream).ReadSummary();
            Check(summary.Schemas.Count == 1,
                "134-9A-3: summary scan recovers schema after non-summary opcode");
            Check(summary.Channels.Count == 1 && summary.Channels[0].Topic == "/phase134_9",
                "134-9A-4: summary scan recovers channel after non-summary opcode");
        }

        private static void StreamingDataEndCrcMismatchThrowsWhenValidationEnabled()
        {
            using var stream = CreateStreamingCrcMismatchMcap();
            Check(ThrowsInvalidData(() =>
            {
                using var reader = new McapStreamingReader(stream, leaveOpen: true);
                reader.Read(new McapReadOptions { ValidateCrcs = true });
            }), "134-9B-1: streaming DataEnd CRC mismatch is rejected when validation is enabled");
        }

        private static void StreamingDataEndCrcMismatchCanBeIgnoredWhenValidationDisabled()
        {
            using var stream = CreateStreamingCrcMismatchMcap();
            using var reader = new McapStreamingReader(stream, leaveOpen: true);
            var result = reader.Read(new McapReadOptions { ValidateCrcs = false });
            Check(result.Summary.Schemas.Count == 1,
                "134-9B-2: streaming DataEnd CRC mismatch can be ignored for compatibility");
        }

        private static void TruncatedMessageContentThrowsInvalidData()
        {
            Check(ThrowsInvalidData(() => McapRecordDecoder.DecodeMessage(new byte[10], 0, 10)),
                "134-9C-1: truncated message fixed header throws InvalidDataException");
        }

        private static void ChunkIndexVectorLengthMustBeMultipleOfPairSize()
        {
            var content = new MemoryStream();
            WriteU64LE(content, 1);
            WriteU64LE(content, 2);
            WriteU64LE(content, 3);
            WriteU64LE(content, 4);
            WriteU32LE(content, 11);
            Check(ThrowsInvalidData(() => McapRecordDecoder.DecodeChunkIndex(content.ToArray())),
                "134-9C-2: malformed chunk index vector length is rejected");
        }

        private static void StatisticsVectorLengthMustBeMultipleOfPairSize()
        {
            var content = new MemoryStream();
            WriteU64LE(content, 1);
            WriteU16LE(content, 1);
            WriteU32LE(content, 1);
            WriteU32LE(content, 0);
            WriteU32LE(content, 0);
            WriteU32LE(content, 0);
            WriteU64LE(content, 1);
            WriteU64LE(content, 2);
            WriteU32LE(content, 11);
            Check(ThrowsInvalidData(() => McapRecordDecoder.DecodeStatistics(content.ToArray())),
                "134-9C-3: malformed statistics channel-count vector length is rejected");
        }

        private static void ReadChunkRecordsValidatesIndexedChunkLength()
        {
            using var stream = CreateChunkMcap(out var chunkStart, out var chunkLength);
            Check(ThrowsInvalidData(() =>
            {
                new McapReader(stream).ReadChunkRecords(chunkStart, chunkLength + 1, out _);
            }), "134-9D-1: chunk record length must match the indexed chunk length");
        }

        private static void SummarylessChunkCrcCanBeDisabled()
        {
            using (var rejecting = CreateSummarylessBadChunkCrcMcap())
            {
                Check(ThrowsInvalidData(() => new McapReader(rejecting).ReadSummary()),
                    "134-9D-2: summaryless chunk CRC mismatch is rejected by default");
            }

            using (var permissive = CreateSummarylessBadChunkCrcMcap())
            {
                var summary = new McapReader(permissive).ReadSummary(validateCrcs: false);
                Check(summary.Statistics != null && summary.Statistics.ChunkCount == 1,
                    "134-9D-3: summaryless chunk CRC validation can be disabled");
            }
        }

        private static void IndexedReaderThrowsAfterDispose()
        {
            using var stream = CreateSimpleMessageMcap(3);
            var reader = new McapIndexedReader(stream, leaveOpen: true, McapSequentialReadLimits.UnlimitedForTests);
            reader.Dispose();
            Check(ThrowsObjectDisposed(() => { var _ = reader.Summary; }),
                "134-9E-1: Summary rejects access after indexed reader disposal");
            Check(ThrowsObjectDisposed(() => reader.ReadMessages()),
                "134-9E-2: ReadMessages rejects access after indexed reader disposal");
        }

        private static void FileOrderMaxMessagesKeepsFirstMatches()
        {
            using var stream = CreateSimpleMessageMcap(5);
            using var indexed = new McapIndexedReader(stream, leaveOpen: true, McapSequentialReadLimits.UnlimitedForTests);
            var messages = indexed.ReadMessages(new McapReadOptions
            {
                Order = McapReadOrder.FileOrder,
                MaxMessages = 2
            });
            Check(messages.Count == 2 && messages[0].Sequence == 1 && messages[1].Sequence == 2,
                "134-9F-1: indexed FileOrder + MaxMessages keeps the first file-order messages");
        }

        private static void StreamingFileOrderMaxMessagesKeepsFirstMatches()
        {
            using var stream = CreateSimpleMessageMcap(5);
            using var streaming = new McapStreamingReader(stream, leaveOpen: true, McapSequentialReadLimits.UnlimitedForTests);
            var result = streaming.Read(new McapReadOptions
            {
                Order = McapReadOrder.FileOrder,
                MaxMessages = 2
            });
            Check(result.Messages.Count == 2 && result.Messages[0].Sequence == 1 && result.Messages[1].Sequence == 2,
                "134-9F-2: streaming FileOrder + MaxMessages keeps the first file-order messages");
        }

        private static void MalformedMetadataMapLengthThrowsInvalidData()
        {
            var content = new MemoryStream();
            WriteString(content, "phase134_9");
            WriteU32LE(content, 0x80000000);
            Check(ThrowsInvalidData(() => McapRecordDecoder.DecodeMetadata(content.ToArray())),
                "134-9G-1: oversized metadata map length is rejected");
        }

        private static void SecondHeaderInDataSectionThrows()
        {
            using var stream = CreateSecondHeaderMcap();
            Check(ThrowsInvalidData(() => new McapReader(stream).ReadSummary()),
                "134-9G-2: second Header in data section is rejected");
        }

        private static MemoryStream CreateMcapWithSummaryOffsetBeforeSummaryStart()
        {
            var stream = new MemoryStream();
            using (var writer = new McapWriter(stream, leaveOpen: true))
            {
                writer.WriteMagic();
                writer.WriteHeader("", "phase134-9-summary-offset");
                writer.WriteDataEnd();
                var summaryStart = (ulong)writer.Position;
                writer.WriteSchema(1, "phase134_9.Schema", "jsonschema", Encoding.UTF8.GetBytes("{}"));
                writer.WriteFooter(summaryStart, summaryStart - 1, 0);
                writer.WriteMagic();
            }

            stream.Position = 0;
            return stream;
        }

        private static MemoryStream CreateSummaryRecordCrossingFooterMcap()
        {
            var stream = new MemoryStream();
            using (var writer = new McapWriter(stream, leaveOpen: true))
            {
                writer.WriteMagic();
                writer.WriteHeader("", "phase134-9-summary-crossing");
                writer.WriteDataEnd();
                var summaryStart = (ulong)writer.Position;
                writer.WriteBytes(new[] { McapWriter.OpcodeSchema });
                WriteU64LE(stream, 32);
                writer.WriteBytes(new byte[] { 1, 2, 3, 4 });
                writer.WriteFooter(summaryStart, 0, 0);
                writer.WriteMagic();
            }

            stream.Position = 0;
            return stream;
        }

        private static MemoryStream CreateSummaryWithWrongOpcodeMcap()
        {
            var stream = new MemoryStream();
            using (var writer = new McapWriter(stream, leaveOpen: true))
            {
                writer.WriteMagic();
                writer.WriteHeader("", "phase134-9-summary-wrong-opcode");
                writer.WriteDataEnd();
                var summaryStart = (ulong)writer.Position;
                writer.WriteMessage(1, 1, 10, 10, Encoding.UTF8.GetBytes("{}"));
                writer.WriteSchema(1, "phase134_9.Schema", "jsonschema", Encoding.UTF8.GetBytes("{}"));
                writer.WriteChannel(1, 1, "/phase134_9", "json", new Dictionary<string, string>());
                writer.WriteFooter(summaryStart, 0, 0);
                writer.WriteMagic();
            }

            stream.Position = 0;
            return stream;
        }

        private static MemoryStream CreateStreamingCrcMismatchMcap()
        {
            var stream = new MemoryStream();
            using (var writer = new McapWriter(stream, leaveOpen: true))
            {
                writer.WriteMagic();
                writer.WriteHeader("", "phase134-9-streaming-crc");
                writer.WriteSchema(1, "phase134_9.Schema", "jsonschema", Encoding.UTF8.GetBytes("{}"));
                writer.WriteChannel(1, 1, "/phase134_9", "json", new Dictionary<string, string>());
                writer.WriteMessage(1, 1, 10, 10, Encoding.UTF8.GetBytes("{}"));
                writer.WriteDataEnd(0x12345678);
                writer.WriteFooter(0, 0, 0);
                writer.WriteMagic();
            }

            stream.Position = 0;
            return stream;
        }

        private static MemoryStream CreateSimpleMessageMcap(int messageCount)
        {
            var stream = new MemoryStream();
            using (var writer = new McapWriter(stream, leaveOpen: true))
            {
                writer.WriteMagic();
                writer.WriteHeader("", "phase134-9-file-order");
                writer.WriteSchema(1, "phase134_9.Schema", "jsonschema", Encoding.UTF8.GetBytes("{}"));
                writer.WriteChannel(1, 1, "/phase134_9", "json", new Dictionary<string, string>());
                for (var i = 1; i <= messageCount; i++)
                    writer.WriteMessage(1, (uint)i, (ulong)i, (ulong)i, Encoding.UTF8.GetBytes("{}"));
                writer.WriteDataEnd();
                writer.WriteFooter(0, 0, 0);
                writer.WriteMagic();
            }

            stream.Position = 0;
            return stream;
        }

        private static MemoryStream CreateChunkMcap(out ulong chunkStart, out ulong chunkLength)
        {
            var stream = new MemoryStream();
            using (var writer = new McapWriter(stream, leaveOpen: true))
            {
                writer.WriteMagic();
                writer.WriteHeader("", "phase134-9-chunk-length");
                chunkStart = (ulong)writer.Position;
                writer.WriteChunk(1, 1, 0, 0, "", 0, Array.Empty<byte>());
                chunkLength = (ulong)writer.Position - chunkStart;
                writer.WriteDataEnd();
                writer.WriteFooter(0, 0, 0);
                writer.WriteMagic();
            }

            stream.Position = 0;
            return stream;
        }

        private static MemoryStream CreateSummarylessBadChunkCrcMcap()
        {
            var stream = new MemoryStream();
            var records = CreateMessageRecord(1);
            using (var writer = new McapWriter(stream, leaveOpen: true))
            {
                writer.WriteMagic();
                writer.WriteHeader("", "phase134-9-bad-chunk-crc");
                writer.WriteChunk(1, 1, (ulong)records.Length, 0x12345678, "", (ulong)records.Length, records);
                writer.WriteDataEnd();
                writer.WriteFooter(0, 0, 0);
                writer.WriteMagic();
            }

            stream.Position = 0;
            return stream;
        }

        private static MemoryStream CreateSecondHeaderMcap()
        {
            var stream = new MemoryStream();
            using (var writer = new McapWriter(stream, leaveOpen: true))
            {
                writer.WriteMagic();
                writer.WriteHeader("", "phase134-9-first-header");
                writer.WriteHeader("", "phase134-9-second-header");
                writer.WriteDataEnd();
                writer.WriteFooter(0, 0, 0);
                writer.WriteMagic();
            }

            stream.Position = 0;
            return stream;
        }

        private static byte[] CreateMessageRecord(uint sequence)
        {
            var content = new MemoryStream();
            WriteU16LE(content, 1);
            WriteU32LE(content, sequence);
            WriteU64LE(content, sequence);
            WriteU64LE(content, sequence);
            var payload = Encoding.UTF8.GetBytes("{}");
            content.Write(payload, 0, payload.Length);

            var record = new MemoryStream();
            record.WriteByte(McapWriter.OpcodeMessage);
            WriteU64LE(record, (ulong)content.Length);
            var contentBytes = content.ToArray();
            record.Write(contentBytes, 0, contentBytes.Length);
            return record.ToArray();
        }

        private static void WriteString(Stream stream, string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            WriteU32LE(stream, (uint)bytes.Length);
            stream.Write(bytes, 0, bytes.Length);
        }

        private static void WriteU16LE(Stream stream, ushort value)
        {
            stream.WriteByte((byte)value);
            stream.WriteByte((byte)(value >> 8));
        }

        private static void WriteU32LE(Stream stream, uint value)
        {
            stream.WriteByte((byte)value);
            stream.WriteByte((byte)(value >> 8));
            stream.WriteByte((byte)(value >> 16));
            stream.WriteByte((byte)(value >> 24));
        }

        private static void WriteU64LE(Stream stream, ulong value)
        {
            stream.WriteByte((byte)value);
            stream.WriteByte((byte)(value >> 8));
            stream.WriteByte((byte)(value >> 16));
            stream.WriteByte((byte)(value >> 24));
            stream.WriteByte((byte)(value >> 32));
            stream.WriteByte((byte)(value >> 40));
            stream.WriteByte((byte)(value >> 48));
            stream.WriteByte((byte)(value >> 56));
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

        private static bool ThrowsObjectDisposed(Action action)
        {
            try
            {
                action();
                return false;
            }
            catch (ObjectDisposedException)
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
    }
}
