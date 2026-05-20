// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity
// Purpose: Declares the subscription boundary for the optional Unity2Foxglove ROS2 facade.

using System;

namespace Unity2Foxglove.Ros2ForUnity
{
    public interface IUnity2FoxgloveRos2Subscription : IDisposable
    {
        string Topic { get; }
    }
}
