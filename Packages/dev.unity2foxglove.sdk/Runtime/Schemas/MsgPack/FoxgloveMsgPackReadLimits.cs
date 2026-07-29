// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/MsgPack
// Purpose: Immutable resource limits for untrusted MessagePack input.

using System;

namespace Unity.FoxgloveSDK.Schemas.MsgPack
{
    /// <summary>
    /// Immutable limits captured by one FoxRun subscription session.
    /// </summary>
    public sealed class FoxgloveMsgPackReadLimits
    {
        public const int DefaultMaxDepth = 34;
        public const int AbsoluteMaxContainerItems = 16_384;

        public FoxgloveMsgPackReadLimits(
            int maxDepth,
            int maxContainerItems,
            int maxStringBytes,
            int maxBinaryBytes)
        {
            if (maxDepth <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxDepth));
            if (maxContainerItems < 0)
                throw new ArgumentOutOfRangeException(nameof(maxContainerItems));
            if (maxStringBytes < 0)
                throw new ArgumentOutOfRangeException(nameof(maxStringBytes));
            if (maxBinaryBytes < 0)
                throw new ArgumentOutOfRangeException(nameof(maxBinaryBytes));

            MaxDepth = maxDepth;
            MaxContainerItems = maxContainerItems;
            MaxStringBytes = maxStringBytes;
            MaxBinaryBytes = maxBinaryBytes;
        }

        public int MaxDepth { get; }
        public int MaxContainerItems { get; }
        public int MaxStringBytes { get; }
        public int MaxBinaryBytes { get; }

        /// <summary>Create the exact Phase185 limits for one frozen byte cap.</summary>
        public static FoxgloveMsgPackReadLimits ForPayloadBytes(
            int maxPayloadBytes)
        {
            if (maxPayloadBytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxPayloadBytes));

            return new FoxgloveMsgPackReadLimits(
                DefaultMaxDepth,
                Math.Min(maxPayloadBytes, AbsoluteMaxContainerItems),
                maxPayloadBytes,
                maxPayloadBytes);
        }
    }
}
