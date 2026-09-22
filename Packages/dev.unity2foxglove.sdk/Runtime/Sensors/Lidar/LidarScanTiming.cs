// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Sensors/Lidar

using System;

namespace Unity.FoxgloveSDK.Sensors.Lidar
{
    /// <summary>
    /// Shared scan timing helpers for LiDAR patterns and point-cloud payloads.
    /// </summary>
    public static class LidarScanTiming
    {
        /// <summary>
        /// Convert a normalized offset inside one scan period into seconds.
        /// </summary>
        public static float NormalizedOffsetToSeconds(float normalizedOffset, double scanRateHz)
        {
            if (float.IsNaN(normalizedOffset) || float.IsInfinity(normalizedOffset) || normalizedOffset <= 0f)
                return 0f;
            if (double.IsNaN(scanRateHz) || double.IsInfinity(scanRateHz) || scanRateHz <= 0d)
                return 0f;

            return (float)(Math.Min(normalizedOffset, 1f) / scanRateHz);
        }

        /// <summary>
        /// Returns the point acquisition offset when a scan spans multiple physics ticks.
        /// The elapsed physics time is authoritative; normalized phase only refines the
        /// position inside the current tick and never compresses a budget-stretched scan.
        /// </summary>
        public static float AcquisitionOffsetSeconds(
            double acquisitionPhysSeconds,
            double scanStartPhysSeconds,
            float normalizedOffset,
            float fixedDeltaTimeSeconds)
        {
            var elapsed = acquisitionPhysSeconds - scanStartPhysSeconds;
            if (double.IsNaN(elapsed) || double.IsInfinity(elapsed) || elapsed < 0d)
                elapsed = 0d;
            var tick = float.IsNaN(fixedDeltaTimeSeconds) || float.IsInfinity(fixedDeltaTimeSeconds)
                ? 0f
                : Math.Max(0f, fixedDeltaTimeSeconds);
            var phase = float.IsNaN(normalizedOffset) || float.IsInfinity(normalizedOffset)
                ? 0f
                : Math.Clamp(normalizedOffset, 0f, 1f);
            return (float)elapsed + phase * tick;
        }
    }
}
