using System;
using Unity.FoxgloveSDK.Schemas.Camera;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.Sensors
{
    public sealed class CompressedImageInboundTests
    {
        [Fact]
        public void MapperCopiesJpegAndNormalizesJpg()
        {
            var source = new byte[] { 1, 2, 3 };
            var message = new CompressedImageInboundMessage(42, "camera", source, "JPG");

            Assert.True(CompressedImageInboundMapper.TryMap(message, 1024, out var frame, out var error));
            Assert.Equal(string.Empty, error);
            Assert.NotSame(source, frame.Data);
            Assert.Equal(source, frame.Data);
            Assert.Equal("jpeg", frame.Format);
        }

        [Theory]
        [InlineData("webp", "format")]
        [InlineData("jpeg", "maxBytes")]
        public void MapperRejectsUnsupportedOrOversizedFrames(string format, string expected)
        {
            var message = new CompressedImageInboundMessage(42, "camera", new byte[4], format);
            var maxBytes = expected == "maxBytes" ? 3 : 1024;

            Assert.False(CompressedImageInboundMapper.TryMap(message, maxBytes, out _, out var error));
            Assert.Contains(expected, error, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void SlotDropsOlderFrameAndReportsDelivery()
        {
            using var slot = new SensorCompressedImageFrameSlot();
            Assert.True(slot.Publish(CreateFrame(1)));
            Assert.True(slot.Publish(CreateFrame(2)));
            Assert.True(slot.TryTake(out var frame));
            Assert.Equal((ulong)2, frame.UnixNs);
            Assert.False(slot.TryTake(out _));
            var stats = slot.Stats;
            Assert.Equal(2, stats.Received);
            Assert.Equal(1, stats.Dropped);
            Assert.Equal(1, stats.Delivered);
        }

        [Fact]
        public void SlotRejectsAfterDispose()
        {
            var slot = new SensorCompressedImageFrameSlot();
            slot.Dispose();
            Assert.False(slot.Publish(CreateFrame(1)));
            Assert.Equal(1, slot.Stats.Rejected);
            Assert.Equal("slot is disposed", slot.Stats.LastError);
        }

        private static SensorCompressedImageFrame CreateFrame(ulong timestamp)
            => new SensorCompressedImageFrame(timestamp, "camera", new byte[] { 1 }, "jpeg");
    }
}
