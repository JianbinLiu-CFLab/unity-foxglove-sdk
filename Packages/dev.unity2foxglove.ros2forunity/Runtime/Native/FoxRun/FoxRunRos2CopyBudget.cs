// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native/FoxRun
// Purpose: Safety budget for managed data copied from callback-owned messages.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;

namespace Unity2Foxglove.Ros2ForUnity.Native
{
    /// <summary>
    /// Bounds copied managed storage. Values are not DDS/CDR payload sizes and
    /// are independent of the WebSocket payload limit.
    /// </summary>
    public sealed class FoxRunRos2CopyBudget
    {
        public FoxRunRos2CopyBudget(long maximumBytes)
        {
            if (maximumBytes < 0)
                throw new ArgumentOutOfRangeException(nameof(maximumBytes));
            MaximumBytes = maximumBytes;
            RemainingBytes = maximumBytes;
        }

        public long MaximumBytes { get; }
        public long ConsumedBytes => MaximumBytes - RemainingBytes;
        public long RemainingBytes { get; private set; }

        public void RequireBytes(long byteCount)
        {
            if (byteCount < 0)
                throw new ArgumentOutOfRangeException(nameof(byteCount));
            if (byteCount > RemainingBytes)
                throw new InvalidOperationException("FoxRun ROS2 managed-copy budget exceeded.");
            RemainingBytes -= byteCount;
        }

        public void RequireString(string value)
        {
            if (value != null)
                RequireBytes(checked((long)value.Length * sizeof(char)));
        }

        public void RequireSequenceElements(long elementCount, long elementStorageBytes)
        {
            if (elementCount < 0)
                throw new ArgumentOutOfRangeException(nameof(elementCount));
            if (elementStorageBytes < 0)
                throw new ArgumentOutOfRangeException(nameof(elementStorageBytes));
            RequireBytes(checked(elementCount * elementStorageBytes));
        }
    }
}
#endif
