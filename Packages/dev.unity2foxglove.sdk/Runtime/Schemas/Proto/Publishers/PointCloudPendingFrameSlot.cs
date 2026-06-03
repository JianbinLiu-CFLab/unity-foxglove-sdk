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
            lock (_gate)
            {
                var droppedPendingFrame = _frame != null && frame != null;
                _frame = frame;

                warning = null;
                if (droppedPendingFrame && logDrops && !_warnedReplacementDrop)
                {
                    warning = ReplacementWarning;
                    _warnedReplacementDrop = true;
                }

                return droppedPendingFrame;
            }
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
