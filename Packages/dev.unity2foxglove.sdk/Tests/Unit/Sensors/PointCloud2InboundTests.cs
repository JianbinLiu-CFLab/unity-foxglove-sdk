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
