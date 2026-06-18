// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core/Routing
// Purpose: Per-sink channel filtering contracts for live and recording outputs.

namespace Unity.FoxgloveSDK.Core
{
    /// <summary>
    /// Runtime sink kinds that can independently accept or reject a registered channel.
    /// </summary>
    public enum FoxgloveSinkKind
    {
        /// <summary>Live Foxglove WebSocket advertise, subscribe, and data routing.</summary>
        LiveWebSocket = 0,

        /// <summary>MCAP recording channel and message writing.</summary>
        McapRecording = 1
    }

    /// <summary>
    /// Immutable metadata supplied to a per-sink channel filter.
    /// </summary>
    public readonly struct SinkChannelFilterContext
    {
        public SinkChannelFilterContext(
            FoxgloveSinkKind sink,
            uint channelId,
            string topic,
            string encoding,
            string schemaName,
            string schemaEncoding)
        {
            Sink = sink;
            ChannelId = channelId;
            Topic = topic ?? string.Empty;
            Encoding = encoding ?? string.Empty;
            SchemaName = schemaName ?? string.Empty;
            SchemaEncoding = schemaEncoding ?? string.Empty;
        }

        public FoxgloveSinkKind Sink { get; }
        public uint ChannelId { get; }
        public string Topic { get; }
        public string Encoding { get; }
        public string SchemaName { get; }
        public string SchemaEncoding { get; }
    }

    /// <summary>
    /// Optional policy for allowing or denying a channel for a specific sink.
    /// </summary>
    public interface ISinkChannelFilter
    {
        bool AllowChannel(SinkChannelFilterContext context);
    }
}
