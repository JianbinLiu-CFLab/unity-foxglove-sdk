// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO/Mcap/DataLoader
// Purpose: Inclusive log-time range DTO for local MCAP DataLoader initialization.

namespace Unity.FoxgloveSDK.IO
{
    /// <summary>Inclusive nanosecond log-time range for an initialized MCAP file.</summary>
    public sealed class McapDataLoaderTimeRange
    {
        /// <summary>True when the MCAP summary can report a message time range.</summary>
        public bool HasRange;

        /// <summary>Earliest message log timestamp in nanoseconds.</summary>
        public ulong StartTimeNs;

        /// <summary>Latest message log timestamp in nanoseconds.</summary>
        public ulong EndTimeNs;
    }
}
