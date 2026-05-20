// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity
// Purpose: Creates the optional Unity2Foxglove ROS2 facade context.

namespace Unity2Foxglove.Ros2ForUnity
{
    public static class Unity2FoxgloveRos2ContextFactory
    {
        public static IUnity2FoxgloveRos2Context Create()
        {
            return Unity2FoxgloveRos2UnavailableContext.Instance;
        }
    }
}
