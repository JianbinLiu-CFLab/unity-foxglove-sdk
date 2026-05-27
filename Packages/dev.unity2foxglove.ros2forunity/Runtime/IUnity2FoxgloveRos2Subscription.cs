// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity
// Purpose: Declares the subscription boundary for the optional Unity2Foxglove ROS2 facade.

using System;

namespace Unity2Foxglove.Ros2ForUnity
{
    /// <summary>
    /// Disposable subscription ownership token returned by the optional ROS2 facade.
    /// Message type information is consumed by <c>CreateSubscription&lt;T&gt;</c>.
    /// </summary>
    public interface IUnity2FoxgloveRos2Subscription : IDisposable
    {
        /// <summary>
        /// Gets the normalized topic this subscription listens to.
        /// </summary>
        string Topic { get; }
    }
}
