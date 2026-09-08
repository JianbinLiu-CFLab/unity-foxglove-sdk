// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
using System;
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
    }
}
