// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity
// Purpose: Declares the node boundary for the optional Unity2Foxglove ROS2 facade.

using System;

namespace Unity2Foxglove.Ros2ForUnity
{
    /// <summary>
    /// ROS2 node boundary exposed by the optional ROS2 For Unity facade.
    /// </summary>
    public interface IUnity2FoxgloveRos2Node : IDisposable
    {
        /// <summary>
        /// Gets the normalized node name used by the backing runtime or no-op facade.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Creates a typed publisher for <paramref name="topic"/>. The generic type is
        /// the concrete ROS2 message type expected by the backing adapter.
        /// </summary>
        IUnity2FoxgloveRos2Publisher<T> CreatePublisher<T>(string topic);

        /// <summary>
        /// Creates a typed subscription for <paramref name="topic"/>. The generic type
        /// is consumed at creation time; the returned disposable intentionally remains
        /// non-generic because the facade v1 only needs lifecycle ownership after
        /// registration.
        /// </summary>
        IUnity2FoxgloveRos2Subscription CreateSubscription<T>(string topic, Action<T> callback);
    }
}
