// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Utilities
// Purpose: Pools managed VirtualLidar point snapshots handed to background encoders.

using System;
using System.Buffers;
using Unity.FoxgloveSDK.Schemas.PointCloud;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Shared pool for full-revolution LiDAR point snapshots that leave the main
    /// thread and are owned by background point-cloud encode requests.
    /// Returned arrays are not cleared for performance; callers must treat the
    /// separately supplied point count as the only valid readable range.
    /// </summary>
    internal static class VirtualLidarPointSnapshotPool
    {
        public static VirtualLidarPointData[] Rent(int minimumLength)
            => minimumLength <= 0
                ? Array.Empty<VirtualLidarPointData>()
                : ArrayPool<VirtualLidarPointData>.Shared.Rent(minimumLength);

        public static void Return(VirtualLidarPointData[] snapshot)
        {
            if (snapshot == null || snapshot.Length == 0)
                return;

            ArrayPool<VirtualLidarPointData>.Shared.Return(snapshot, clearArray: false);
        }
    }
}
