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
    }
}
