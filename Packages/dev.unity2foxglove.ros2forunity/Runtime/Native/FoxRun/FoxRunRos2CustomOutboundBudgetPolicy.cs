// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native/FoxRun
// Purpose: R2FU-owned fixed cap for custom outbound mappings.

namespace Unity2Foxglove.Ros2ForUnity.Native
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
