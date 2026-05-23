// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO/Mcap/DataLoader
// Purpose: Backfill query options for local MCAP DataLoader lookups.

using System.Collections.Generic;

namespace Unity.FoxgloveSDK.IO
{
    /// <summary>Per-channel latest-message lookup at or before a log timestamp.</summary>
    public sealed class McapDataLoaderBackfillQuery
    {
        /// <summary>Inclusive lookup timestamp in nanoseconds.</summary>
        public ulong TimeNs = ulong.MaxValue;

        /// <summary>Optional channel ID filter; empty means all channels.</summary>
        public List<ushort> ChannelIds = new List<ushort>();

        /// <summary>Optional topic filter; empty means all topics.</summary>
        public List<string> Topics = new List<string>();
    }
}
