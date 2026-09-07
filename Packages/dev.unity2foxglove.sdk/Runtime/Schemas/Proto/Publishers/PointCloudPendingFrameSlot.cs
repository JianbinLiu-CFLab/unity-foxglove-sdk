// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Publishers

using Unity.FoxgloveSDK.Schemas;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Last-value-wins pending frame slot shared between source callbacks and Update.
    /// </summary>
    internal sealed class PointCloudPendingFrameSlot
    {
        private const string ReplacementWarning =
            "[Foxglove] PointCloud pending frame replaced; stale pending frame dropped.";

        private readonly object _gate = new object();
        private PointCloudFrame _frame;
        private bool _warnedReplacementDrop;

        public bool SetFrame(PointCloudFrame frame, bool logDrops, out string warning)
        {
            // SetFrame is the public producer boundary. The next Update may hand the
            // stored frame to a background encoder, so retain an owned snapshot rather
            // than allowing a caller to mutate the worker's input after this method
            // returns. The native VirtualLidar paths already transfer their own pooled
            // snapshots and do not use this managed-frame slot.
            var ownedFrame = Snapshot(frame);
            lock (_gate)
            {
                var droppedPendingFrame = _frame != null && ownedFrame != null;
                _frame = ownedFrame;

                warning = null;
                if (droppedPendingFrame && logDrops && !_warnedReplacementDrop)
                {
                    warning = ReplacementWarning;
                    _warnedReplacementDrop = true;
                }

                return droppedPendingFrame;
            }
        }

        private static PointCloudFrame Snapshot(PointCloudFrame frame)
        {
            if (frame == null)
                return null;

            var snapshot = new PointCloudFrame
            {
                UnixNs = frame.UnixNs,
                FrameId = frame.FrameId,
                ValidCount = frame.ValidCount,
                EmitAbsoluteTimeNs = frame.EmitAbsoluteTimeNs
            };
            snapshot.Points.Capacity = frame.Points.Count;
            snapshot.Points.AddRange(frame.Points);
            return snapshot;
        }

        public PointCloudFrame Take()
        {
            lock (_gate)
            {
                var frame = _frame;
                _frame = null;
                return frame;
            }
        }

        public void ResetReplacementWarning()
        {
            lock (_gate)
            {
                _warnedReplacementDrop = false;
            }
        }
    }
}
