// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using Unity.FoxgloveSDK.Schemas.Camera;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    [Trait("Phase", "173-104")]
    public sealed class Phase173104ReviewTests
    {
        [Fact]
        public void JazzyTimeSourcesAndValidatorPreserveClockMutexAndTimeGuards()
        {
            var timeSource = TestSources.Text("Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64/Runtime/Ros2ForUnity/Scripts/Time/ROS2TimeSource.cs");
            var timeUtils = TestSources.Text("Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64/Runtime/Ros2ForUnity/Scripts/Time/TimeUtils.cs");
            var validator = TestSources.Text("Scripts/ros2forunity/windows/jazzy/validate_r2fu_runtime_package.py");
            var builder = TestSources.Text("Scripts/ros2forunity/windows/jazzy/build_r2fu_runtime_package.py");

            Assert.Contains("private readonly object clockMutex = new object();", timeSource, StringComparison.Ordinal);
            Assert.Contains("lock (clockMutex)", timeSource, StringComparison.Ordinal);
            Assert.Contains("Double.IsNaN(secondsIn)", timeUtils, StringComparison.Ordinal);
            Assert.Contains("Int32.MaxValue", timeUtils, StringComparison.Ordinal);
            Assert.Contains("locks lazy ROS clock access", validator, StringComparison.Ordinal);
            Assert.Contains("hardened seconds validation guards", builder, StringComparison.Ordinal);
        }

        [Fact]
        public void CameraReadbackTimingKeepsSlotIndexBounded()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraReadbackTiming.cs");

            Assert.Contains("_nextSlot = (_nextSlot + 1) % _requestKeys.Length;", source, StringComparison.Ordinal);
            Assert.DoesNotContain("_nextSlot++ % _requestKeys.Length", source, StringComparison.Ordinal);
        }

        [Fact]
        public void PointCloudFallbackWarningFlagUsesInterlocked()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/PointCloudPublishState.cs");

            Assert.Contains("private int _warnedTransformFallbackSuppressed", source, StringComparison.Ordinal);
            Assert.Contains("Interlocked.Exchange(ref _warnedTransformFallbackSuppressed, 0)", source, StringComparison.Ordinal);
            Assert.Contains("Interlocked.Exchange(ref _warnedTransformFallbackSuppressed, 1) == 0", source, StringComparison.Ordinal);
        }

        [Fact]
        public void CameraDtosRejectAmbiguousDimensionsAndExposeBoolEndianness()
        {
            var k = new double[9];
            var r = new double[9];
            var p = new double[12];

            Assert.Throws<ArgumentOutOfRangeException>(
                () => new SensorCameraInfoFrame(0UL, "camera", 0, 1, "plumb_bob", Array.Empty<double>(), k, r, p));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new SensorCameraInfoFrame(0UL, "camera", 1, 0, "plumb_bob", Array.Empty<double>(), k, r, p));

            var frame = new SensorRawImageFrame(0UL, "camera", 1, 1, new byte[3], "rgb8", true);
            Assert.Equal((byte)1, frame.IsBigendian);
        }

        [Fact]
        public void DocumentationAndPythonLoaderAvoidStaleContracts()
        {
            var docs = TestSources.Text("Packages/dev.unity2foxglove.sdk/Documentation~/en/05_Verifying_Basic_Visualization.md");
            var loader = TestSources.Text("Scripts/architecture/test_architecture_tooling.py");

            Assert.Contains("ws://127.0.0.1:<configured port>", docs, StringComparison.Ordinal);
            Assert.Contains("The default port is `8765`.", docs, StringComparison.Ordinal);
            Assert.Contains("sys.modules.pop(spec.name, None)", loader, StringComparison.Ordinal);
        }
    }
}
