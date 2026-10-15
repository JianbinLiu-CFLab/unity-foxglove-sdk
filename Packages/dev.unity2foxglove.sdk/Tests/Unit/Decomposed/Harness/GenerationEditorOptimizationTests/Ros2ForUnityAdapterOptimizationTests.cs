// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Phase 140-68/69/70/71/72/73 generation and editor optimization checks.

using System;
using System.IO;
using System.Text.Json;
using Unity.FoxgloveSDK.Editor;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{

    [Trait("Phase", "140-72")]
    [Trait("Domain", "Harness")]
    public sealed class Ros2ForUnityAdapterOptimizationTests
    {
        [Fact]
        public void NativeBridgesReuseScanCollections()
        {
            foreach (var file in new[]
            {
                "Ros2ForUnityCameraNativeBridge.cs",
                "Ros2ForUnityImuNativeBridge.cs",
                "Ros2ForUnityPackedPointCloudBridge.cs",
                "Ros2ForUnityTransformNativeBridge.cs"
            })
            {
                var source = TestSources.Text("Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/" + file);
                Assert.Contains("readonly HashSet<int>", source, StringComparison.Ordinal);
                Assert.Contains("readonly List<int>", source, StringComparison.Ordinal);
                Assert.Contains(".Clear();", source, StringComparison.Ordinal);
                Assert.DoesNotContain("var seen = new HashSet<int>();", source, StringComparison.Ordinal);
                Assert.DoesNotContain("var stale = new List<int>();", source, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void CameraBridgeDiscoversPublishersOnceAndKeepsOwnershipDeferred()
        {
            var cameraBridge = TestSources.Text("Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityCameraNativeBridge.cs");
            var refreshBindings = TestSources.Slice(cameraBridge, "private void RefreshBindings()", "        private void RefreshRawImageBindings");
            var cameraInfo = TestSources.Text("Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityCameraInfoBinding.cs");
            var pointCloudBridge = TestSources.Text("Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityPackedPointCloudBridge.cs");
            var transformBridge = TestSources.Text("Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityTransformNativeBridge.cs");
            var builder = TestSources.Text("Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityPointCloud2MessageBuilder.cs");

            Assert.Contains("var cameraPublishers = FindObjectsByType<FoxgloveCameraPublisher>", refreshBindings, StringComparison.Ordinal);
            Assert.Contains("RefreshImageBindings(cameraPublishers)", refreshBindings, StringComparison.Ordinal);
            Assert.Contains("RefreshRawImageBindings(cameraPublishers)", refreshBindings, StringComparison.Ordinal);
            Assert.Equal(1, TestSources.Count(cameraBridge, "FindObjectsByType<FoxgloveCameraPublisher>"));
            Assert.Contains("Transforms = new[]", cameraInfo, StringComparison.Ordinal);
            Assert.Contains("Transforms = new[]", pointCloudBridge, StringComparison.Ordinal);
            Assert.Contains("Transforms = new[]", transformBridge, StringComparison.Ordinal);
            Assert.Contains("new sensor_msgs.msg.PointField[packedFields.Count]", builder, StringComparison.Ordinal);
        }

        [Fact]
        public void Phase14072MigratedConsolePhaseIsRemoved()
            => TestSources.AssertConsolePhaseRemoved("Phase140_72Validation.cs", "--phase140-72", "Phase140_72Validation.Validate");
    }
}
