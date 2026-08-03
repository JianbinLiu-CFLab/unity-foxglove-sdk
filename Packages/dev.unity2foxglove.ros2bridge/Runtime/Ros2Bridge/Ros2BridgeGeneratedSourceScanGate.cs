// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Ros2Bridge
// Purpose: Pure double-precision cadence gate for generated-source discovery.

namespace Unity2Foxglove.Ros2Bridge
{
    internal static class Ros2BridgeGeneratedSourceScanGate
    {
        internal const double IntervalSeconds = 0.5D;

        internal static bool TryAdvance(double nowSeconds, ref double nextScanAtSeconds)
        {
            if (nowSeconds < nextScanAtSeconds)
                return false;

            nextScanAtSeconds = nowSeconds + IntervalSeconds;
            return true;
        }
    }
}
