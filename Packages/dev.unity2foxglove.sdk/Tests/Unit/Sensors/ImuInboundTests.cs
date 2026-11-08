using System;
using System.Numerics;
using Unity.FoxgloveSDK.Schemas.Imu;
using Xunit;
#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System.Runtime.InteropServices;
using Unity2Foxglove.Ros2ForUnity.Native;
#endif

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
        public void MapperRejectsOversizedFrameIds()
        {
            var message = new ImuInboundMessage(
                42,
                new string('x', 1024),
                Vector3.Zero,
                Vector3.Zero,
                Quaternion.Identity,
                false);

            Assert.False(ImuInboundMapper.TryMap(message, out _, out var error));
            Assert.Contains("frame id", error, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void SlotRejectsOversizedFrameIdsBeforeQueueing()
        {
            using var slot = new ImuNativeFrameSlot();
            var frame = new ImuNativeFrame(
                42,
                new string('x', 1024),
                Vector3.Zero,
                Vector3.Zero,
                Quaternion.Identity,
                false);

            Assert.False(slot.Publish(frame));
            Assert.Equal(0, slot.Count);
            Assert.Equal(1, slot.Stats.Rejected);
        }

        [Fact]
        public void SlotBoundsQueuedFrameIdBytes()
        {
            using var slot = new ImuNativeFrameSlot(2, 4);
            Assert.True(slot.Publish(CreateFrame(1, "abcd")));
            Assert.True(slot.Publish(CreateFrame(2, "efgh")));
            Assert.Equal(1, slot.Count);
            Assert.True(slot.TryTake(out var frame));
            Assert.Equal((ulong)2, frame.UnixNs);
            Assert.Equal(1, slot.Stats.Dropped);
        }

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
        [Fact]
        public void NativeImuFrameIdGuardRejectsOversizedNativeHeaderField()
        {
            var nativeMessage = Marshal.AllocHGlobal(32);
            try
            {
                Marshal.WriteInt64(nativeMessage, 16, 257);
                Assert.False(Ros2ForUnityImuNativeMessageGuard.TryAdmit(nativeMessage, 256));
                Marshal.WriteInt64(nativeMessage, 16, 256);
                Assert.True(Ros2ForUnityImuNativeMessageGuard.TryAdmit(nativeMessage, 256));
            }
            finally
            {
                Marshal.FreeHGlobal(nativeMessage);
            }
        }
#endif

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
        public void LegacyNativeFrameConstructorPreservesCovarianceDefaults()
        {
            var frame = new ImuNativeFrame(
                42,
                "imu_link",
                Vector3.Zero,
                Vector3.Zero,
                Quaternion.Identity,
                true);

            Assert.True(frame.HasLinearAcceleration);
            Assert.True(frame.HasAngularVelocity);
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
            => CreateFrame(timestamp, "imu_link");

        private static ImuNativeFrame CreateFrame(ulong timestamp, string frameId)
            => new ImuNativeFrame(
                timestamp,
                frameId,
                Vector3.Zero,
                Vector3.Zero,
                Quaternion.Identity,
                true);
    }
}
