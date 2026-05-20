// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity
// Purpose: Declares the node boundary for the optional Unity2Foxglove ROS2 facade.

using System;

namespace Unity2Foxglove.Ros2ForUnity
{
    public interface IUnity2FoxgloveRos2Node : IDisposable
    {
        string Name { get; }

        IUnity2FoxgloveRos2Publisher<T> CreatePublisher<T>(string topic);

        IUnity2FoxgloveRos2Subscription CreateSubscription<T>(string topic, Action<T> callback);
    }
}
