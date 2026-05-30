// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Transport
// Purpose: Inspector/runtime transport mode selection shared by Unity
// manager code and package documentation.

namespace Unity.FoxgloveSDK.Transport
{
    /// <summary>
    /// Transport listener mode owned by one FoxgloveManager instance.
    /// </summary>
    public enum FoxgloveTransportMode
    {
        /// <summary>Plain local WebSocket listener, for example <c>ws://127.0.0.1:8765</c>.</summary>
        WebSocket,

        /// <summary>TLS WebSocket listener, for example <c>wss://127.0.0.1:8765</c>.</summary>
        SecureWebSocket,

        /// <summary>No transport listener. Use when only ROS2 Bridge output is needed.</summary>
        None = 2
    }
}
