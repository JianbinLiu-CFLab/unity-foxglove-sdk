// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Sensors/Lidar
// Purpose: Freezes one VirtualLidar scan's storage and acquisition representation.

using System;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Schemas.PointCloud;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Immutable output representation captured when a scan starts. Publisher mode
    /// changes are applied only after the active scan reaches its boundary.
    /// </summary>
    internal readonly struct VirtualLidarScanRepresentation
    {
        public VirtualLidarScanRepresentation(
            bool useNativeSnapshot,
            bool requiresNativeAcquisitionFrame)
        {
            UseNativeSnapshot = useNativeSnapshot;
            RequiresNativeAcquisitionFrame = requiresNativeAcquisitionFrame;
        }

        /// <summary>Whether this scan accumulates the full native ray snapshot.</summary>
        public bool UseNativeSnapshot { get; }

        /// <summary>Whether this scan computes acquisition-time coordinates.</summary>
        public bool RequiresNativeAcquisitionFrame { get; }

        /// <summary>
        /// Append every valid native snapshot point to a managed frame. This is the
        /// lossless boundary fallback when the publisher can no longer consume the
        /// representation captured at scan start.
        /// </summary>
        public int AppendValidSnapshotPoints(
            PointCloudFrame frame,
            VirtualLidarPointData[] snapshot,
            int snapshotCount)
        {
            if (!UseNativeSnapshot || frame == null || snapshot == null || snapshotCount <= 0)
                return 0;

            var end = Math.Min(snapshotCount, snapshot.Length);
            var appended = 0;
            for (var i = 0; i < end; i++)
            {
                var point = snapshot[i];
                if (point.IsValid == 0)
                    continue;

                frame.Points.Add(new PointCloudPoint(point.X, point.Y, point.Z)
                {
                    Intensity = point.Intensity,
                    Reflectivity = point.Reflectivity,
                    TimeOffsetSeconds = point.TimeOffsetSeconds,
                    Ring = point.Ring
                });
                appended++;
            }

            return appended;
        }
    }
}
