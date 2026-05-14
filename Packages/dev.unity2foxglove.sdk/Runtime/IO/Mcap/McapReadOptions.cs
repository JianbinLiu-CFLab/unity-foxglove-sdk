// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO/Mcap
// Purpose: Public options for local MCAP indexed message queries.

using System.Collections.Generic;

namespace Unity.FoxgloveSDK.IO
{
    /// <summary>
    /// Options for filtering messages returned by <see cref="McapIndexedReader"/>.
    /// </summary>
    public class McapReadOptions
    {
        /// <summary>
        /// Inclusive lower log-time bound in nanoseconds.
        /// </summary>
        public ulong StartTimeNs = 0;

        /// <summary>
        /// Inclusive upper log-time bound in nanoseconds.
        /// </summary>
        public ulong EndTimeNs = ulong.MaxValue;

        /// <summary>
        /// Topic names to include. Empty means all topics unless channel IDs
        /// are provided.
        /// </summary>
        public List<string> Topics = new List<string>();

        /// <summary>
        /// Channel IDs to include. Empty means all channels unless topics are
        /// provided.
        /// </summary>
        public List<ushort> ChannelIds = new List<ushort>();

        /// <summary>
        /// Maximum number of latest matching messages to keep. Values less
        /// than or equal to zero mean unlimited.
        /// </summary>
        public int MaxMessages = 0;
    }
}
