// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Bounded IMU sample queue behavior checks.

using System;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Sensors.Imu;
using Unity.FoxgloveSDK.UnitTests.Harness;
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
            Assert.Equal(1, queue.DroppedCount);
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

        [Fact]
        public void ExtremeTargetRateIsNormalizedAndTickWorkIsBounded()
        {
            Assert.Equal(ImuSubStep.MaxSupportedRateHz, ImuSubStep.NormalizeRateHz(int.MaxValue));

            var plan = ImuSubStep.PlanTickSamples(
                tickStartSeconds: 0.0,
                tickEndSeconds: 1.0,
                targetRateHz: int.MaxValue,
                nextSampleIndex: 0,
                maxSamples: 512);

            Assert.Equal(512, plan.SampleCount);
            Assert.Equal(4_489, plan.FirstSampleIndex);
            Assert.Equal(4_489, plan.SkippedSampleCount);
            Assert.Equal(5_001, plan.NextSampleIndex);
            Assert.True(
                ImuSubStep.SampleTimestampNs(0, plan.FirstSampleIndex, int.MaxValue)
                < ImuSubStep.SampleTimestampNs(0, plan.NextSampleIndex - 1, int.MaxValue));
        }

        [Theory]
        [InlineData(double.NaN, 0)]
        [InlineData(double.NegativeInfinity, 0)]
        [InlineData(-1.0, 0)]
        [InlineData(0.0, 0)]
        [InlineData(200.0, 200)]
        [InlineData(double.PositiveInfinity, ImuSubStep.MaxSupportedRateHz)]
        public void RateNormalizationHandlesNonFiniteAndOutOfRangeValues(double requested, int expected)
        {
            Assert.Equal(expected, ImuSubStep.NormalizeRateHz(requested));
        }

        [Fact]
        public void ExplicitlySkippedSamplesUseSaturatingDropAccounting()
        {
            var queue = new ImuSampleQueue();

            queue.RecordDropped(17);
            queue.RecordDropped(long.MaxValue);
            queue.RecordDropped(1);

            Assert.Equal(long.MaxValue, queue.DroppedCount);
        }

        [Fact]
        public void VirtualImuReacquiresItsPhysicsOverrideAndUsesBoundedTickPlanning()
        {
            var source = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Imu/VirtualImu.cs");
            var onEnable = TestSources.Slice(source, "private void OnEnable()", "private void OnDisable()");
            var apply = TestSources.Slice(
                source,
                "private void ApplyGlobalPhysicsRateOverride",
                "private void RestoreFixedDeltaTime()");
            var fixedUpdate = TestSources.Slice(source, "private void FixedUpdate()", "private void Update()");

            Assert.Contains("private bool _initialized", source, StringComparison.Ordinal);
            Assert.Contains("_initialized && _globalPhysicsRateHzOverride > 0", onEnable, StringComparison.Ordinal);
            Assert.Contains("ApplyGlobalPhysicsRateOverride(_globalPhysicsRateHzOverride)", onEnable, StringComparison.Ordinal);
            Assert.Contains("if (_didSetFixedDelta)", apply, StringComparison.Ordinal);
            Assert.Contains("ImuSubStep.PlanTickSamples", fixedUpdate, StringComparison.Ordinal);
            Assert.DoesNotContain("while (ImuSubStep.TryGetSampleTime", fixedUpdate, StringComparison.Ordinal);
        }

        [Fact]
        public void VirtualImuDropLogDeadlineUsesDoublePrecisionClock()
        {
            var source = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Imu/VirtualImu.cs");

            Assert.Contains("private double _nextDroppedSamplesLogTime", source, StringComparison.Ordinal);
            Assert.Contains("Time.unscaledTimeAsDouble", source, StringComparison.Ordinal);
        }

        [Fact]
        public void VirtualImuUsesFailureIsolatedNativeFrameDispatch()
        {
            var source = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Imu/VirtualImu.cs");
            var update = TestSources.Slice(source, "private void Update()", "private void OnValidate()");

            Assert.Contains("_nativeFrameDispatch.Invoke(", update, StringComparison.Ordinal);
            Assert.DoesNotContain("nativeFrameHandler.Invoke(nativeFrame)", update, StringComparison.Ordinal);
        }

        private static ImuSample Sample(ulong timestampNs)
            => new ImuSample(
                timestampNs,
                new Vector3 { x = 1, y = 2, z = 3 },
                new Vector3 { x = 4, y = 5, z = 6 },
                new Quaternion { x = 0, y = 0, z = 0, w = 1 });
    }
}
