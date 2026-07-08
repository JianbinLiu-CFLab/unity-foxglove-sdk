// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core/Replay
// Purpose: Carries replay message source metadata from MCAP replay to scene adapters.

namespace Unity.FoxgloveSDK.Core
{
    /// <summary>
    /// Immutable source context for one replayed MCAP message.
    /// Payload references are shared across replay handlers and must be treated as read-only.
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
        public readonly ulong ReplaySessionId;
        /// <summary>Replayed payload bytes. Treat as read-only; do not mutate this array.</summary>
        public readonly byte[] Payload;

        public ReplayMessageContext(
            ushort channelId,
            string topic,
            string messageEncoding,
            string schemaName,
            string schemaEncoding,
            ulong logTimeNs,
            ulong replayStartTimeNs,
            byte[] payload,
            ulong replaySessionId = 0UL)
        {
            ChannelId = channelId;
            Topic = topic ?? string.Empty;
            MessageEncoding = messageEncoding ?? string.Empty;
            SchemaName = schemaName ?? string.Empty;
            SchemaEncoding = schemaEncoding ?? string.Empty;
            LogTimeNs = logTimeNs;
            ReplayStartTimeNs = replayStartTimeNs;
            ReplaySessionId = replaySessionId;
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
        public readonly ulong ReplaySessionId;
        public readonly int MessageCount;
        public readonly string Source;

        public ReplayBatchContext(
            ulong batchLogTimeNs,
            ulong replayStartTimeNs,
            int messageCount,
            string source,
            ulong replaySessionId = 0UL)
        {
            BatchLogTimeNs = batchLogTimeNs;
            ReplayStartTimeNs = replayStartTimeNs;
            ReplaySessionId = replaySessionId;
            MessageCount = messageCount;
            Source = source ?? string.Empty;
        }
    }
}
