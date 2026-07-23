// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Attributes
// Purpose: Shared FoxRun publish-target and subscribe-source vocabulary.

using System;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Identifies a FoxRun transport endpoint. <see cref="FoxRunAttribute.Source"/>
    /// accepts one value, while <see cref="FoxRunAttribute.Targets"/> accepts a
    /// non-empty flags set.
    /// </summary>
    [Flags]
    public enum FoxRunEndpoint
    {
        /// <summary>Foxglove WebSocket transport.</summary>
        Foxglove = 1 << 0,

        /// <summary>In-process ROS 2 transport supplied by the optional R2FU package.</summary>
        Ros2Native = 1 << 1,

        /// <summary>Local ROS 2 sidecar bridge publish transport.</summary>
        Ros2Bridge = 1 << 2
    }
}
