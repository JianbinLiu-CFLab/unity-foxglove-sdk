// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Ros2ForUnity
// Purpose: Verify callback lifetime isolation and bounded generated copies.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Numerics;
using Unity.FoxgloveSDK.Schemas.Imu;
using Unity2Foxglove.Ros2ForUnity.Native;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Ros2ForUnity
{
    [Trait("Phase", "179-C")]
    [Trait("Domain", "Ros2Ownership")]
    public sealed class FoxRunRos2MessageOwnershipTests
    {
        [Fact]
        public void RawCallbackReferenceExpiresButGeneratedOwnedCopyRemainsReadable()
        {
            var rawSlot = new FoxRunRos2OwnedLatestSlot<FakeMessage>(message => message.Dispose());
            var callback = new FakeCallbackScope(new FakeMessage("callback", new[] { 1, 2, 3 }));
            Assert.True(rawSlot.TryPublish(() => callback.Message));
            callback.ReturnFromCallback();
            Assert.Throws<ObjectDisposedException>(() => rawSlot.TryApplyLatest(
                message => _ = message.Text,
                _ => false));
            rawSlot.Stop(_ => false);

            var ownedSlot = new FoxRunRos2OwnedLatestSlot<FakeMessage>(message => message.Dispose());
            callback = new FakeCallbackScope(new FakeMessage("owned", new[] { 4, 5, 6 }));
            var budget = new FoxRunRos2CopyBudget(64);
            Assert.True(ownedSlot.TryPublish(() => GeneratedCopy(callback.Message, budget)));
            callback.ReturnFromCallback();

            FakeMessage applied = null;
            Assert.True(ownedSlot.TryApplyLatest(message => applied = message, _ => false));
            Assert.Equal("owned", applied.Text);
            Assert.Equal(new[] { 4, 5, 6 }, applied.Values);
            Assert.Equal(22, budget.ConsumedBytes);
            ownedSlot.Stop(_ => false);
        }

        [Fact]
        public void CopyBudgetCountsManagedStringAndSequenceStorageOnly()
        {
            var budget = new FoxRunRos2CopyBudget(24);

            budget.RequireString("four");
            budget.RequireSequenceElements(4, sizeof(int));

            Assert.Equal(24, budget.ConsumedBytes);
            Assert.Equal(0, budget.RemainingBytes);
            Assert.Equal(24, budget.MaximumBytes);
            Assert.Throws<InvalidOperationException>(() => budget.RequireBytes(1));
            Assert.Throws<ArgumentOutOfRangeException>(() => budget.RequireSequenceElements(-1, sizeof(int)));
            Assert.Throws<ArgumentOutOfRangeException>(() => budget.RequireSequenceElements(1, -1));
        }

        [Fact]
        public void CopyFailureOrBudgetOverflowDoesNotPublishAnOwnedValue()
        {
            var slot = new FoxRunRos2OwnedLatestSlot<FakeMessage>(message => message.Dispose());
            Assert.False(slot.TryPublish(
                () => throw new InvalidOperationException("copy failed"),
                out var copyFailure));
            Assert.IsType<InvalidOperationException>(copyFailure);

            var callback = new FakeCallbackScope(new FakeMessage("too large", new[] { 1 }));
            Assert.False(slot.TryPublish(
                () => GeneratedCopy(callback.Message, new FoxRunRos2CopyBudget(1)),
                out var budgetFailure));
            Assert.IsType<InvalidOperationException>(budgetFailure);

            Assert.Equal(2, slot.ReceivedCount);
            Assert.Equal(2, slot.CopyFailedCount);
            Assert.Equal(0, slot.PendingCount);
            Assert.Equal(0, slot.ReplacedCount);
        }

        [Fact]
        public void GeneratedCopyCleansPartiallyConstructedNestedGraphOnFailure()
        {
            NestedGraph partial = null;
            var slot = new FoxRunRos2OwnedLatestSlot<NestedGraph>(graph => graph.Dispose());

            Assert.False(slot.TryPublish(() =>
            {
                partial = new NestedGraph();
                try
                {
                    partial.Children.Add(new NestedChild(1));
                    partial.Children.Add(new NestedChild(2));
                    throw new InvalidOperationException("nested copy failed");
                }
                catch
                {
                    partial.Dispose();
                    throw;
                }
            }, out var failure));

            Assert.IsType<InvalidOperationException>(failure);
            Assert.Equal(1, partial.DisposeCount);
            Assert.All(partial.Children, child => Assert.Equal(1, child.DisposeCount));
            Assert.Equal(1, slot.CopyFailedCount);
            slot.Stop(_ => false);
            Assert.Equal(1, partial.DisposeCount);
        }

        private static FakeMessage GeneratedCopy(FakeMessage source, FoxRunRos2CopyBudget budget)
        {
            var text = source.Text;
            var values = source.Values;
            budget.RequireString(text);
            budget.RequireSequenceElements(values.Length, sizeof(int));
            return new FakeMessage(text, (int[])values.Clone());
        }

        private sealed class FakeCallbackScope
        {
            public FakeCallbackScope(FakeMessage message) => Message = message;

            public FakeMessage Message { get; }

            public void ReturnFromCallback() => Message.InvalidateCallbackOwner();
        }

        private sealed class FakeMessage
        {
            private readonly string _text;
            private readonly int[] _values;
            private int _invalid;
            private int _disposeCount;

            public FakeMessage(string text, int[] values)
            {
                _text = text;
                _values = values;
            }

            public string Text
            {
                get
                {
                    ThrowIfInvalid();
                    return _text;
                }
            }

            public int[] Values
            {
                get
                {
                    ThrowIfInvalid();
                    return _values;
                }
            }

            public void InvalidateCallbackOwner() => Volatile.Write(ref _invalid, 1);

            public void Dispose() => Interlocked.Increment(ref _disposeCount);

            private void ThrowIfInvalid()
            {
                if (Volatile.Read(ref _invalid) != 0)
                    throw new ObjectDisposedException(nameof(FakeMessage), "Callback-owned message expired.");
            }
        }

        private sealed class NestedGraph
        {
            private int _disposeCount;

            public List<NestedChild> Children { get; } = new List<NestedChild>();
            public int DisposeCount => Volatile.Read(ref _disposeCount);

            public void Dispose()
            {
                if (Interlocked.Increment(ref _disposeCount) != 1)
                    throw new InvalidOperationException("Graph disposed more than once.");
                foreach (var child in Children)
                    child.Dispose();
            }
        }

        private sealed class NestedChild
        {
            private int _disposeCount;

            public NestedChild(int value) => Value = value;

            public int Value { get; }
            public int DisposeCount => Volatile.Read(ref _disposeCount);

            public void Dispose()
            {
                if (Interlocked.Increment(ref _disposeCount) != 1)
                    throw new InvalidOperationException("Child disposed more than once.");
            }
        }
    }

    [Trait("Phase", "200-W4")]
    [Trait("Domain", "Ros2ImuMeasurement")]
    public sealed class FoxRunRos2ImuMeasurementTests
    {
        [Fact]
        public void GeneratedImuCopyMeasurementCoversDecisionRates()
        {
            if (!TryCreateGeneratedImu(out var source))
                return;

            try
            {
                foreach (var hertz in new[] { 200, 500, 1000 })
                {
                    var slot = new FoxRunRos2OwnedLatestSlot<sensor_msgs.msg.Imu>(message => message.Dispose());
                    for (var index = 0; index < 32; index++)
                        PublishAndApply(slot, source);

                    var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
                    var beforeTicks = Stopwatch.GetTimestamp();
                    var iterations = hertz / 10;
                    for (var index = 0; index < iterations; index++)
                        using (CopyWithRentedContext(source))
                        {
                        }
                    var elapsedTicks = Stopwatch.GetTimestamp() - beforeTicks;
                    var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
                    var allocatedPerMessage = allocatedBytes / (double)iterations;
                    var materializeMicroseconds = elapsedTicks * 1_000_000d / Stopwatch.Frequency / iterations;

                    var mailboxBefore = Stopwatch.GetTimestamp();
                    for (var index = 0; index < iterations; index++)
                        PublishAndApply(slot, source);
                    var publishApplyMicroseconds = (Stopwatch.GetTimestamp() - mailboxBefore)
                                                   * 1_000_000d
                                                   / Stopwatch.Frequency
                                                   / iterations;

                    Console.WriteLine(
                        $"IMU_MEASUREMENT hz={hertz} materialize_copy_us={materializeMicroseconds:F2} "
                        + $"publish_apply_us={publishApplyMicroseconds:F2} "
                        + $"allocated_bytes_per_message={allocatedPerMessage:F2} "
                        + $"replacements={slot.ReplacedCount}");
                    Assert.InRange(materializeMicroseconds, 0d, 10_000d);
                    Assert.InRange(allocatedPerMessage, 0d, 4096d);
                    slot.Stop(_ => false);
                }
            }
            finally
            {
                source.Dispose();
            }
        }

        [Fact]
        public void GeneratedImuCopyBurstMeasuresDeterministicMailboxReplacement()
        {
            if (!TryCreateGeneratedImu(out var source))
                return;

            try
            {
                var slot = new FoxRunRos2OwnedLatestSlot<sensor_msgs.msg.Imu>(message => message.Dispose());
                for (var index = 0; index < 64; index++)
                    Assert.True(slot.TryPublish(source, CopyWithRentedContext));

                Assert.True(slot.ReplacedCount > 0);
                Assert.True(slot.TryApplyLatest(_ => { }, _ => false));
                slot.Stop(_ => false);
            }
            finally
            {
                source.Dispose();
            }
        }

        [Fact]
        public void DedicatedImuManagedPathMeasurementCoversDecisionRates()
        {
            foreach (var hertz in new[] { 200, 500, 1000 })
            {
                using (var warmupSlot = new ImuNativeFrameSlot(256))
                {
                    for (var tick = 0; tick < 60; tick++)
                        PublishAndDrain(warmupSlot, hertz, tick);
                }

                using var slot = new ImuNativeFrameSlot(256);

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
                for (var tick = 0; tick < 60; tick++)
                    PublishAndDrain(slot, hertz, tick);

                var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
                var messageCount = hertz;
                var allocatedPerMessage = allocatedBytes / (double)messageCount;
                Console.WriteLine(
                    $"IMU_MANAGED_PATH_MEASUREMENT hz={hertz} "
                    + $"allocated_bytes_per_message={allocatedPerMessage:F2} "
                    + $"drops={slot.Stats.Dropped} delivered={slot.Stats.Delivered}");

                Assert.Equal(0, slot.Stats.Dropped);
                Assert.Equal(messageCount, slot.Stats.Delivered);
                Assert.InRange(allocatedPerMessage, 0, 128);
            }
        }

        private static void PublishAndDrain(
            ImuNativeFrameSlot slot,
            int hertz,
            int tick)
        {
            var samplesThisTick = hertz / 60 + (tick < hertz % 60 ? 1 : 0);
            for (var sample = 0; sample < samplesThisTick; sample++)
            {
                var message = new ImuInboundMessage(
                    (ulong)(tick * 1_000_000_000L + sample),
                    "imu_link",
                    Vector3.Zero,
                    Vector3.Zero,
                    Quaternion.Identity,
                    true);
                Assert.True(ImuInboundMapper.TryMap(message, out var frame, out var error), error);
                Assert.True(slot.Publish(frame));
            }

            var budget = slot.Count;
            while (budget-- > 0)
                Assert.True(slot.TryTake(out _));
        }

        private static void PublishAndApply(
            FoxRunRos2OwnedLatestSlot<sensor_msgs.msg.Imu> slot,
            sensor_msgs.msg.Imu source)
        {
            Assert.True(slot.TryPublish(source, CopyWithRentedContext));
            Assert.True(slot.TryApplyLatest(_ => { }, _ => false));
        }

        private static sensor_msgs.msg.Imu CopyWithRentedContext(sensor_msgs.msg.Imu source)
        {
            var context = FoxRunRos2CopyContext.Rent(4096);
            try
            {
                return CopyGeneratedImu(source, context);
            }
            finally
            {
                context.Return();
            }
        }

        private static sensor_msgs.msg.Imu CopyGeneratedImu(
            sensor_msgs.msg.Imu source,
            FoxRunRos2CopyContext context)
        {
            var sourceHeader = source.Header;
            var sourceStamp = sourceHeader?.Stamp;
            var frameId = sourceHeader?.Frame_id ?? string.Empty;
            context.RequireBytes(checked(frameId.Length * sizeof(char)));
            context.RequireBytes(checked(
                (source.Orientation_covariance.Length
                 + source.Angular_velocity_covariance.Length
                 + source.Linear_acceleration_covariance.Length) * sizeof(double)));

            var copy = new sensor_msgs.msg.Imu
            {
                Header = new std_msgs.msg.Header
                {
                    Stamp = new builtin_interfaces.msg.Time
                    {
                        Sec = sourceStamp?.Sec ?? 0,
                        Nanosec = sourceStamp?.Nanosec ?? 0
                    },
                    Frame_id = new string(frameId.ToCharArray())
                },
                Orientation = new geometry_msgs.msg.Quaternion
                {
                    X = source.Orientation.X,
                    Y = source.Orientation.Y,
                    Z = source.Orientation.Z,
                    W = source.Orientation.W
                },
                Angular_velocity = new geometry_msgs.msg.Vector3
                {
                    X = source.Angular_velocity.X,
                    Y = source.Angular_velocity.Y,
                    Z = source.Angular_velocity.Z
                },
                Linear_acceleration = new geometry_msgs.msg.Vector3
                {
                    X = source.Linear_acceleration.X,
                    Y = source.Linear_acceleration.Y,
                    Z = source.Linear_acceleration.Z
                }
            };
            Array.Copy(source.Orientation_covariance, copy.Orientation_covariance, source.Orientation_covariance.Length);
            Array.Copy(source.Angular_velocity_covariance, copy.Angular_velocity_covariance, source.Angular_velocity_covariance.Length);
            Array.Copy(source.Linear_acceleration_covariance, copy.Linear_acceleration_covariance, source.Linear_acceleration_covariance.Length);
            return copy;
        }

        private static sensor_msgs.msg.Imu CreateGeneratedImu()
        {
            var message = new sensor_msgs.msg.Imu
            {
                Header = new std_msgs.msg.Header
                {
                    Stamp = new builtin_interfaces.msg.Time { Sec = 42, Nanosec = 7 },
                    Frame_id = "imu_link"
                },
                Orientation = new geometry_msgs.msg.Quaternion { W = 1d },
                Angular_velocity = new geometry_msgs.msg.Vector3 { X = 1d, Y = 2d, Z = 3d },
                Linear_acceleration = new geometry_msgs.msg.Vector3 { X = 4d, Y = 5d, Z = 6d }
            };
            for (var index = 0; index < 9; index++)
            {
                message.Orientation_covariance[index] = 0.01d + index;
                message.Angular_velocity_covariance[index] = 0.02d + index;
                message.Linear_acceleration_covariance[index] = 0.03d + index;
            }
            return message;
        }

        private static bool TryCreateGeneratedImu(out sensor_msgs.msg.Imu message)
        {
            try
            {
                message = CreateGeneratedImu();
                return true;
            }
            catch (TypeInitializationException) when (!OperatingSystem.IsWindows())
            {
                message = null;
                return false;
            }
        }
    }
}
#endif
