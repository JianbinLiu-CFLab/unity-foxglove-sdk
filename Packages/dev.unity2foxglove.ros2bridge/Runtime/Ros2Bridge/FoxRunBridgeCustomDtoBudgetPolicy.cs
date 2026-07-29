// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Ros2Bridge
// Purpose: Bounds one generated custom-DTO CDR envelope owned by the Bridge Provider.

namespace Unity2Foxglove.Ros2Bridge
{
    public static class FoxRunBridgeCustomDtoBudgetPolicy
    {
        public const long MaximumBytes = 4L * 1024L * 1024L;
    }
}
