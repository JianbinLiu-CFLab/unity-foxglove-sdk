// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity
// Purpose: Declares the publisher boundary for the optional Unity2Foxglove ROS2 facade.

using System;

namespace Unity2Foxglove.Ros2ForUnity
{
    /// <summary>
    /// Typed ROS2 publisher boundary exposed by the optional ROS2 For Unity facade.
    /// </summary>
    public interface IUnity2FoxgloveRos2Publisher<in T> : IDisposable
    {
        /// <summary>
        /// Gets the normalized topic this publisher targets.
        /// </summary>
        string Topic { get; }

        /// <summary>
        /// Attempts to publish <paramref name="message"/>. Implementations should
        /// return <c>false</c> with a concise <paramref name="error"/> instead of
        /// throwing for unavailable runtime, disposed publisher, or unsupported
        /// message type conditions.
        /// </summary>
        bool TryPublish(T message, out string error);
    }
}
