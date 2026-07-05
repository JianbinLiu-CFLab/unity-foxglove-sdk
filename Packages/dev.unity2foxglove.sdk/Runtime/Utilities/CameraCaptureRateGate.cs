// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Utilities
// Purpose: Source-side camera capture cadence gate for heavy render/readback work.

using System;

namespace Unity.FoxgloveSDK.Util
{
    /// <summary>
    /// Pure camera capture cadence gate. It runs before camera rendering so skipped
    /// visualization frames do not enqueue GPU readback or encoder work.
    /// </summary>
    public static class CameraCaptureRateGate
    {
        /// <summary>
        /// Resolves a positive rate into a nanosecond interval.
        /// </summary>
        public static ulong ResolveIntervalNs(float rateHz)
        {
            if (rateHz <= 0f || float.IsNaN(rateHz) || float.IsInfinity(rateHz))
                throw new ArgumentOutOfRangeException(nameof(rateHz), "Rate must be positive.");

            return (ulong)Math.Max(1d, Math.Round(1_000_000_000d / rateHz));
        }

        /// <summary>
        /// Returns true when a capture should proceed for the timestamp. Backward
        /// clock jumps reset the baseline and allow one frame through.
        /// </summary>
        public static bool ShouldCapture(ref ulong lastCaptureUnixNs, ulong timestampNs, ulong intervalNs)
        {
            if (intervalNs == 0UL)
                throw new ArgumentOutOfRangeException(nameof(intervalNs));

            if (lastCaptureUnixNs != 0UL
                && timestampNs >= lastCaptureUnixNs
                && timestampNs - lastCaptureUnixNs < intervalNs)
            {
                return false;
            }

            lastCaptureUnixNs = timestampNs;
            return true;
        }
    }
}
