// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Attributes
// Purpose: Official ROS 2 QoS base profiles exposed by FoxRun declarations.

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>Portable ROS 2 QoS base profile.</summary>
    public enum FoxRunQosProfile
    {
        /// <summary>Reliable, volatile, Keep Last 10.</summary>
        Default = 1,

        /// <summary>Best effort, volatile, Keep Last 5.</summary>
        SensorData = 2,

        /// <summary>Delegate every QoS policy to the active ROS 2 system.</summary>
        SystemDefault = 3
    }
}
