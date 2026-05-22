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
        bool IsAvailable { get; }

        Unity2FoxgloveRos2Status Status { get; }

        string StatusMessage { get; }

        IUnity2FoxgloveRos2Node CreateNode(string nodeName);
    }
}
