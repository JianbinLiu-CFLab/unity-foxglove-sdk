// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Publishers
// Purpose: Tracks point-cloud source/fallback and prepared-publish demand state.

using System.Threading;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Tracks source-driven point-cloud ownership and prepared publish demand for one publisher.
    /// </summary>
    internal sealed class PointCloudPublishState
    {
        private bool _hasPreparedPublishDemand;
        private bool _preparedPublishWebSocket;
        private bool _preparedPublishBridge;
        private int _hasSourceDrivenFrames;
        private int _warnedTransformFallbackSuppressed;

        public void MarkSourceDriven()
        {
            Interlocked.Exchange(ref _hasSourceDrivenFrames, 1);
        }

        public void ResetSourceDriven()
        {
            Interlocked.Exchange(ref _hasSourceDrivenFrames, 0);
            Interlocked.Exchange(ref _warnedTransformFallbackSuppressed, 0);
        }

        public bool ShouldSuppressTransformFallback(bool suppressAfterSourceFrames)
            => suppressAfterSourceFrames && Volatile.Read(ref _hasSourceDrivenFrames) != 0;

        public bool ShouldLogTransformFallbackSuppressedWarning()
            => Interlocked.Exchange(ref _warnedTransformFallbackSuppressed, 1) == 0;

        public void SetPreparedDemand(bool publishWebSocket, bool publishBridge)
        {
            _preparedPublishWebSocket = publishWebSocket;
            _preparedPublishBridge = publishBridge;
            _hasPreparedPublishDemand = true;
        }

        public void ClearPreparedDemand()
        {
            _hasPreparedPublishDemand = false;
            _preparedPublishWebSocket = false;
            _preparedPublishBridge = false;
        }

        public bool TryGetPreparedDemand(out bool publishWebSocket, out bool publishBridge)
        {
            publishWebSocket = _preparedPublishWebSocket;
            publishBridge = _preparedPublishBridge;
            return _hasPreparedPublishDemand;
        }
    }
}
