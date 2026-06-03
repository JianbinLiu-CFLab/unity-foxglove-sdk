// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Sensors/Lidar
// Purpose: Publishes completed VirtualLidar scans via the point cloud publisher.

using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Schemas.PointCloud;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Decouples scan publishing decisions from VirtualLidar's scan-generation loop.
    /// </summary>
    internal sealed class VirtualLidarScanFramePublisher
    {
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
            ref int snapshotCount)
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

            if (!TryPublishNativePointCloud2Scan(pointCloudPublisher, activeScanFrame, ref snapshot, ref snapshotCount)
                && !TryPublishNativeDracoScan(pointCloudPublisher, activeScanFrame, ref snapshot, ref snapshotCount))
            {
                pointCloudPublisher.SetFrame(activeScanFrame);
            }

            return true;
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

        private static bool TryPublishNativePointCloud2Scan(
            FoxglovePointCloudPublisher pointCloudPublisher,
            PointCloudFrame activeScanFrame,
            ref VirtualLidarPointData[] snapshot,
            ref int snapshotCount)
        {
            if (snapshot == null || snapshotCount <= 0 || !pointCloudPublisher.CanQueueVirtualLidarPointCloud2NativeFrame)
                return false;

            if (!pointCloudPublisher.TryQueueVirtualLidarPointCloud2NativeFrame(
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
    }
}
