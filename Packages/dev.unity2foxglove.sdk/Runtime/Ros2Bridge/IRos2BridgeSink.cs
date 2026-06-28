// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Ros2Bridge
// Purpose: Transport boundary for the experimental Unity-to-ROS2 bridge.

using System;

namespace Unity.FoxgloveSDK.Ros2Bridge
{
    /// <summary>Sink for already-serialized ROS 2 bridge frames.</summary>
    public interface IRos2BridgeSink : IDisposable
    {
        /// <summary>Whether the sink currently has an active transport connection.</summary>
        bool IsConnected { get; }

        /// <summary>Connect or enable delivery for the configured endpoint.</summary>
        void Connect(string host, int port, int timeoutMs);

        /// <summary>Send or enqueue an already-serialized bridge frame.</summary>
        void Send(Ros2BridgeFrame frame, int timeoutMs);

        /// <summary>Disconnect or disable delivery.</summary>
        void Disconnect();
    }
}
