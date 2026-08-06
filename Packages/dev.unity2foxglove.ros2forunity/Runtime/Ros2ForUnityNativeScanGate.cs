// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime
// Purpose: Pure double-precision cadence gates shared by native bridges.

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

    internal static class Ros2ForUnityNativePublisherRetryGate
    {
        internal const double CooldownSeconds = 1D;

        internal static bool CanAttempt(double nowSeconds, double nextAttemptAtSeconds)
            => nowSeconds >= nextAttemptAtSeconds;

        internal static void RecordFailure(double nowSeconds, ref double nextAttemptAtSeconds)
            => nextAttemptAtSeconds = nowSeconds + CooldownSeconds;

        internal static void Reset(ref double nextAttemptAtSeconds)
            => nextAttemptAtSeconds = 0D;
    }
}
