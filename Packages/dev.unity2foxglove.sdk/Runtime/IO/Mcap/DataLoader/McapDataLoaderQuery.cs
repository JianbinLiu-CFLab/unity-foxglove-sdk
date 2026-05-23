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
        /// <summary>Inclusive lower log-time bound in nanoseconds.</summary>
        public ulong StartTimeNs = 0;

        /// <summary>Inclusive upper log-time bound in nanoseconds.</summary>
        public ulong EndTimeNs = ulong.MaxValue;

        /// <summary>Optional channel ID filter; empty means all channels.</summary>
        public List<ushort> ChannelIds = new List<ushort>();

        /// <summary>Optional topic filter; empty means all topics.</summary>
        public List<string> Topics = new List<string>();

        /// <summary>Optional latest-message cap; zero means unlimited.</summary>
        public int MaxMessages = 0;
    }
}
