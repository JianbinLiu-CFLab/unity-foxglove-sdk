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

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new Exception("[FAIL] " + label);

            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
