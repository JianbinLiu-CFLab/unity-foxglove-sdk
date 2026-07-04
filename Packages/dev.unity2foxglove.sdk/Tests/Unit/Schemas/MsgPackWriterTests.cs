// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Foxglove MsgPack writer coverage.

using System;
using Unity.FoxgloveSDK.Schemas.MsgPack;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests
{
    [Trait("Phase", "168")]
    [Trait("Domain", "Schemas")]
    public class MsgPackWriterTests
    {
        [Fact]
        public void WriterEmitsCanonicalMapWithStringsNumbersBooleansAndBinary()
        {
            var writer = new FoxgloveMsgPackWriter();

            writer.WriteMapHeader(4);
            writer.WriteString("name");
            writer.WriteString("unity");
            writer.WriteString("count");
            writer.WriteInt32(42);
            writer.WriteString("ok");
            writer.WriteBool(true);
            writer.WriteString("data");
            writer.WriteBinary(new byte[] { 0x01, 0x02, 0x03 });

            Assert.Equal(
                new byte[]
                {
                    0x84,
                    0xa4, 0x6e, 0x61, 0x6d, 0x65,
                    0xa5, 0x75, 0x6e, 0x69, 0x74, 0x79,
                    0xa5, 0x63, 0x6f, 0x75, 0x6e, 0x74,
                    0x2a,
                    0xa2, 0x6f, 0x6b,
                    0xc3,
                    0xa4, 0x64, 0x61, 0x74, 0x61,
                    0xc4, 0x03, 0x01, 0x02, 0x03
                },
                writer.ToArray());
        }

        [Fact]
        public void WriterEmitsFloat64AsBigEndianPayload()
        {
            var writer = new FoxgloveMsgPackWriter();

            writer.WriteDouble(1.5);

            Assert.Equal(
                new byte[] { 0xcb, 0x3f, 0xf8, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 },
                writer.ToArray());
        }

        [Fact]
        public void WriterEmitsFloatPayloadsWithoutPerValueHeapAllocations()
        {
            var writer = new FoxgloveMsgPackWriter(64);

            var before = GC.GetAllocatedBytesForCurrentThread();
            writer.WriteFloat(1.5f);
            writer.WriteDouble(1.5);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(0, allocated);
            Assert.Equal(
                new byte[]
                {
                    0xca, 0x3f, 0xc0, 0x00, 0x00,
                    0xcb, 0x3f, 0xf8, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
                },
                writer.ToArray());
        }

        [Fact]
        public void WriterUsesThirtyTwoBitHeadersAtLargeStringAndBinaryBoundaries()
        {
            var writer = new FoxgloveMsgPackWriter(65_550);

            writer.WriteString(new string('a', 65_536));
            var stringBytes = writer.ToArray();
            Assert.Equal(new byte[] { 0xdb, 0x00, 0x01, 0x00, 0x00 }, stringBytes[..5]);

            writer.Clear();
            writer.WriteBinary(new byte[65_536]);
            var binaryBytes = writer.ToArray();
            Assert.Equal(new byte[] { 0xc6, 0x00, 0x01, 0x00, 0x00 }, binaryBytes[..5]);
        }

        [Fact]
        public void WriterRejectsNegativeContainerLengths()
        {
            var writer = new FoxgloveMsgPackWriter();

            Assert.Throws<ArgumentOutOfRangeException>(() => writer.WriteArrayHeader(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => writer.WriteMapHeader(-1));
        }
    }
}
