// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native/FoxRun
// Purpose: Public compile-time seam implemented by generated FoxRun partial types.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
namespace Unity2Foxglove.Ros2ForUnity.Native
{
    /// <summary>
    /// Generated-code-facing source of statically typed native ROS2 subscriptions.
    /// Backend discovery and lifecycle implementation remain internal to the
    /// optional Native package.
    /// </summary>
    public interface IFoxRunRos2SubscriptionSource
    {
        int FoxRunRos2SubscriptionCount { get; }

        void FoxRunRos2RegisterSubscriptions(IFoxRunRos2SubscriptionRegistrar registrar);
    }
}
#endif
