// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Attributes
// Purpose: Official ROS 2 durability policies exposed by FoxRun declarations.

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>Portable ROS 2 durability policy.</summary>
    public enum FoxRunQosDurability
    {
        SystemDefault = 1,
        Volatile = 2,
        TransientLocal = 3
    }
}
