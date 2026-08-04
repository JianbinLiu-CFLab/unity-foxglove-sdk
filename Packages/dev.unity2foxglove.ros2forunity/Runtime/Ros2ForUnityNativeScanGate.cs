// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime
// Purpose: Pure double-precision cadence gate shared by native discovery bridges.

namespace Unity2Foxglove.Ros2ForUnity.Native
{
    internal static class Ros2ForUnityNativeScanGate
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
