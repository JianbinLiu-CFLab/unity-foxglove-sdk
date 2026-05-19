// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity
// Purpose: Defines the status values for the optional Unity2Foxglove ROS2 facade.

namespace Unity2Foxglove.Ros2ForUnity
{
    public enum Unity2FoxgloveRos2Status
    {
        Unavailable = 0,
        Ready = 1,
        Error = 2,
        Disposed = 3
    }
}
