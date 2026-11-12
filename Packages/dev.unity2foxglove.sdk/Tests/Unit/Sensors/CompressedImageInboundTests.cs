using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
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
        [InlineData("bgr8; tiff compressed bgr8", "format")]
        [InlineData("bgr8; jpeg", "format")]
        [InlineData("jpeg", "maxBytes")]
        public void MapperRejectsUnsupportedOrOversizedFrames(string format, string expected)
        {
            var message = new CompressedImageInboundMessage(42, "camera", new byte[4], format);
            var maxBytes = expected == "maxBytes" ? 3 : 1024;

            Assert.False(CompressedImageInboundMapper.TryMap(message, maxBytes, out _, out var error));
            Assert.Contains(expected, error, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void MapperRejectsExcessiveFormatMetadata()
        {
            var format = new string('x', CompressedImageInboundMapper.MaxFormatBytes + 1);
            var message = new CompressedImageInboundMessage(42, "camera", new byte[1], format);

            Assert.False(CompressedImageInboundMapper.TryMap(message, 1024, out _, out var error));
            Assert.Contains("metadata", error, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("bgr8; jpeg compressed bgr8", "jpeg")]
        [InlineData("rgb8; png compressed rgb8", "png")]
        public void MapperAcceptsStandardRosCompressedImageFormat(string format, string expected)
        {
            var message = new CompressedImageInboundMessage(42, "camera", new byte[4], format);

            Assert.True(CompressedImageInboundMapper.TryMap(message, 1024, out var frame, out var error));
            Assert.Equal(string.Empty, error);
            Assert.Equal(expected, frame.Format);
        }

        [Fact]
        public void IngressBudgetRejectsFramesAfterFrameOrByteLimit()
        {
            var budget = new CompressedImageInboundBudget(2, 10);
            var now = Stopwatch.GetTimestamp();

            Assert.True(budget.TryAdmitFrame(4, 1, now));
            Assert.True(budget.TryAdmitFrame(4, 1, now));
            Assert.False(budget.TryAdmitFrame(1, 9, now));
            Assert.False(budget.TryAdmitFrame(3, 7, now));
            Assert.True(budget.TryAdmitFrame(4, 1, now + Stopwatch.Frequency));
        }

        [Fact]
        public void IngressBudgetSerializesConcurrentAdmissions()
        {
            var budget = new CompressedImageInboundBudget(1, 1024);
            var now = Stopwatch.GetTimestamp();
            var admitted = 0;

            Parallel.For(0, 32, _ =>
            {
                if (budget.TryAdmitFrame(1, 1, now))
                    Interlocked.Increment(ref admitted);
            });

            Assert.Equal(1, admitted);
        }

        [Fact]
        public void IngressBudgetIgnoresStaleCallbackTimestamps()
        {
            var budget = new CompressedImageInboundBudget(2, 1024);
            var now = Stopwatch.GetTimestamp();

            Assert.True(budget.TryAdmitFrame(1, 1, now));
            Assert.True(budget.TryAdmitFrame(1, 1, now + Stopwatch.Frequency / 2));
            Assert.False(budget.TryAdmitFrame(1, 1, now - Stopwatch.Frequency / 2));
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
