// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validate Phase 134-35 MCAP test helper hardening.

using System;
using System.IO;
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
