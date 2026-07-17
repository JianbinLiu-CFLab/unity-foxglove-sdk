// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Attributes
// Purpose: Declared FoxRun inbound subscription-provider policy.

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Declares which provider receives inbound data for a FoxRun topic.
    /// Provider selection is independent of <see cref="FoxRunWireEncoding"/>.
    /// </summary>
    public enum FoxRunSubscriptionProvider
    {
        /// <summary>Resolve through the FoxgloveManager default.</summary>
        Inherit = 0,

        /// <summary>Receive subscriptions through the Foxglove WebSocket server.</summary>
        FoxgloveWebSocket = 1,

        /// <summary>
        /// Receive subscriptions through the optional R2FU/ROS2 provider. This
        /// is not Foxglove <c>cdr</c> encoding and is not necessarily DDS-backed.
        /// </summary>
        Ros2Native = 2
    }
}
