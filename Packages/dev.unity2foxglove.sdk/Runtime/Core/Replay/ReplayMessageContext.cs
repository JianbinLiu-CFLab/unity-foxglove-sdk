// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core/Replay
// Purpose: Carries replay message source metadata from MCAP replay to scene adapters.

namespace Unity.FoxgloveSDK.Core
{
    /// <summary>
    /// Immutable source context for one replayed MCAP message.
    /// </summary>
    public readonly struct ReplayMessageContext
    {
        public readonly ushort ChannelId;
        public readonly string Topic;
        public readonly string MessageEncoding;
        public readonly string SchemaName;
        public readonly string SchemaEncoding;
        public readonly ulong LogTimeNs;
        public readonly ulong ReplayStartTimeNs;
        public readonly byte[] Payload;

        public ReplayMessageContext(
            ushort channelId,
            string topic,
            string messageEncoding,
            string schemaName,
            string schemaEncoding,
            ulong logTimeNs,
            ulong replayStartTimeNs,
            byte[] payload)
        {
            ChannelId = channelId;
            Topic = topic ?? string.Empty;
            MessageEncoding = messageEncoding ?? string.Empty;
            SchemaName = schemaName ?? string.Empty;
            SchemaEncoding = schemaEncoding ?? string.Empty;
            LogTimeNs = logTimeNs;
            ReplayStartTimeNs = replayStartTimeNs;
            Payload = payload;
        }
    }

    /// <summary>
    /// Immutable context emitted after a replay controller batch has been forwarded.
    /// </summary>
    public readonly struct ReplayBatchContext
    {
        public readonly ulong BatchLogTimeNs;
        public readonly ulong ReplayStartTimeNs;
        public readonly int MessageCount;
        public readonly string Source;

        public ReplayBatchContext(
            ulong batchLogTimeNs,
            ulong replayStartTimeNs,
            int messageCount,
            string source)
        {
            BatchLogTimeNs = batchLogTimeNs;
            ReplayStartTimeNs = replayStartTimeNs;
            MessageCount = messageCount;
            Source = source ?? string.Empty;
        }
    }
}
