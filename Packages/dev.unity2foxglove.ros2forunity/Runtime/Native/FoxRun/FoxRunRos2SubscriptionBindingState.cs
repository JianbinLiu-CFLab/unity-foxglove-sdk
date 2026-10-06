// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native/FoxRun
// Purpose: Stable native subscription binding lifecycle states.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
namespace Unity2Foxglove.Ros2ForUnity.Native
{
    public enum FoxRunRos2SubscriptionBindingState
    {
        Configured = 0,
        WaitingForRuntime = 1,
        Ready = 2,
        Receiving = 3,
        Unsupported = 4,
        Failed = 5,
        Stopped = 6
    }
}
#endif
