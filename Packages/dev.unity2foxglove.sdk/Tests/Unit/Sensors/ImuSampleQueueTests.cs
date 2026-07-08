// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Bounded IMU sample queue behavior checks.

using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Sensors.Imu;
using UnityEngine;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Sensors
{
    [Trait("Phase", "140I1")]
    [Trait("Domain", "Sensors")]
    public sealed class ImuSampleQueueTests
    {
        [Fact]
        public void EnqueueDropsOldestSampleWhenQueueIsFull()
        {
            var queue = new ImuSampleQueue();
            queue.Resize(2, 2);

            queue.Enqueue(Sample(1));
            queue.Enqueue(Sample(2));
            queue.Enqueue(Sample(3));

            Assert.Equal(2, queue.Count);
            Assert.Equal(1, queue.DroppedCount);
            Assert.True(queue.TryDequeue(out var first));
            Assert.True(queue.TryDequeue(out var second));
            Assert.Equal(2UL, first.TimestampNs);
            Assert.Equal(3UL, second.TimestampNs);
            Assert.Equal(0, queue.Count);
        }

        [Fact]
        public void ResizeResetsDroppedCountForNewSessionCapacity()
        {
            var queue = new ImuSampleQueue();
            queue.Resize(2, 2);
            queue.Enqueue(Sample(1));
            queue.Enqueue(Sample(2));
            queue.Enqueue(Sample(3));

            queue.Resize(3, 2);

            Assert.Equal(0, queue.DroppedCount);
        }

        [Fact]
        public void SampleTimestampNsRoundsForNonDecimalRates()
        {
            var expected = (ulong)System.Math.Round(1_000_000_000d / 333d, System.MidpointRounding.AwayFromZero);

            Assert.Equal(expected, ImuSubStep.SampleTimestampNs(0UL, 1, 333));
        }

        [Fact]
        public void ResizePreservesOldestSamplesUpToNewCapacity()
        {
            var queue = new ImuSampleQueue();
            queue.Resize(4, 2);
            queue.Enqueue(Sample(10));
            queue.Enqueue(Sample(20));
            queue.Enqueue(Sample(30));

            queue.Resize(2, 2);

            Assert.Equal(2, queue.Count);
            Assert.True(queue.TryDequeue(out var first));
            Assert.True(queue.TryDequeue(out var second));
            Assert.Equal(10UL, first.TimestampNs);
            Assert.Equal(20UL, second.TimestampNs);
        }

        [Fact]
        public void ResizeUsesProvidedMinimumCapacityWhenRequestedCapacityIsInvalid()
        {
            var queue = new ImuSampleQueue();

            queue.Resize(0, 2);
            queue.Enqueue(Sample(1));
            queue.Enqueue(Sample(2));
            queue.Enqueue(Sample(3));

            Assert.Equal(2, queue.Count);
            Assert.True(queue.TryDequeue(out var first));
            Assert.True(queue.TryDequeue(out var second));
            Assert.Equal(2UL, first.TimestampNs);
            Assert.Equal(3UL, second.TimestampNs);
        }

        [Fact]
        public void TryDequeueOnEmptyQueueReturnsFalse()
        {
            var queue = new ImuSampleQueue();

            var dequeued = queue.TryDequeue(out var sample);

            Assert.False(dequeued);
            Assert.Equal(0UL, sample.TimestampNs);
            Assert.Equal(0, queue.Count);
            Assert.Equal(0, queue.DroppedCount);
        }

        [Fact]
        public void ResizeToSameCapacityDoesNotChangeQueueState()
        {
            var queue = new ImuSampleQueue();
            queue.Resize(2, 2);
            queue.Enqueue(Sample(10));
            queue.Enqueue(Sample(20));
            queue.Enqueue(Sample(30));

            queue.Resize(2, 2);

            Assert.Equal(2, queue.Count);
            Assert.Equal(1, queue.DroppedCount);
            Assert.True(queue.TryDequeue(out var first));
            Assert.True(queue.TryDequeue(out var second));
            Assert.Equal(20UL, first.TimestampNs);
            Assert.Equal(30UL, second.TimestampNs);
        }

        private static ImuSample Sample(ulong timestampNs)
            => new ImuSample(
                timestampNs,
                new Vector3 { x = 1, y = 2, z = 3 },
                new Vector3 { x = 4, y = 5, z = 6 },
                new Quaternion { x = 0, y = 0, z = 0, w = 1 });
    }
}
