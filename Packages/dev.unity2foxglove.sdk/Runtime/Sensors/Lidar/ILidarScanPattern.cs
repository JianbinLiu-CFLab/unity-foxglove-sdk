// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Sensors/Lidar

using System.Numerics;

namespace Unity.FoxgloveSDK.Sensors.Lidar
{
    /// <summary>
    /// Vendor-agnostic LiDAR scan pattern. Implementations produce ray directions
    /// and time offsets for a single frame of a LiDAR scan.
    /// </summary>
    /// <summary>
    /// Summary text for this member.
    /// </summary>

/// <summary>Summary text for this member.</summary>
    public interface ILidarScanPattern
    {
        string ProductLine { get; }
        double ScanRateHz { get; }
        double MinRangeMeters { get; }
        int RayCount { get; }

        /// <summary>
        /// Computes the i-th ray of the current frame.
        /// </summary>
        /// <param name="index">Ray index [0 .. RayCount).</param>
        /// <param name="frameIndex">Monotonic frame counter (non-repetitive patterns use as seed; spinning ignores).</param>
        /// <param name="direction">Unit-length direction vector in sensor-local space (x-right, y-up, z-forward).</param>
        /// <param name="timeOffset">Normalized time offset [0..1) within the scan period.</param>
        bool TryGetRay(int index, int frameIndex,
            out Vector3 direction, out float timeOffset);
    }
}

