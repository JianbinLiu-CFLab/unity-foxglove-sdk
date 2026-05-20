// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity
// Purpose: Declares the publisher boundary for the optional Unity2Foxglove ROS2 facade.

using System;

namespace Unity2Foxglove.Ros2ForUnity
{
    public interface IUnity2FoxgloveRos2Publisher<in T> : IDisposable
    {
        string Topic { get; }

        bool TryPublish(T message, out string error);
    }
}
