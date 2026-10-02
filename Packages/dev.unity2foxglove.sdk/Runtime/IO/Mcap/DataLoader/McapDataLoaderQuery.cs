// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO/Mcap/DataLoader
// Purpose: Query options for local MCAP DataLoader message iteration.

using System.Collections.Generic;

namespace Unity.FoxgloveSDK.IO
{
    /// <summary>Filter options for deterministic local MCAP message iteration.</summary>
    public sealed class McapDataLoaderQuery
    {
        /// <summary>Default retained message count for iterator queries.</summary>
        public const int DefaultMaxMessages = 4096;

        /// <summary>Inclusive lower log-time bound in nanoseconds.</summary>
        public ulong StartTimeNs = 0;

        /// <summary>Inclusive upper log-time bound in nanoseconds.</summary>
        public ulong EndTimeNs = ulong.MaxValue;

        /// <summary>Optional channel ID filter; empty means all channels. When both filters are set, a message matching either filter is included.</summary>
        public List<ushort> ChannelIds = new List<ushort>();

        /// <summary>Optional topic filter; empty means all topics. When both filters are set, topics and channel IDs are combined with union semantics.</summary>
        public List<string> Topics = new List<string>();

        /// <summary>
        /// Bounded result count. Eager log-time queries retain the latest matching messages;
        /// lazy file-order queries retain the first matching messages because they are
        /// forward-only. Defaults to <see cref="DefaultMaxMessages"/>; set to zero to opt
        /// in to an unlimited query.
        /// </summary>
        public int MaxMessages = DefaultMaxMessages;
    }
}
