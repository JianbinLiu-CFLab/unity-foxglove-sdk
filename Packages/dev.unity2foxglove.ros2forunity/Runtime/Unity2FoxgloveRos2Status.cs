// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity
// Purpose: Defines the status values for the optional Unity2Foxglove ROS2 facade.

namespace Unity2Foxglove.Ros2ForUnity
{
    /// <summary>
    /// Lifecycle states reported by the optional ROS2 For Unity facade.
    /// </summary>
    public enum Unity2FoxgloveRos2Status
    {
        /// <summary>The base facade or adapter has no active ROS2 runtime.</summary>
        Unavailable = 0,
        /// <summary>The adapter is backed by a runtime and can create live nodes.</summary>
        Ready = 1,
        /// <summary>The adapter encountered a runtime or configuration error.</summary>
        Error = 2,
        /// <summary>A real runtime-backed context has been disposed.</summary>
        Disposed = 3
    }
}
