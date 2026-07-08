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
    /// Call <see cref="Validate"/> after customizing mutable fields and before
    /// passing an instance to an MCAP reader.
    /// </summary>
    public sealed class McapSequentialReadLimits
    {
        /// <summary>Default retained message count limit for no-index sequential fallback.</summary>
        public const int DefaultMaxMessages = 100000;

        /// <summary>Default retained payload byte limit for no-index sequential fallback.</summary>
        public const long DefaultMaxPayloadBytes = 256L * 1024L * 1024L;
        /// <summary>Default retained metadata record limit for streaming scans.</summary>
        public const int DefaultMaxMetadataRecords = 10000;
        /// <summary>Default retained metadata byte limit for streaming scans.</summary>
        public const long DefaultMaxMetadataBytes = 64L * 1024L * 1024L;
        /// <summary>Default retained attachment record limit for streaming scans.</summary>
        public const int DefaultMaxAttachmentRecords = 10000;
        /// <summary>Default retained attachment byte limit for streaming scans.</summary>
        public const long DefaultMaxAttachmentBytes = 256L * 1024L * 1024L;

        /// <summary>Maximum retained messages. A value of 0 disables the count limit.</summary>
        public int MaxMessages = DefaultMaxMessages;

        /// <summary>Maximum retained payload bytes. A value of 0 disables the payload-byte limit.</summary>
        public long MaxPayloadBytes = DefaultMaxPayloadBytes;
        /// <summary>Maximum retained metadata records. A value of 0 disables the count limit.</summary>
        public int MaxMetadataRecords = DefaultMaxMetadataRecords;
        /// <summary>Maximum retained metadata bytes. A value of 0 disables the byte limit.</summary>
        public long MaxMetadataBytes = DefaultMaxMetadataBytes;
        /// <summary>Maximum retained attachment records. A value of 0 disables the count limit.</summary>
        public int MaxAttachmentRecords = DefaultMaxAttachmentRecords;
        /// <summary>Maximum retained attachment bytes. A value of 0 disables the byte limit.</summary>
        public long MaxAttachmentBytes = DefaultMaxAttachmentBytes;

        /// <summary>Default production limits. The returned instance may be customized by the caller.</summary>
        public static McapSequentialReadLimits Default => new McapSequentialReadLimits();

        /// <summary>Explicitly unbounded limits for small tests and controlled internal fixtures.</summary>
        public static McapSequentialReadLimits UnlimitedForTests => new McapSequentialReadLimits
        {
            MaxMessages = 0,
            MaxPayloadBytes = 0,
            MaxMetadataRecords = 0,
            MaxMetadataBytes = 0,
            MaxAttachmentRecords = 0,
            MaxAttachmentBytes = 0
        };

        /// <summary>Validate limit values before a sequential scan starts.</summary>
        public void Validate()
        {
            if (MaxMessages < 0)
                throw new ArgumentOutOfRangeException(nameof(MaxMessages), "MaxMessages cannot be negative.");
            if (MaxPayloadBytes < 0)
                throw new ArgumentOutOfRangeException(nameof(MaxPayloadBytes), "MaxPayloadBytes cannot be negative.");
            if (MaxMetadataRecords < 0)
                throw new ArgumentOutOfRangeException(nameof(MaxMetadataRecords), "MaxMetadataRecords cannot be negative.");
            if (MaxMetadataBytes < 0)
                throw new ArgumentOutOfRangeException(nameof(MaxMetadataBytes), "MaxMetadataBytes cannot be negative.");
            if (MaxAttachmentRecords < 0)
                throw new ArgumentOutOfRangeException(nameof(MaxAttachmentRecords), "MaxAttachmentRecords cannot be negative.");
            if (MaxAttachmentBytes < 0)
                throw new ArgumentOutOfRangeException(nameof(MaxAttachmentBytes), "MaxAttachmentBytes cannot be negative.");
        }
    }
}
