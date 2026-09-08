// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
using System;
using System.IO;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Sensors
{
    public sealed class PointCloudAsyncHandoffTests
    {
        [Fact]
        public void PublicDracoPathClonesFrameBeforeAsyncQueueing()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.cs");
            Assert.Contains("prepared = CloneFrameForAsyncEncoding(prepared, logTimeNs);", source, StringComparison.Ordinal);
            Assert.Contains("clone.Points.AddRange(frame.Points);", source, StringComparison.Ordinal);
            Assert.Contains("private static PointCloudFrame CloneFrameForAsyncEncoding", source, StringComparison.Ordinal);
        }

        private static string Read(string relativePath)
        {
            var isolatedPath = Path.Combine(AppContext.BaseDirectory, "../../../../../../", relativePath);
            var path = File.Exists(isolatedPath)
                ? isolatedPath
                : Path.Combine(Directory.GetCurrentDirectory(), relativePath);
            return File.ReadAllText(path);
        }
    }
}
