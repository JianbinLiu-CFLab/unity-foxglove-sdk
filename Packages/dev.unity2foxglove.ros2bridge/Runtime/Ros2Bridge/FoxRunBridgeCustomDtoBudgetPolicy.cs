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
        public const int MaximumSequenceItems = 16_384;

        /// <summary>Reject a generated custom DTO sequence above the shared reader limit.</summary>
        public static void EnsureSequenceItems(int count)
        {
            if (count < 0)
                throw new System.ArgumentOutOfRangeException(nameof(count));
            if (count > MaximumSequenceItems)
            {
                throw new Schemas.Ros2Msg.Ros2CdrWriterBudgetExceededException(
                    "Bridge CDR sequence exceeds the custom DTO item budget of "
                    + MaximumSequenceItems
                    + ".");
            }
        }
    }
}
