// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Shared fixed cap for custom ROS2 outbound mappings and CDR payloads.

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Bounds every custom DTO outbound representation independently from the
    /// configurable inbound native-copy budget.
    /// </summary>
    public static class FoxRunRos2CustomOutboundBudgetPolicy
    {
        public const long MaximumBytes = 4L * 1024L * 1024L;
    }
}
