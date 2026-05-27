// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity
// Purpose: Declares the root context for the optional Unity2Foxglove ROS2 facade.

using System;

namespace Unity2Foxglove.Ros2ForUnity
{
    /// <summary>
    /// Root context for the optional ROS2 For Unity package boundary.
    /// </summary>
    public interface IUnity2FoxgloveRos2Context : IDisposable
    {
        /// <summary>
        /// Gets whether this context is backed by an active ROS2 For Unity runtime.
        /// Facade-only contexts return <c>false</c> and should not be treated as
        /// publish/subscribe capable.
        /// </summary>
        bool IsAvailable { get; }

        /// <summary>
        /// Gets the current lifecycle state for this context. Implementations backed
        /// by a real runtime should report <see cref="Unity2FoxgloveRos2Status.Disposed"/>
        /// after disposal; the shared unavailable singleton remains
        /// <see cref="Unity2FoxgloveRos2Status.Unavailable"/> because it represents a
        /// missing runtime rather than a disposable runtime instance.
        /// </summary>
        Unity2FoxgloveRos2Status Status { get; }

        /// <summary>
        /// Gets a human-readable status explanation suitable for Inspector and smoke
        /// evidence output.
        /// </summary>
        string StatusMessage { get; }

        /// <summary>
        /// Creates or returns a node boundary with the supplied ROS2 node name.
        /// Unavailable contexts return a no-op node that preserves normalized names
        /// and reports publish failures through <c>TryPublish</c>.
        /// </summary>
        IUnity2FoxgloveRos2Node CreateNode(string nodeName);
    }
}
