// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Foxglove MsgPack writer coverage.

using System;
using System.Text;
using Unity.FoxgloveSDK.Schemas.MsgPack;
using Unity.FoxgloveSDK.UnitTests.Harness;
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
            using var writer = new FoxgloveMsgPackWriter();

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
            using var writer = new FoxgloveMsgPackWriter();

            writer.WriteDouble(1.5);

            Assert.Equal(
                new byte[] { 0xcb, 0x3f, 0xf8, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 },
                writer.ToArray());
        }

        [Fact]
        public void WriterEmitsFloatPayloadsWithoutPerValueHeapAllocations()
        {
            using var writer = new FoxgloveMsgPackWriter(64);

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
            using var writer = new FoxgloveMsgPackWriter(65_550);

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
            using var writer = new FoxgloveMsgPackWriter();

            Assert.Throws<ArgumentOutOfRangeException>(() => writer.WriteArrayHeader(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => writer.WriteMapHeader(-1));
        }

        [Fact]
        public void WriterExposesOwnedBufferSegmentWithoutCopy()
        {
            using var writer = new FoxgloveMsgPackWriter();

            writer.WriteString("unity");
            var buffer = writer.GetBuffer(out var length);
            var owned = writer.ToArray();

            Assert.Equal(owned.Length, length);
            Assert.NotSame(owned, buffer);
            Assert.Equal(owned, buffer[..length]);
        }

        [Fact]
        public void WriterUsesPooledUtf8StringEncodingPath()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/MsgPack/FoxgloveMsgPackWriter.cs");
            var writeString = TestSources.Slice(source, "public void WriteString", "public void WriteBinary");

            Assert.Contains("ArrayPool<byte>.Shared.Rent", writeString, StringComparison.Ordinal);
            Assert.Contains("new UTF8Encoding(false, true)", source, StringComparison.Ordinal);
            Assert.Contains("StrictUtf8.GetByteCount(value)", writeString, StringComparison.Ordinal);
            Assert.Contains("StrictUtf8.GetBytes(value", writeString, StringComparison.Ordinal);
            Assert.DoesNotContain("Encoding.UTF8", writeString, StringComparison.Ordinal);
        }

        [Fact]
        public void WriterRejectsLoneHighSurrogateBeforeRetainingHeaderOrPayload()
        {
            AssertInvalidStringDoesNotRetainBytes("\ud800");
        }

        [Fact]
        public void WriterRejectsLoneLowSurrogateBeforeRetainingHeaderOrPayload()
        {
            AssertInvalidStringDoesNotRetainBytes("\udc00");
        }

        private static void AssertInvalidStringDoesNotRetainBytes(string invalid)
        {
            using var writer = new FoxgloveMsgPackWriter();

            Assert.Throws<EncoderFallbackException>(() => writer.WriteString(invalid));

            Assert.Equal(0, writer.Length);
            Assert.Empty(writer.ToArray());
        }
    }
}
