// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using Unity.FoxgloveSDK.Components;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    public sealed class Phase173106ReviewTests
    {
        [Fact]
        public void SharedSensorClockGuardsDoubleToUlongOverflow()
        {
            var source = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveSharedSensorClock.cs");

            Assert.Contains("deltaNs > ulong.MaxValue", source);
            Assert.Contains("return ulong.MaxValue;", source);
            Assert.DoesNotContain("checked(_epochUnixNs + (ulong)System.Math.Round", source);
        }

        [Fact]
        public void ChangeHelperTreatsNegativeEpsilonAsZero()
        {
            Assert.False(FoxRunChangeHelper.FloatChanged(1f, 1f, -0.001f));
            Assert.False(FoxRunChangeHelper.DoubleChanged(1d, 1d, -0.001d));
            Assert.True(FoxRunChangeHelper.FloatChanged(1.001f, 1f, -0.001f));
            Assert.True(FoxRunChangeHelper.DoubleChanged(1.001d, 1d, -0.001d));
        }
    }
}
