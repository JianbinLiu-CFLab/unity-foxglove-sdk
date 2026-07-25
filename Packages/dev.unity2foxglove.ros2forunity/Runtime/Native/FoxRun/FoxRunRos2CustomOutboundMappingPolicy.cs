// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native/FoxRun
// Purpose: Fixed outbound custom-DTO mapping cap, independent from inbound copying.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using Unity.FoxgloveSDK.Components;

namespace Unity2Foxglove.Ros2ForUnity.Native
{
    /// <summary>
    /// Native custom DTO output has one fixed, optional-facade cap. It must not
    /// read or inherit the Manager's inbound callback-copy budget.
    /// </summary>
    public static class FoxRunRos2CustomOutboundMappingPolicy
    {
        public const long MaximumBytes =
            FoxRunRos2CustomOutboundBudgetPolicy.MaximumBytes;

        public static FoxRunRos2CustomOutboundMappingContext CreateContext()
            => new FoxRunRos2CustomOutboundMappingContext(MaximumBytes);
    }

    /// <summary>Typed signal used only to classify the fixed custom output cap.</summary>
    public sealed class FoxRunRos2CustomOutboundBudgetExceededException : InvalidOperationException
    {
        internal FoxRunRos2CustomOutboundBudgetExceededException()
            : base("FoxRun custom ROS2 outbound mapping budget exceeded.")
        {
        }
    }

    /// <summary>Tracks managed storage allocated while mapping one DTO to an owned ROS envelope.</summary>
    public sealed class FoxRunRos2CustomOutboundMappingContext
    {
        internal FoxRunRos2CustomOutboundMappingContext(long maximumBytes)
        {
            if (maximumBytes < 0)
                throw new ArgumentOutOfRangeException(nameof(maximumBytes));
            RemainingBytes = maximumBytes;
        }

        public long RemainingBytes { get; private set; }

        public void RequireBytes(long byteCount)
        {
            if (byteCount < 0)
                throw new ArgumentOutOfRangeException(nameof(byteCount));
            if (byteCount > RemainingBytes)
                throw new FoxRunRos2CustomOutboundBudgetExceededException();
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
