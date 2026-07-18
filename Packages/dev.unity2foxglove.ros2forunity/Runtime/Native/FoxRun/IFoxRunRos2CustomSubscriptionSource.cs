// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native/FoxRun
// Purpose: Generated-code discovery seam for custom ROS2 native subscriptions.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
namespace Unity2Foxglove.Ros2ForUnity.Native
{
    /// <summary>
    /// Kept separate from the packaged-message source seam so Phase179 source
    /// discovery and lifecycle remain byte-for-byte compatible.
    /// </summary>
    public interface IFoxRunRos2CustomSubscriptionSource
    {
        int FoxRunRos2CustomSubscriptionCount { get; }

        void FoxRunRos2RegisterCustomSubscriptions(IFoxRunRos2SubscriptionRegistrar registrar);
    }
}
#endif
