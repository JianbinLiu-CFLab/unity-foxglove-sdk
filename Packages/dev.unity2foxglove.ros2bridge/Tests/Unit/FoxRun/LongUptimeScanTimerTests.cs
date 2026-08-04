// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2Bridge.Tests/Unit/FoxRun
// Purpose: Locks production discovery scans to double-precision monotonic deadlines.

using System;
using Unity.FoxgloveSDK.UnitTests.Harness;
using Xunit;

namespace Unity2Foxglove.Ros2Bridge.UnitTests
{
    [Trait("Phase", "187")]
    [Trait("Domain", "LongUptime")]
    public sealed class LongUptimeScanTimerTests
    {
        [Theory]
        [InlineData(
            "Packages/dev.unity2foxglove.ros2bridge/Runtime/Schemas/Ros2Msg/Generated/Ros2BridgeTransportProvider.cs",
            "_nextGeneratedSourceScanTime")]
        [InlineData(
            "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityCameraNativeBridge.cs",
            "_nextScanAt")]
        [InlineData(
            "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityImuNativeBridge.cs",
            "_nextScanAt")]
        [InlineData(
            "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityPackedPointCloudBridge.cs",
            "_nextScanAt")]
        [InlineData(
            "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityTransformNativeBridge.cs",
            "_nextScanAt")]
        public void ProductionScannerUsesDoublePrecisionUnscaledDeadline(
            string relativePath,
            string deadlineField)
        {
            var source = TestSources.Text(relativePath);

            Assert.Contains("private double " + deadlineField + ";", source, StringComparison.Ordinal);
            Assert.Contains("Time.unscaledTimeAsDouble", source, StringComparison.Ordinal);
            Assert.Contains("ref " + deadlineField, source, StringComparison.Ordinal);
            Assert.DoesNotContain("Time.unscaledTime <", source, StringComparison.Ordinal);
        }

        [Fact]
        public void GeneratedSourceGateRetainsHalfSecondCadenceAtFloatPrecisionBoundary()
        {
            const double start = 8_388_608D;
            var nextScanAt = 0D;

            Assert.True(Ros2BridgeGeneratedSourceScanGate.TryAdvance(start, ref nextScanAt));
            Assert.Equal(start + 0.5D, nextScanAt);
            Assert.False(Ros2BridgeGeneratedSourceScanGate.TryAdvance(start + 0.25D, ref nextScanAt));
            Assert.True(Ros2BridgeGeneratedSourceScanGate.TryAdvance(start + 0.5D, ref nextScanAt));
            Assert.Equal(start + 1D, nextScanAt);
            Assert.False(Ros2BridgeGeneratedSourceScanGate.TryAdvance(start + 0.75D, ref nextScanAt));
        }

        [Fact]
        public void PackedPointCloudBackpressureCooldownUsesDoublePrecisionUnscaledTime()
        {
            var source = TestSources.Text(
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityPackedPointCloudBridge.cs");

            Assert.Contains("private const double ZenohBackpressureCooldownSeconds", source, StringComparison.Ordinal);
            Assert.Contains("private double _zenohBackpressureSuppressUntil;", source, StringComparison.Ordinal);
            Assert.Contains(
                "Time.unscaledTimeAsDouble < _zenohBackpressureSuppressUntil",
                source,
                StringComparison.Ordinal);
        }
    }
}
