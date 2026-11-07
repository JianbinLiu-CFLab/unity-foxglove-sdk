using System;
using System.Numerics;
using Unity.FoxgloveSDK.Schemas.Imu;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.Sensors
{
    public sealed class ImuInboundTests
    {
        [Fact]
        public void MapperPreservesRosCompatibleValues()
        {
            var message = new ImuInboundMessage(
                42,
                "imu_link",
                new Vector3(1f, 2f, 3f),
                new Vector3(4f, 5f, 6f),
                new Quaternion(0f, 0f, 0f, 1f),
                true);

            Assert.True(ImuInboundMapper.TryMap(message, out var frame, out var error));
            Assert.Equal(string.Empty, error);
            Assert.Equal((ulong)42, frame.UnixNs);
            Assert.Equal("imu_link", frame.FrameId);
            Assert.Equal(new Vector3(1f, 2f, 3f), frame.LinearAcceleration);
            Assert.Equal(new Vector3(4f, 5f, 6f), frame.AngularVelocity);
            Assert.True(frame.HasOrientation);
        }

        [Fact]
        public void MapperRejectsNonFiniteValues()
        {
            var message = new ImuInboundMessage(
                42,
                "imu_link",
                new Vector3(float.NaN, 0f, 0f),
                Vector3.Zero,
                Quaternion.Identity,
                false);

            Assert.False(ImuInboundMapper.TryMap(message, out _, out var error));
            Assert.Contains("finite", error, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void SlotRetainsFifoFramesUntilCapacityAndReportsOverflow()
        {
            using var slot = new ImuNativeFrameSlot(2);
            Assert.True(slot.Publish(CreateFrame(1)));
            Assert.True(slot.Publish(CreateFrame(2)));
            Assert.True(slot.Publish(CreateFrame(3)));
            Assert.True(slot.TryTake(out var frame));
            Assert.Equal((ulong)2, frame.UnixNs);
            Assert.True(slot.TryTake(out frame));
            Assert.Equal((ulong)3, frame.UnixNs);
            Assert.False(slot.TryTake(out _));
            var stats = slot.Stats;
            Assert.Equal(3, stats.Received);
            Assert.Equal(1, stats.Dropped);
            Assert.Equal(2, stats.Delivered);
        }

        [Fact]
        public void MapperPreservesCovarianceAvailabilityFlags()
        {
            var message = new ImuInboundMessage(
                42,
                "imu_link",
                Vector3.Zero,
                Vector3.Zero,
                Quaternion.Identity,
                true,
                false,
                false);

            Assert.True(ImuInboundMapper.TryMap(message, out var frame, out _));
            Assert.False(frame.HasLinearAcceleration);
            Assert.False(frame.HasAngularVelocity);
        }

        [Fact]
        public void RetiredSubscriptionGenerationCannotPublishToCurrentSlot()
        {
            var lifecycle = new ImuInboundLifecycleState();
            var firstGeneration = lifecycle.BeginGeneration();
            var currentGeneration = lifecycle.BeginGeneration();

            Assert.False(lifecycle.IsCurrent(firstGeneration));
            Assert.True(lifecycle.IsCurrent(currentGeneration));
        }

        private static ImuNativeFrame CreateFrame(ulong timestamp)
            => new ImuNativeFrame(
                timestamp,
                "imu_link",
                Vector3.Zero,
                Vector3.Zero,
                Quaternion.Identity,
                true);
    }
}
