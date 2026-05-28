// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO/Mcap
// Purpose: Limits for unindexed MCAP sequential fallback message retention.

using System;

namespace Unity.FoxgloveSDK.IO
{
    /// <summary>
    /// Memory guardrails for unindexed MCAP sequential fallback queries.
    /// Exceeding either guard throws <see cref="InvalidOperationException"/>
    /// before more messages are retained.
    /// </summary>
    public sealed class McapSequentialReadLimits
    {
        /// <summary>Default retained message count limit for no-index sequential fallback.</summary>
        public const int DefaultMaxMessages = 100000;

        /// <summary>Default retained payload byte limit for no-index sequential fallback.</summary>
        public const long DefaultMaxPayloadBytes = 256L * 1024L * 1024L;

        /// <summary>Maximum retained messages. A value of 0 disables the count limit.</summary>
        public int MaxMessages = DefaultMaxMessages;

        /// <summary>Maximum retained payload bytes. A value of 0 disables the payload-byte limit.</summary>
        public long MaxPayloadBytes = DefaultMaxPayloadBytes;

        /// <summary>Default production limits. The returned instance may be customized by the caller.</summary>
        public static McapSequentialReadLimits Default => new McapSequentialReadLimits();

        /// <summary>Explicitly unbounded limits for small tests and controlled internal fixtures.</summary>
        public static McapSequentialReadLimits UnlimitedForTests => new McapSequentialReadLimits
        {
            MaxMessages = 0,
            MaxPayloadBytes = 0
        };

        /// <summary>Validate limit values before a sequential scan starts.</summary>
        public void Validate()
        {
            if (MaxMessages < 0)
                throw new ArgumentOutOfRangeException(nameof(MaxMessages), "MaxMessages cannot be negative.");
            if (MaxPayloadBytes < 0)
                throw new ArgumentOutOfRangeException(nameof(MaxPayloadBytes), "MaxPayloadBytes cannot be negative.");
        }
    }
}
