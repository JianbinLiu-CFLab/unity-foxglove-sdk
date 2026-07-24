// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Attributes
// Purpose: Official ROS 2 history policies exposed by FoxRun declarations.

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>Portable ROS 2 history policy.</summary>
    public enum FoxRunQosHistory
    {
        SystemDefault = 1,
        KeepLast = 2,
        KeepAll = 3
    }
}
