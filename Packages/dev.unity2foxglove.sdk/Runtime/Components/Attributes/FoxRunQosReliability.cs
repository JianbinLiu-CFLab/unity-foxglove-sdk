// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Attributes
// Purpose: Official ROS 2 reliability policies exposed by FoxRun declarations.

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>Portable ROS 2 reliability policy.</summary>
    public enum FoxRunQosReliability
    {
        SystemDefault = 1,
        Reliable = 2,
        BestEffort = 3
    }
}
