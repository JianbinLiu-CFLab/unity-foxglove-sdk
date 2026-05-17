// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Ros2Bridge/Diagnostics
// Purpose: Stable status enums for ROS2 Bridge health diagnostics.

namespace Unity.FoxgloveSDK.Ros2Bridge
{
    /// <summary>Stable per-check health status.</summary>
    public enum Ros2BridgeHealthStatus
    {
        Pass = 0,
        Warning = 1,
        Fail = 2,
        Skipped = 3
    }

    /// <summary>Stable user-facing ROS2 Bridge health summary.</summary>
    public enum Ros2BridgeHealthSummary
    {
        Ready = 0,
        NeedsSetup = 1,
        SidecarNotRunning = 2,
        Failed = 3
    }
}
