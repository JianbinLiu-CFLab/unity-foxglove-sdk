// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Resolves portable FoxRun ROS2 QoS declarations against Manager policy.

using System;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>Pure resolver for portable ROS2 subscription QoS presets.</summary>
    public static class FoxRunRos2QosResolver
    {
        /// <summary>Resolves a source declaration to one concrete QoS preset.</summary>
        public static FoxRunRos2QosPreset Resolve(
            FoxRunRos2QosPreset declaredPreset,
            FoxRunRos2QosPreset managerDefault)
        {
            switch (declaredPreset)
            {
                case FoxRunRos2QosPreset.Default:
                case FoxRunRos2QosPreset.Reliable:
                case FoxRunRos2QosPreset.SensorData:
                case FoxRunRos2QosPreset.TransientLocal:
                    return declaredPreset;
                case FoxRunRos2QosPreset.Inherit:
                    return NormalizeManagerDefault(managerDefault);
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(declaredPreset),
                        "FoxRun ROS2 QoS declaration is invalid.");
            }
        }

        /// <summary>Normalizes the Manager's source-only Inherit state to Default.</summary>
        public static FoxRunRos2QosPreset NormalizeManagerDefault(
            FoxRunRos2QosPreset managerDefault)
        {
            switch (managerDefault)
            {
                case FoxRunRos2QosPreset.Inherit:
                case FoxRunRos2QosPreset.Default:
                    return FoxRunRos2QosPreset.Default;
                case FoxRunRos2QosPreset.Reliable:
                case FoxRunRos2QosPreset.SensorData:
                case FoxRunRos2QosPreset.TransientLocal:
                    return managerDefault;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(managerDefault),
                        "FoxRun Manager ROS2 QoS default is invalid.");
            }
        }
    }
}
