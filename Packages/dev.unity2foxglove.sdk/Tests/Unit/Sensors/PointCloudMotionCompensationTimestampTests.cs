// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
using System;
using System.Numerics;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Schemas.PointCloud;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Sensors
{
    public sealed class PointCloudMotionCompensationTimestampTests
    {
        [Fact]
        public void ScanStartUsesScanStartWhenLeadingSlotsAreInvalid()
        {
            const ulong scanStart = 1_000_000_000UL;
            var source = new[]
            {
                new VirtualLidarPointData { IsValid = 0 },
                new VirtualLidarPointData { IsValid = 1, TimeOffsetSeconds = 0.25f }
            };
            var request = new PointCloudMotionCompensationRequest(
                "deskewed",
                PointCloudMotionCompensationReferenceTime.ScanStart,
                PointCloudMotionCompensationInputConvention.ScanReferenceSensorFrame,
                Array.Empty<SensorMotionPoseSample>());

            var succeeded = PointCloudMotionCompensator.TryCompensateVirtualLidar(
                source, source.Length, scanStart, request, out var result, out var error);

            Assert.True(succeeded, error);
            Assert.Equal(scanStart, result.ReferenceUnixNs);
        }

        [Fact]
        public void DistinctPoseHistoryTransformsAcquisitionPointAndClearsTimingMetadata()
        {
            const ulong scanStart = 1_000_000_000UL;
            var source = new[]
            {
                new VirtualLidarPointData
                {
                    X = 1f,
                    Y = 2f,
                    Z = 3f,
                    AcquisitionX = 1f,
                    AcquisitionY = 2f,
                    AcquisitionZ = 3f,
                    HasAcquisitionFrame = 1,
                    TimeOffsetSeconds = 0.5f,
                    IsValid = 1
                }
            };
            var request = new PointCloudMotionCompensationRequest(
                "deskewed",
                PointCloudMotionCompensationReferenceTime.ScanStart,
                PointCloudMotionCompensationInputConvention.AcquisitionTimeSensorFrame,
                new[]
                {
                    new SensorMotionPoseSample(scanStart, Vector3.Zero, Quaternion.Identity),
                    new SensorMotionPoseSample(scanStart + 1_000_000_000UL, new Vector3(1f, 0f, 0f), Quaternion.Identity)
                });

            var succeeded = PointCloudMotionCompensator.TryCompensateVirtualLidar(
                source, source.Length, scanStart, request, out var result, out var error);

            Assert.True(succeeded, error);
            Assert.InRange(result.Points[0].X, 1.49f, 1.51f);
            Assert.Equal(0f, result.Points[0].TimeOffsetSeconds);
            Assert.Equal(0, result.Points[0].HasAcquisitionFrame);
        }
    }
}
