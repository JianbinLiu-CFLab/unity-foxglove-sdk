using System;
using System.Numerics;
using Unity.FoxgloveSDK.Schemas.Imu;
using Xunit;
#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
            var frameIdData = Marshal.AllocHGlobal(1);
            try
            {
                Marshal.WriteIntPtr(nativeMessage, 8, frameIdData);
                Marshal.WriteInt64(nativeMessage, 16, 257);
                Marshal.WriteInt64(nativeMessage, 24, 257);
                Assert.False(Ros2ForUnityImuNativeMessageGuard.TryAdmit(nativeMessage, 256));
                Marshal.WriteInt64(nativeMessage, 16, 256);
                Marshal.WriteInt64(nativeMessage, 24, 256);
                Assert.True(Ros2ForUnityImuNativeMessageGuard.TryAdmit(nativeMessage, 256));
            }
            finally
            {
                Marshal.FreeHGlobal(frameIdData);
                Marshal.FreeHGlobal(nativeMessage);
            }
        }

        [Theory]
        [InlineData(0, true)]
        [InlineData(1, true)]
        [InlineData(256, true)]
        [InlineData(257, false)]
        public void NativeImuFrameIdGuardEnforcesBoundarySizes(long frameIdBytes, bool expected)
        {
            var nativeMessage = Marshal.AllocHGlobal(32);
            var frameIdData = frameIdBytes == 0 ? IntPtr.Zero : Marshal.AllocHGlobal(1);
            try
            {
                Marshal.WriteIntPtr(nativeMessage, 8, frameIdData);
                Marshal.WriteInt64(nativeMessage, 16, frameIdBytes);
                Marshal.WriteInt64(nativeMessage, 24, frameIdBytes);
                Assert.Equal(
                    expected,
                    Ros2ForUnityImuNativeMessageGuard.TryAdmit(nativeMessage, 256));
            }
            finally
            {
                if (frameIdData != IntPtr.Zero)
                    Marshal.FreeHGlobal(frameIdData);
                Marshal.FreeHGlobal(nativeMessage);
            }
        }

        [Fact]
        public void NativeImuFrameIdGuardRejectsMalformedNativeHeaderField()
        {
            var nativeMessage = Marshal.AllocHGlobal(32);
            var frameIdData = Marshal.AllocHGlobal(1);
            try
            {
                Marshal.WriteIntPtr(nativeMessage, 8, frameIdData);
                Marshal.WriteInt64(nativeMessage, 16, 257);
                Marshal.WriteInt64(nativeMessage, 24, 256);
                Assert.False(Ros2ForUnityImuNativeMessageGuard.TryAdmit(nativeMessage, 256));
            }
            finally
            {
                Marshal.FreeHGlobal(frameIdData);
                Marshal.FreeHGlobal(nativeMessage);
            }
        }

        [Fact]
        public void RuntimeLossRotatesSlotAndInvalidatesRetiredCallbacks()
        {
            var lifecycle = new ImuInboundLifecycleState();
            var oldSlot = new ImuNativeFrameSlot();
            var generation = lifecycle.BeginGeneration();
            Assert.True(oldSlot.Publish(CreateFrame(1)));

            var currentSlot = oldSlot;
            Ros2ForUnityImuInboundRecovery.ResetForRuntimeLoss(lifecycle, ref currentSlot);

            Assert.False(lifecycle.IsCurrent(generation));
            Assert.False(oldSlot.Publish(CreateFrame(2)));
            Assert.NotSame(oldSlot, currentSlot);
            Assert.True(currentSlot.Publish(CreateFrame(3)));
        }

        [Theory]
        [InlineData(true, 0d, 0d)]
        [InlineData(true, 0.01d, 0.01d)]
        [InlineData(false, -1d, 0d)]
        public void ImuMeasurementAvailabilityFollowsRosCovarianceMarkers(
            bool expected,
            double firstValue,
            double diagonalValue)
        {
            var covariance = new double[9];
            covariance[0] = firstValue;
            covariance[4] = diagonalValue;
            covariance[8] = diagonalValue;
            Assert.Equal(expected, Ros2ForUnityImuInboundSemantics.IsMeasurementAvailable(covariance));
        }

        [Fact]
        public void ImuTimestampRejectsOutOfRangeNanoseconds()
        {
            Assert.False(
                Ros2ForUnityImuInboundSemantics.TryCreateUnixNanoseconds(
                    1,
                    1_000_000_000,
                    out _));
            Assert.True(
                Ros2ForUnityImuInboundSemantics.TryCreateUnixNanoseconds(
                    1,
                    999_999_999,
                    out var unixNs));
            Assert.Equal(1_999_999_999UL, unixNs);
        }

        [Fact]
        public void BridgeRetainsSubscriptionUntilNativeRemovalSucceeds()
        {
            var removals = new Queue<Func<bool>>(new Func<bool>[]
            {
                () => false,
                () => throw new InvalidOperationException("remove-subscription"),
                () => false,
                () => true,
            });
            var (bridge, unity, node, subscription) = CreateBridgeWithNativeHandles();
            node.RemoveSubscriptionResult = _ => removals.Dequeue()();

            InvokeBridge(bridge, "Update");
            AssertCleanupPending(bridge, node, subscription);
            InvokeBridge(bridge, "Update");
            AssertCleanupPending(bridge, node, subscription);

            InvokeBridge(bridge, "EnsureSubscription");
            AssertCleanupPending(bridge, node, subscription);
            Assert.Equal(0, unity.CreateNodeCalls);

            InvokeBridge(bridge, "Update");
            Assert.Null(BridgeField(bridge, "_subscription"));
            Assert.Null(BridgeField(bridge, "_node"));
            Assert.False((bool)BridgeField(bridge, "_cleanupPending"));
            Assert.Empty(removals);
        }

        [Fact]
        public void BridgeRetainsNodeUntilNodeRemovalSucceeds()
        {
            var removals = new Queue<Func<bool>>(new Func<bool>[]
            {
                () => false,
                () => throw new InvalidOperationException("remove-node"),
                () => true,
            });
            var (bridge, unity, node, _) = CreateBridgeWithNativeHandles();
            node.RemoveSubscriptionResult = _ => true;
            unity.TryRemoveNodeResult = _ => removals.Dequeue()();

            InvokeBridge(bridge, "Update");
            Assert.Null(BridgeField(bridge, "_subscription"));
            Assert.Same(node, BridgeField(bridge, "_node"));
            Assert.True((bool)BridgeField(bridge, "_cleanupPending"));
            InvokeBridge(bridge, "Update");
            Assert.Same(node, BridgeField(bridge, "_node"));
            Assert.True((bool)BridgeField(bridge, "_cleanupPending"));

            InvokeBridge(bridge, "EnsureSubscription");
            Assert.Null(BridgeField(bridge, "_node"));
            Assert.False((bool)BridgeField(bridge, "_cleanupPending"));
            Assert.Empty(removals);
        }

        [Fact]
        public void RetiredCallbackCannotPublishIntoSlotRecreatedByReenable()
        {
            var bridge = new Ros2ForUnityImuInboundBridge { gameObject = new UnityEngine.GameObject("imu") };
            InvokeBridge(bridge, "OnEnable");
            var lifecycle = (ImuInboundLifecycleState)BridgeField(bridge, "_lifecycle");
            var retiredGeneration = lifecycle.CurrentGeneration;
            var retiredSlot = (ImuNativeFrameSlot)BridgeField(bridge, "_slot");

            InvokeBridge(bridge, "OnDisable");
            InvokeBridge(bridge, "OnEnable");
            var currentGeneration = lifecycle.CurrentGeneration;
            var currentSlot = (ImuNativeFrameSlot)BridgeField(bridge, "_slot");
            Assert.NotSame(retiredSlot, currentSlot);

            InvokeBridge(bridge, "OnImu", null, retiredGeneration, retiredSlot);
            InvokeBridge(bridge, "OnImu", null, retiredGeneration, currentSlot);
            InvokeBridge(bridge, "OnImu", null, currentGeneration, retiredSlot);
            Assert.Equal(0, currentSlot.Stats.Rejected);

            InvokeBridge(bridge, "OnImu", null, currentGeneration, currentSlot);
            Assert.Equal(1, currentSlot.Stats.Rejected);
            Assert.Equal(0, currentSlot.Count);
        }

        [Unity.FoxgloveSDK.UnitTests.Ros2ForUnity.GeneratedImuFact]
        public void OnImuKeepsMeasurementsWhoseCovarianceIsUnknown()
        {
            var frame = DeliverImuThroughBridge(orientationMarker: 0d);

            Assert.True(frame.HasOrientation);
            Assert.True(frame.HasLinearAcceleration);
            Assert.True(frame.HasAngularVelocity);
            Assert.Equal(0.5f, frame.Orientation.X);
            Assert.Equal(0.5f, frame.Orientation.W);
            Assert.Equal(4f, frame.LinearAcceleration.X);
            Assert.Equal(1f, frame.AngularVelocity.X);
        }

        [Unity.FoxgloveSDK.UnitTests.Ros2ForUnity.GeneratedImuFact]
        public void OnImuDropsOnlyTheFieldMarkedWithoutMeasurement()
        {
            var frame = DeliverImuThroughBridge(orientationMarker: -1d);

            Assert.False(frame.HasOrientation);
            Assert.Equal(System.Numerics.Quaternion.Identity, frame.Orientation);
            Assert.True(frame.HasLinearAcceleration);
            Assert.True(frame.HasAngularVelocity);
            Assert.Equal(4f, frame.LinearAcceleration.X);
        }

        private static ImuNativeFrame DeliverImuThroughBridge(double orientationMarker)
        {
            var bridge = new Ros2ForUnityImuInboundBridge { gameObject = new UnityEngine.GameObject("imu") };
            InvokeBridge(bridge, "OnEnable");
            try
            {
                var generation = ((ImuInboundLifecycleState)BridgeField(bridge, "_lifecycle")).CurrentGeneration;
                var slot = (ImuNativeFrameSlot)BridgeField(bridge, "_slot");
                using var message = new Ros2ForUnityGuardedImuMessage
                {
                    Header = new std_msgs.msg.Header
                    {
                        Stamp = new builtin_interfaces.msg.Time { Sec = 42, Nanosec = 7 },
                        Frame_id = "imu_link"
                    },
                    Orientation = new geometry_msgs.msg.Quaternion { X = 0.5d, Y = 0.5d, Z = 0.5d, W = 0.5d },
                    Angular_velocity = new geometry_msgs.msg.Vector3 { X = 1d, Y = 2d, Z = 3d },
                    Linear_acceleration = new geometry_msgs.msg.Vector3 { X = 4d, Y = 5d, Z = 6d }
                };
                message.Orientation_covariance[0] = orientationMarker;

                InvokeBridge(bridge, "OnImu", message, generation, slot);

                Assert.Equal(0, slot.Stats.Rejected);
                Assert.True(slot.TryTake(out var frame));
                return frame;
            }
            finally
            {
                InvokeBridge(bridge, "OnDisable");
            }
        }

        private static (Ros2ForUnityImuInboundBridge Bridge, ROS2.ROS2UnityComponent Unity, ROS2.ROS2Node Node, ROS2.ISubscriptionBase Subscription)
            CreateBridgeWithNativeHandles()
        {
            var bridge = new Ros2ForUnityImuInboundBridge { gameObject = new UnityEngine.GameObject("imu") };
            var unity = new ROS2.ROS2UnityComponent();
            var node = new ROS2.ROS2Node();
            var subscription = new RetainedSubscription();
            SetBridgeField(bridge, "_ros2Unity", unity);
            SetBridgeField(bridge, "_node", node);
            SetBridgeField(bridge, "_subscription", subscription);
            SetBridgeField(bridge, "_cleanupPending", true);
            return (bridge, unity, node, subscription);
        }

        private static void AssertCleanupPending(
            Ros2ForUnityImuInboundBridge bridge,
            ROS2.ROS2Node node,
            ROS2.ISubscriptionBase subscription)
        {
            Assert.Same(subscription, BridgeField(bridge, "_subscription"));
            Assert.Same(node, BridgeField(bridge, "_node"));
            Assert.True((bool)BridgeField(bridge, "_cleanupPending"));
        }

        private static object BridgeField(Ros2ForUnityImuInboundBridge bridge, string name)
            => BridgeMember<FieldInfo>(name).GetValue(bridge);

        private static void SetBridgeField(Ros2ForUnityImuInboundBridge bridge, string name, object value)
            => BridgeMember<FieldInfo>(name).SetValue(bridge, value);

        private static void InvokeBridge(Ros2ForUnityImuInboundBridge bridge, string name, params object[] arguments)
            => BridgeMember<MethodInfo>(name).Invoke(bridge, arguments);

        private static T BridgeMember<T>(string name)
            where T : MemberInfo
        {
            var member = typeof(Ros2ForUnityImuInboundBridge)
                .GetMember(name, BindingFlags.Instance | BindingFlags.NonPublic)
                .OfType<T>()
                .SingleOrDefault();
            Assert.NotNull(member);
            return member;
        }

        private sealed class RetainedSubscription : ROS2.ISubscriptionBase
        {
            public string Topic => "/imu/data";
            public ROS2.rcl_subscription_t Handle => default;
            public object Mutex { get; } = new object();
            public bool IsDisposed => false;
            public void TakeMessage() { }
            public void Dispose() { }
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
