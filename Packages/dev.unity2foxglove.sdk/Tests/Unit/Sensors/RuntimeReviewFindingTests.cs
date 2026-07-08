// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Phase 173-079 runtime review regression checks.

using System;
using System.Numerics;
using Foxglove.Schemas;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Sensors.Lidar;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Sensors
{
    [Trait("Phase", "173-079")]
    [Trait("Domain", "Runtime")]
    public sealed class RuntimeReviewFindingTests
    {
        [Theory]
        [InlineData(0.0)]
        [InlineData(-1.0)]
        [InlineData(-180.0)]
        [InlineData(double.NaN)]
        [InlineData(double.NegativeInfinity)]
        [InlineData(double.PositiveInfinity)]
        public void AutoIntrinsicsRejectsInvalidVerticalFov(double verticalFovDegrees)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                CameraCalibrationMessageBuilder.CreateAutoIntrinsics(1UL, "camera", 1920, 1080, verticalFovDegrees));
        }

        [Fact]
        public void AutoIntrinsicsKeepsPlausibleFocalLengthForValidFov()
        {
            var calibration = CameraCalibrationMessageBuilder.CreateAutoIntrinsics(1UL, "camera", 640, 480, 60.0);

            Assert.InRange(calibration.K[4], 480.0 / 4.0, 480.0 * 4.0);
        }

        [Fact]
        public void PoseHistoryIgnoresOutOfOrderSamplesWithoutDroppingLatestCoverage()
        {
            var history = new SensorMotionPoseHistory(capacity: 4, maxAgeNs: 10_000_000_000UL);
            history.Add(100UL, new Vector3(0f, 0f, 0f), Quaternion.Identity);
            history.Add(200UL, new Vector3(2f, 0f, 0f), Quaternion.Identity);
            history.Add(150UL, new Vector3(9f, 0f, 0f), Quaternion.Identity);

            var snapshot = history.Snapshot();

            Assert.Equal(2, history.Count);
            Assert.Equal(200UL, snapshot[1].UnixNs);
            Assert.True(history.Covers(100UL, 200UL));
        }

        [Fact]
        public void PoseHistoryReplacesEqualTimestampWithoutGrowingBuffer()
        {
            var history = new SensorMotionPoseHistory(capacity: 4, maxAgeNs: 10_000_000_000UL);
            history.Add(100UL, new Vector3(0f, 0f, 0f), Quaternion.Identity);
            history.Add(200UL, new Vector3(2f, 0f, 0f), Quaternion.Identity);
            history.Add(200UL, new Vector3(7f, 0f, 0f), Quaternion.Identity);
            history.Add(300UL, new Vector3(3f, 0f, 0f), Quaternion.Identity);

            var snapshot = history.Snapshot();

            Assert.Equal(3, history.Count);
            Assert.Equal(200UL, snapshot[1].UnixNs);
            Assert.Equal(7f, snapshot[1].Translation.X);
            Assert.True(history.Covers(100UL, 300UL));
        }

        [Fact]
        public void PoseHistoryInterpolatesInteriorTimestampWhenSearchIndexStartsAtLastSample()
        {
            var samples = new[]
            {
                new SensorMotionPoseSample(100UL, new Vector3(0f, 0f, 0f), Quaternion.Identity),
                new SensorMotionPoseSample(200UL, new Vector3(2f, 0f, 0f), Quaternion.Identity),
                new SensorMotionPoseSample(300UL, new Vector3(4f, 0f, 0f), Quaternion.Identity)
            };
            var searchIndex = samples.Length - 1;

            Assert.True(SensorMotionPoseHistoryMath.TryInterpolateMonotonic(samples, 250UL, ref searchIndex, out var pose));
            Assert.InRange(pose.Translation.X, 2.99f, 3.01f);
            Assert.Equal(1, searchIndex);
        }

        [Fact]
        public void SpinningLidarRejectsNonPositiveScanDimensionsAndRate()
        {
            Assert.Throws<ArgumentException>(() => LidarModelSpec.Velodyne("VLP-16", 16, 1800, 0.0, null));
            Assert.Throws<ArgumentException>(() => new LidarModelSpec(
                LidarVendor.Ouster,
                "bad",
                LidarScanKind.Spinning,
                rings: 0,
                columns: 1024,
                rateHz: 10.0,
                fovTopDeg: 10.0,
                fovBottomDeg: -10.0,
                beamAltitudeAnglesDeg: null,
                modes: null,
                fovHDeg: 0.0,
                fovVDeg: 0.0,
                beamsPerFrame: 0,
                minRangeMeters: 0.5,
                maxRangeMeters: 120.0));
        }
    }
}
