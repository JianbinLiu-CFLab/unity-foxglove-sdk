// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Sensors/Lidar
// Purpose: Publishes completed VirtualLidar scans via the point cloud publisher.

using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Schemas.PointCloud;
using Unity.Profiling;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Decouples scan publishing decisions from VirtualLidar's scan-generation loop.
    /// </summary>
    internal sealed class VirtualLidarScanFramePublisher
    {
        private static readonly ProfilerMarker PublishMarker = new ProfilerMarker("VirtualLidar.Publish");

        /// <summary>
        /// Emits one completed scan through native publish paths when available, otherwise
        /// falls back to regular PointCloudFrame publishing.
        /// </summary>
        public bool TryPublishActiveScan(
            FoxglovePointCloudPublisher pointCloudPublisher,
            bool publishEmptyFrames,
            PointCloudFrame activeScanFrame,
            int activeScanValidPoints,
            ref VirtualLidarPointData[] snapshot,
            ref int snapshotCount,
            ref LidarScanBoundaryTimings timings)
        {
            using (PublishMarker.Auto())
            {
                if (activeScanFrame == null)
                    return false;

                activeScanFrame.ValidCount = activeScanValidPoints > 0
                    ? activeScanValidPoints
                    : snapshotCount;

                var hasNativeSnapshot = snapshotCount > 0;
                if (!hasNativeSnapshot && activeScanValidPoints <= 0 && !publishEmptyFrames)
                    return true;

                if (pointCloudPublisher == null)
                    return true;

                if (!TryPublishNativePackedPointCloudScan(pointCloudPublisher, activeScanFrame, ref snapshot, ref snapshotCount, ref timings)
                    && !TryPublishNativeDracoScan(pointCloudPublisher, activeScanFrame, ref snapshot, ref snapshotCount))
                {
                    pointCloudPublisher.SetFrame(activeScanFrame);
                }

                return true;
            }
        }

        private static bool TryPublishNativeDracoScan(
            FoxglovePointCloudPublisher pointCloudPublisher,
            PointCloudFrame activeScanFrame,
            ref VirtualLidarPointData[] snapshot,
            ref int snapshotCount)
        {
            if (snapshot == null || snapshotCount <= 0 || !pointCloudPublisher.CanQueueVirtualLidarDracoFrame)
                return false;

            if (!pointCloudPublisher.TryQueueVirtualLidarDracoFrame(
                snapshot,
                snapshotCount,
                activeScanFrame.UnixNs,
                activeScanFrame.FrameId,
                activeScanFrame.EmitAbsoluteTimeNs))
            {
                return false;
            }

            snapshot = null;
            snapshotCount = 0;
            return true;
        }

        private static bool TryPublishNativePackedPointCloudScan(
            FoxglovePointCloudPublisher pointCloudPublisher,
            PointCloudFrame activeScanFrame,
            ref VirtualLidarPointData[] snapshot,
            ref int snapshotCount,
            ref LidarScanBoundaryTimings timings)
        {
            if (snapshot == null || snapshotCount <= 0 || !pointCloudPublisher.CanQueueVirtualLidarPackedPointCloudFrame)
                return false;

            if (!pointCloudPublisher.TryQueueVirtualLidarPackedPointCloudFrame(
                snapshot,
                snapshotCount,
                activeScanFrame.UnixNs,
                activeScanFrame.FrameId,
                activeScanFrame.EmitAbsoluteTimeNs,
                ref timings))
            {
                return false;
            }

            snapshot = null;
            snapshotCount = 0;
            return true;
        }
    }
}
