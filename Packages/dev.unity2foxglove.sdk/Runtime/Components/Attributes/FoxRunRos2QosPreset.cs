// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Attributes
// Purpose: Portable FoxRun ROS2 subscription QoS policy.

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Declares the portable ROS2 QoS preset for a native FoxRun subscription.
    /// </summary>
    public enum FoxRunRos2QosPreset
    {
        /// <summary>Resolve through the FoxgloveManager default.</summary>
        Inherit = 0,

        /// <summary>Use the optional ROS2 provider's default QoS.</summary>
        Default = 1,

        /// <summary>Use a reliable delivery preset.</summary>
        Reliable = 2,

        /// <summary>Use a sensor-data preset optimized for current samples.</summary>
        SensorData = 3,

        /// <summary>Use a transient-local durability preset.</summary>
        TransientLocal = 4
    }
}
