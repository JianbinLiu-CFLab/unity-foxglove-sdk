// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native/FoxRun
// Purpose: Immutable Manager publish-session identity tracking.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using Unity.FoxgloveSDK.Components;

namespace Unity2Foxglove.Ros2ForUnity.Native
{
    /// <summary>
    /// Tracks immutable Manager publish-session identity. Reference comparison
    /// deliberately catches Manager replacement even when generation and QoS
    /// values happen to match.
    /// </summary>
    internal sealed class FoxRunRos2CustomPublisherSessionTracker
    {
        private FoxRunPublishSessionPolicy _observed;

        internal FoxRunPublishSessionPolicy Current => _observed;

        internal bool AllowsPublishing
            => _observed == null || _observed.SessionActive;

        internal bool Observe(FoxRunPublishSessionPolicy current)
        {
            if (ReferenceEquals(_observed, current))
                return false;

            _observed = current;
            return true;
        }
    }
}
#endif
