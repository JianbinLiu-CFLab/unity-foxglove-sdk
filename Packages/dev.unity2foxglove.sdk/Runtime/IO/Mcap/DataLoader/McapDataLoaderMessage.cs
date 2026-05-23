// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO/Mcap/DataLoader
// Purpose: Raw message DTO returned by local MCAP DataLoader queries.

namespace Unity.FoxgloveSDK.IO
{
    /// <summary>Raw serialized MCAP message with channel and timing context.</summary>
    public sealed class McapDataLoaderMessage
    {
        /// <summary>MCAP channel ID for this message.</summary>
        public ushort ChannelId;

        /// <summary>Schema ID referenced by the message channel, or zero when absent.</summary>
        public ushort SchemaId;

        /// <summary>Topic recorded for the message channel.</summary>
        public string Topic;

        /// <summary>Message encoding recorded for the message channel.</summary>
        public string MessageEncoding;

        /// <summary>MCAP message sequence number.</summary>
        public uint Sequence;

        /// <summary>Message log timestamp in nanoseconds.</summary>
        public ulong LogTime;

        /// <summary>Message publish timestamp in nanoseconds.</summary>
        public ulong PublishTime;

        /// <summary>Raw serialized message payload bytes.</summary>
        public byte[] Data;

        /// <summary>Creates an empty raw-message DTO.</summary>
        public McapDataLoaderMessage()
        {
            Topic = string.Empty;
            MessageEncoding = string.Empty;
            Data = new byte[0];
        }
    }
}
