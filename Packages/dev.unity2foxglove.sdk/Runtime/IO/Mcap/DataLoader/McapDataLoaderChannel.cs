// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO/Mcap/DataLoader
// Purpose: Channel DTO exposed by the local MCAP DataLoader facade.

namespace Unity.FoxgloveSDK.IO
{
    /// <summary>Channel summary exposed by local MCAP DataLoader initialization.</summary>
    public sealed class McapDataLoaderChannel
    {
        /// <summary>MCAP channel ID.</summary>
        public ushort ChannelId;

        /// <summary>MCAP schema ID referenced by this channel, or zero when absent.</summary>
        public ushort SchemaId;

        /// <summary>Topic recorded for this channel.</summary>
        public string Topic;

        /// <summary>Message encoding recorded for this channel.</summary>
        public string MessageEncoding;

        /// <summary>True when summary statistics include a per-channel message count.</summary>
        public bool HasMessageCount;

        /// <summary>Per-channel message count from summary statistics.</summary>
        public ulong MessageCount;

        /// <summary>Creates an empty channel summary.</summary>
        public McapDataLoaderChannel()
        {
            Topic = string.Empty;
            MessageEncoding = string.Empty;
        }
    }
}
