// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Publishers
// Purpose: Tracks point-cloud source/fallback and prepared-publish demand state.

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
        private bool _hasSourceDrivenFrames;
        private bool _warnedTransformFallbackSuppressed;

        public void MarkSourceDriven()
        {
            _hasSourceDrivenFrames = true;
        }

        public void ResetSourceDriven()
        {
            _hasSourceDrivenFrames = false;
            _warnedTransformFallbackSuppressed = false;
        }

        public bool ShouldSuppressTransformFallback(bool suppressAfterSourceFrames)
            => suppressAfterSourceFrames && _hasSourceDrivenFrames;

        public bool ShouldLogTransformFallbackSuppressedWarning()
        {
            if (_warnedTransformFallbackSuppressed)
                return false;

            _warnedTransformFallbackSuppressed = true;
            return true;
        }

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
