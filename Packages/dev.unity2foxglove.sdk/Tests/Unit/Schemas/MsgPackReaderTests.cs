// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Strict bounded MessagePack reader compatibility and abuse coverage.

using System;
using System.Collections.Generic;
using Unity.FoxgloveSDK.Schemas.MsgPack;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests
{
    [Trait("Phase", "185-C")]
    [Trait("Domain", "Schemas")]
    public sealed class MsgPackReaderTests
    {
        [Fact]
        public void ReaderDecodesEverySupportedScalarAndBoundaryMarker()
        {
            AssertRead(new byte[] { 0xc0 }, reader =>
            {
                Assert.True(reader.TryReadNil(out var nil));
                Assert.True(nil);
            });
            AssertRead(new byte[] { 0xc2 }, reader =>
            {
                Assert.True(reader.TryReadBoolean(out var value));
                Assert.False(value);
            });
            AssertRead(new byte[] { 0xc3 }, reader =>
            {
                Assert.True(reader.TryReadBoolean(out var value));
                Assert.True(value);
            });

            foreach (var value in new[]
                     {
                         long.MinValue, int.MinValue, short.MinValue,
                         (long)sbyte.MinValue, -33L, -32L, -1L, 0L,
                         127L, 128L, byte.MaxValue, 256L,
                         ushort.MaxValue, 65_536L, int.MaxValue
                     })
            {
                using var writer = new FoxgloveMsgPackWriter();
                writer.WriteInt64(value);
                AssertRead(writer.ToArray(), reader =>
                {
                    Assert.True(reader.TryReadInt64(out var actual), reader.Error);
                    Assert.Equal(value, actual);
                });
            }

            foreach (var value in new[]
                     {
                         0UL, 127UL, 128UL, byte.MaxValue, 256UL,
                         ushort.MaxValue, 65_536UL, uint.MaxValue,
                         (ulong)long.MaxValue, ulong.MaxValue
                     })
            {
                using var writer = new FoxgloveMsgPackWriter();
                writer.WriteUInt64(value);
                AssertRead(writer.ToArray(), reader =>
                {
                    Assert.True(reader.TryReadUInt64(out var actual), reader.Error);
                    Assert.Equal(value, actual);
                });
            }

            AssertRead(
                new byte[] { 0xca, 0x80, 0x00, 0x00, 0x00 },
                reader =>
                {
                    Assert.True(reader.TryReadSingle(out var value), reader.Error);
                    Assert.Equal(
                        BitConverter.SingleToInt32Bits(-0f),
                        BitConverter.SingleToInt32Bits(value));
                });
            AssertRead(
                new byte[] { 0xca, 0x7f, 0xc0, 0x00, 0x00 },
                reader =>
                {
                    Assert.True(reader.TryReadSingle(out var value), reader.Error);
                    Assert.True(float.IsNaN(value));
                });
            AssertRead(
                new byte[] { 0xcb, 0x7f, 0xf0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 },
                reader =>
                {
                    Assert.True(reader.TryReadDouble(out var value), reader.Error);
                    Assert.Equal(double.PositiveInfinity, value);
                });
            AssertRead(
                new byte[] { 0xcb, 0xff, 0xf0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 },
                reader =>
                {
                    Assert.True(reader.TryReadDouble(out var value), reader.Error);
                    Assert.Equal(double.NegativeInfinity, value);
                });
        }

        [Fact]
        public void ReaderDecodesStrictStringsBinaryArraysAndMaps()
        {
            AssertRead(
                new byte[]
                {
                    0x82,
                    0xa4, 0x6e, 0x61, 0x6d, 0x65,
                    0xa5, 0x75, 0x6e, 0x69, 0x74, 0x79,
                    0xa4, 0x64, 0x61, 0x74, 0x61,
                    0x92, 0xc4, 0x02, 0x01, 0x02, 0xc0
                },
                reader =>
                {
                    Assert.True(reader.TryReadMapHeader(out var fields), reader.Error);
                    Assert.Equal(2, fields);
                    Assert.True(reader.TryReadString(out var first), reader.Error);
                    Assert.Equal("name", first);
                    Assert.True(reader.TryReadString(out var name), reader.Error);
                    Assert.Equal("unity", name);
                    Assert.True(reader.TryReadString(out var second), reader.Error);
                    Assert.Equal("data", second);
                    Assert.True(reader.TryReadArrayHeader(out var count), reader.Error);
                    Assert.Equal(2, count);
                    Assert.True(reader.TryReadBinary(out var data), reader.Error);
                    Assert.Equal(new byte[] { 0x01, 0x02 }, data);
                    Assert.True(reader.TryReadNil(out var nil), reader.Error);
                    Assert.True(nil);
                });
        }

        [Fact]
        public void ReaderChecksEveryNumericNarrowingConversion()
        {
            AssertRejected(
                new byte[] { 0xcc, 0x80 },
                reader => reader.TryReadSByte(out _),
                "range");
            AssertRejected(
                new byte[] { 0xd0, 0xff },
                reader => reader.TryReadByte(out _),
                "range");
            AssertRejected(
                new byte[] { 0xce, 0x80, 0x00, 0x00, 0x00 },
                reader => reader.TryReadInt32(out _),
                "range");
            AssertRejected(
                new byte[] { 0xd3, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff },
                reader => reader.TryReadUInt64(out _),
                "range");
        }

        [Fact]
        public void ReaderRejectsInvalidUtf8ReservedAndExtensionMarkers()
        {
            AssertRejected(
                new byte[] { 0xa1, 0xff },
                reader => reader.TryReadString(out _),
                "UTF-8");
            AssertRejected(
                new byte[] { 0xa1, 0xff },
                reader => reader.TrySkipValue(),
                "UTF-8");

            foreach (var marker in new byte[]
                     {
                         0xc1, 0xd4, 0xd5, 0xd6, 0xd7, 0xd8,
                         0xc7, 0xc8, 0xc9
                     })
            {
                AssertRejected(
                    new[] { marker },
                    reader => reader.TrySkipValue(),
                    "marker");
            }
        }

        [Fact]
        public void ReaderRejectsTruncationAtEveryByteBoundary()
        {
            var valid = new byte[]
            {
                0x82,
                0xa1, 0x61,
                0xda, 0x00, 0x04, 0xf0, 0x9f, 0x9a, 0x80,
                0xa1, 0x62,
                0x92, 0xce, 0x12, 0x34, 0x56, 0x78, 0xc4, 0x02, 0xaa, 0xbb
            };

            for (var length = 0; length < valid.Length; length++)
            {
                var truncated = new byte[length];
                Array.Copy(valid, truncated, length);
                var exception = Record.Exception(() =>
                {
                    var reader = new FoxgloveMsgPackReader(truncated, Limits());
                    Assert.False(reader.TrySkipValue());
                    Assert.NotEmpty(reader.Error);
                    Assert.InRange(reader.Error.Length, 1, 160);
                });
                Assert.Null(exception);
            }

            AssertRead(valid, reader => Assert.True(reader.TrySkipValue(), reader.Error));
        }

        [Fact]
        public void ReaderRejectsLengthAndAggregateBudgetsBeforeAllocation()
        {
            var limits = new FoxgloveMsgPackReadLimits(
                maxDepth: 34,
                maxContainerItems: 4,
                maxStringBytes: 3,
                maxBinaryBytes: 2);

            AssertRejected(
                new byte[] { 0xd9, 0x04 },
                reader => reader.TryReadString(out _),
                "length",
                limits);
            AssertRejected(
                new byte[] { 0xc4, 0x03 },
                reader => reader.TryReadBinary(out _),
                "length",
                limits);
            AssertRejected(
                new byte[] { 0x95 },
                reader => reader.TryReadArrayHeader(out _),
                "budget",
                limits);
            AssertRejected(
                new byte[] { 0x85 },
                reader => reader.TryReadMapHeader(out _),
                "budget",
                limits);
            AssertRejected(
                new byte[] { 0xdb, 0xff, 0xff, 0xff, 0xff },
                reader => reader.TryReadString(out _),
                "length",
                limits);
        }

        [Fact]
        [Trait("Phase", "185-F")]
        public void ReaderRejectsContainerCountsImpossibleForRemainingPayload()
        {
            var arrayReader = new FoxgloveMsgPackReader(
                new byte[] { 0xdc, 0x40, 0x00 },
                Limits());
            Assert.False(arrayReader.TryReadArrayHeader(out var arrayCount));
            Assert.Equal(0, arrayCount);
            Assert.Contains(
                "remaining",
                arrayReader.Error,
                StringComparison.OrdinalIgnoreCase);

            var mapReader = new FoxgloveMsgPackReader(
                new byte[] { 0xde, 0x20, 0x00 },
                Limits());
            Assert.False(mapReader.TryReadMapHeader(out var mapCount));
            Assert.Equal(0, mapCount);
            Assert.Contains(
                "remaining",
                mapReader.Error,
                StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        [Trait("Phase", "185-F")]
        public void PublicReadLimitsCannotExceedAbsoluteDepthOrContainerCaps()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new FoxgloveMsgPackReadLimits(
                    FoxgloveMsgPackReadLimits.DefaultMaxDepth + 1,
                    1,
                    1,
                    1));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new FoxgloveMsgPackReadLimits(
                    FoxgloveMsgPackReadLimits.DefaultMaxDepth,
                    FoxgloveMsgPackReadLimits.AbsoluteMaxContainerItems + 1,
                    1,
                    1));
        }

        [Fact]
        public void ReaderAcceptsWireDepthThirtyThreeAndThirtyFourButRejectsThirtyFive()
        {
            AssertRead(
                NestedArray(33),
                reader => Assert.True(reader.TrySkipValue(), reader.Error));
            AssertRead(
                NestedArray(34),
                reader => Assert.True(reader.TrySkipValue(), reader.Error));
            AssertRejected(
                NestedArray(35),
                reader => reader.TrySkipValue(),
                "depth");
        }

        [Fact]
        public void ReaderRequiresWholePayloadCompletion()
        {
            var reader = new FoxgloveMsgPackReader(
                new byte[] { 0x01, 0x02 },
                Limits());

            Assert.True(reader.TryReadInt32(out var first), reader.Error);
            Assert.Equal(1, first);
            Assert.False(reader.TryComplete());
            Assert.Contains("trailing", reader.Error, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void BoundedDeterministicFuzzNeverThrowsOrReturnsUnboundedDiagnostics()
        {
            var random = new Random(0x185c);
            for (var caseIndex = 0; caseIndex < 1_024; caseIndex++)
            {
                var payload = new byte[random.Next(0, 97)];
                random.NextBytes(payload);
                var exception = Record.Exception(() =>
                {
                    var reader = new FoxgloveMsgPackReader(
                        payload,
                        new FoxgloveMsgPackReadLimits(34, 128, 96, 96));
                    if (reader.TrySkipValue())
                        reader.TryComplete();
                    Assert.InRange(reader.Error.Length, 0, 160);
                });
                Assert.Null(exception);
            }
        }

        private static FoxgloveMsgPackReadLimits Limits()
            => new FoxgloveMsgPackReadLimits(
                maxDepth: 34,
                maxContainerItems: 16_384,
                maxStringBytes: 1_024,
                maxBinaryBytes: 1_024);

        private static byte[] NestedArray(int depth)
        {
            var payload = new byte[depth + 1];
            for (var index = 0; index < depth; index++)
                payload[index] = 0x91;
            payload[depth] = 0xc0;
            return payload;
        }

        private static void AssertRead(
            byte[] payload,
            Action<FoxgloveMsgPackReader> read)
        {
            var reader = new FoxgloveMsgPackReader(payload, Limits());
            read(reader);
            Assert.True(reader.TryComplete(), reader.Error);
        }

        private static void AssertRejected(
            byte[] payload,
            Func<FoxgloveMsgPackReader, bool> read,
            string errorFragment,
            FoxgloveMsgPackReadLimits limits = null)
        {
            var reader = new FoxgloveMsgPackReader(payload, limits ?? Limits());
            var exception = Record.Exception(() => Assert.False(read(reader)));
            Assert.Null(exception);
            Assert.Contains(
                errorFragment,
                reader.Error,
                StringComparison.OrdinalIgnoreCase);
            Assert.InRange(reader.Error.Length, 1, 160);
        }
    }
}
