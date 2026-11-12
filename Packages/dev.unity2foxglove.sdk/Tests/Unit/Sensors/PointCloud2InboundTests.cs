using System;
using System.Collections.Generic;
using Unity.FoxgloveSDK.Schemas.PointCloud;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.Sensors
{
    public sealed class PointCloud2InboundTests
    {
        [Fact]
        public void MapperCopiesValidLittleEndianFrame()
        {
            var source = new byte[] { 1, 2, 3, 4 };
            var message = new PointCloud2InboundMessage(
                42,
                "laser",
                1,
                1,
                new[] { new PointCloud2InboundField("x", 0, 7) },
                4,
                4,
                source,
                false,
                true);

            Assert.True(PointCloud2InboundMapper.TryMap(message, 1024, "/points", out var frame, out var error));
            Assert.Equal(string.Empty, error);
            Assert.NotSame(source, frame.Data);
            Assert.Equal(source, frame.Data);
            Assert.Equal("/points", frame.Topic);
            Assert.Equal(PointCloudPackedNumericType.Float32, frame.Fields[0].Type);
        }

        [Theory]
        [InlineData(true, 4, 4, 4, 4, "big-endian")]
        [InlineData(false, 4, 4, 4, 3, "rowStep")]
        [InlineData(false, 1, 4, 4, 3, "maxBytes")]
        public void MapperRejectsInvalidFrames(bool bigEndian, uint width, uint pointStep, uint rowStep, int maxBytes, string expected)
        {
            var message = new PointCloud2InboundMessage(
                42,
                "laser",
                1,
                width,
                Array.Empty<PointCloud2InboundField>(),
                pointStep,
                rowStep,
                new byte[4],
                bigEndian,
                true);

            Assert.False(PointCloud2InboundMapper.TryMap(message, maxBytes, "/points", out _, out var error));
            Assert.Contains(expected, error, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void SlotDropsOlderFrameAndReportsDelivery()
        {
            using var slot = new PackedPointCloudFrameSlot();
            Assert.True(slot.Publish(CreateFrame(1)));
            Assert.True(slot.Publish(CreateFrame(2)));
            Assert.True(slot.TryTake(out var frame));
            Assert.Equal((ulong)2, frame.UnixNs);
            frame.RecycleData();
            Assert.False(slot.TryTake(out _));
            var stats = slot.Stats;
            Assert.Equal(2, stats.Received);
            Assert.Equal(1, stats.Dropped);
            Assert.Equal(1, stats.Delivered);
        }

        [Fact]
        public void SlotRejectsAfterDispose()
        {
            var slot = new PackedPointCloudFrameSlot();
            slot.Dispose();
            Assert.False(slot.Publish(CreateFrame(1)));
            Assert.Equal(1, slot.Stats.Rejected);
            Assert.Equal("slot is disposed", slot.Stats.LastError);
        }

        [Fact]
        public void OwnedMapperTransfersBufferUntilFrameDispose()
        {
            PointCloudPackedByteBufferPool.ClearCachedBuffers();
            var data = PointCloudPackedByteBufferPool.Rent(4);
            var message = new PointCloud2InboundMessage(
                42,
                "laser",
                1,
                1,
                Array.Empty<PointCloud2InboundField>(),
                4,
                4,
                data,
                false,
                true);

            Assert.True(PointCloud2InboundMapper.TryMapOwned(message, 1024, "/points", out var frame, out var error));
            Assert.Equal(string.Empty, error);
            Assert.Same(data, frame.Data);
            frame.Dispose();
            frame.Dispose();
            var returned = PointCloudPackedByteBufferPool.Rent(4, out var reused);
            Assert.True(reused);
            Assert.Same(data, returned);
            PointCloudPackedByteBufferPool.Return(returned);
        }

        [Fact]
        public void MapperCompactsValidRowPadding()
        {
            var source = new byte[]
            {
                1, 2, 3, 4, 99, 99, 99, 99,
                5, 6, 7, 8, 88, 88, 88, 88
            };
            var message = new PointCloud2InboundMessage(
                42,
                "laser",
                2,
                1,
                Array.Empty<PointCloud2InboundField>(),
                4,
                8,
                source,
                false,
                true);

            Assert.True(PointCloud2InboundMapper.TryMap(message, 1024, "/points", out var frame, out var error));
            Assert.Equal(string.Empty, error);
            Assert.Equal(4U, frame.RowStep);
            Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, frame.Data);
        }

        [Theory]
        [InlineData(1, 4, "pointStep")]
        [InlineData(uint.MaxValue, 4, "pointStep")]
        [InlineData(1, 8, "pointStep")]
        public void MapperRejectsFieldsOutsidePointStep(uint offset, uint pointStep, string expected)
        {
            var message = new PointCloud2InboundMessage(
                42,
                "laser",
                1,
                1,
                new[] { new PointCloud2InboundField("x", offset, pointStep == 8 ? (byte)8 : (byte)7) },
                pointStep,
                pointStep,
                new byte[pointStep],
                false,
                true);

            Assert.False(PointCloud2InboundMapper.TryMap(message, 1024, "/points", out _, out var error));
            Assert.Contains(expected, error, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void MapperRejectsExcessiveFieldMetadata()
        {
            var fields = new List<PointCloud2InboundField>();
            for (var index = 0; index < 129; index++)
                fields.Add(new PointCloud2InboundField("field" + index, 0, 2));

            var message = new PointCloud2InboundMessage(
                42,
                "laser",
                1,
                1,
                fields,
                1,
                1,
                new byte[1],
                false,
                true);

            Assert.False(PointCloud2InboundMapper.TryMap(message, 1024, "/points", out _, out var error));
            Assert.Contains("metadata", error, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void MapperRejectsOversizedFieldNameBeforeUtf8Scan()
        {
            var message = new PointCloud2InboundMessage(
                42,
                "laser",
                1,
                1,
                new[] { new PointCloud2InboundField(new string('x', PointCloud2InboundMapper.MaxFieldNameBytes + 1), 0, 2) },
                1,
                1,
                new byte[1],
                false,
                true);

            Assert.False(PointCloud2InboundMapper.TryMap(message, 1024, "/points", out _, out var error));
            Assert.Contains("name", error, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void DimensionGuardRejectsOversizedCompactFrameBeforeRent()
        {
            Assert.False(
                PointCloud2InboundMapper.TryGetCompactByteCount(
                    5_000_000,
                    1,
                    1,
                    1,
                    5_000_000,
                    4 * 1024 * 1024,
                    out _,
                    out _,
                    out var error));
            Assert.Contains("maxBytes", error, StringComparison.OrdinalIgnoreCase);
        }

        private static PackedPointCloudFrame CreateFrame(ulong timestamp)
            => new PackedPointCloudFrame(
                timestamp,
                "laser",
                1,
                1,
                new List<PointCloudPackedField>(),
                1,
                new byte[1],
                true);
    }
}
