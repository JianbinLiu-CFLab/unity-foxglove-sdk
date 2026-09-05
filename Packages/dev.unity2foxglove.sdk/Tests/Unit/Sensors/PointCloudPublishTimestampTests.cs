// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Schemas.PointCloud;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Util;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Sensors
{
    public sealed class PointCloudPublishTimestampTests
    {
        [Fact]
        public void QoSPreparationUsesPublishTimestampForImmediateFrame()
        {
            var frame = new PointCloudFrame { UnixNs = 100UL, FrameId = "submission-A" };
            frame.Points.Add(new PointCloudPoint(1f, 2f, 3f));
            var reducer = new PointCloudQoSReducer();

            var prepared = reducer.PrepareFrameForQoS(
                frame, 200UL, "fallback", 100, 1024,
                PointCloudSamplingMode.UniformStride, 0f, false, out _);

            Assert.Equal(200UL, prepared.UnixNs);
            Assert.Equal("submission-A", prepared.FrameId);
            Assert.Equal(100UL, frame.UnixNs);
        }
    }
}
