// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Ros2ForUnity
// Purpose: Executes the native discovery cadence at long Unity uptime.

using Unity2Foxglove.Ros2ForUnity.Native;
using Xunit;

namespace FoxgloveSdk.UnitTests.Ros2ForUnity
{
    [Trait("Phase", "187")]
    [Trait("Domain", "LongUptime")]
    public sealed class Ros2ForUnityScanIntervalGateTests
    {
        [Fact]
        public void NativeScanGateRetainsHalfSecondCadenceAtFloatPrecisionBoundary()
        {
            const double start = 8_388_608D;
            var nextScanAt = 0D;

            Assert.True(Ros2ForUnityNativeScanGate.TryAdvance(start, ref nextScanAt));
            Assert.Equal(start + 0.5D, nextScanAt);
            Assert.False(Ros2ForUnityNativeScanGate.TryAdvance(start + 0.25D, ref nextScanAt));
            Assert.True(Ros2ForUnityNativeScanGate.TryAdvance(start + 0.5D, ref nextScanAt));
            Assert.Equal(start + 1D, nextScanAt);
            Assert.False(Ros2ForUnityNativeScanGate.TryAdvance(start + 0.75D, ref nextScanAt));
        }
    }
}
