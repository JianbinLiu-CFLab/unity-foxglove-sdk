// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Schemas;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Sensors
{
    public sealed class PointCloudSetFrameOwnershipTests
    {
        [Fact]
        public void SetFrameSnapshotsCallerDataBeforeAsyncOwnership()
        {
            var source = new PointCloudFrame
            {
                UnixNs = 123UL,
                FrameId = "sensor",
                ValidCount = 1,
                EmitAbsoluteTimeNs = true
            };
            source.Points.Add(new PointCloudPoint(1f, 2f, 3f));

            var slot = new PointCloudPendingFrameSlot();
            slot.SetFrame(source, logDrops: false, out _);

            source.Points[0] = new PointCloudPoint(99f, 98f, 97f);
            source.Points.Add(new PointCloudPoint(96f, 95f, 94f));
            source.FrameId = "mutated";
            source.UnixNs = 999UL;

            var queued = slot.Take();
            Assert.NotSame(source, queued);
            Assert.Equal(123UL, queued.UnixNs);
            Assert.Equal("sensor", queued.FrameId);
            Assert.Equal(1, queued.ValidCount);
            Assert.True(queued.EmitAbsoluteTimeNs);
            Assert.Single(queued.Points);
            Assert.Equal(1f, queued.Points[0].X);
            Assert.Equal(2f, queued.Points[0].Y);
            Assert.Equal(3f, queued.Points[0].Z);
        }
    }
}
