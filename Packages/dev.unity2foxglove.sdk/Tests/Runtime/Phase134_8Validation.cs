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

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new Exception("[FAIL] " + label);

            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
