// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity
// Purpose: Creates the optional Unity2Foxglove ROS2 facade context.

namespace Unity2Foxglove.Ros2ForUnity
{
    /// <summary>
    /// Creates the optional ROS2 facade context.
    /// </summary>
    /// <remarks>
    /// This adapter package is facade-only and does not bundle a ROS2 runtime by
    /// itself, so <see cref="Create"/> always returns the shared unavailable
    /// context. A real ROS2 For Unity adapter should expose its own factory or
    /// registration path instead of expecting this base facade to discover runtime
    /// binaries automatically.
    /// </remarks>
    public static class Unity2FoxgloveRos2ContextFactory
    {
        /// <summary>
        /// Returns the shared unavailable facade context for the base package.
        /// </summary>
        public static IUnity2FoxgloveRos2Context Create()
        {
            return Unity2FoxgloveRos2UnavailableContext.Instance;
        }
    }
}
