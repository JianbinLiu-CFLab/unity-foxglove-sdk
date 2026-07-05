// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Threading;
using Unity.FoxgloveSDK.Protocol;

namespace Unity.FoxgloveSDK.Core
{
    /// <summary>
    /// Per-sink channel filter state for live WebSocket and MCAP outputs.
    /// </summary>
    internal sealed class SessionChannelFilter
    {
        private ISinkChannelFilter _liveWebSocketChannelFilter;
        private ISinkChannelFilter _mcapRecordingChannelFilter;

        internal void SetSinkChannelFilter(FoxgloveSinkKind sink, ISinkChannelFilter filter)
        {
            switch (sink)
            {
                case FoxgloveSinkKind.LiveWebSocket:
                    Volatile.Write(ref _liveWebSocketChannelFilter, filter);
                    break;
                case FoxgloveSinkKind.McapRecording:
                    Volatile.Write(ref _mcapRecordingChannelFilter, filter);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(sink), sink, "Unknown Foxglove sink kind.");
            }
        }

        internal ISinkChannelFilter GetSinkChannelFilter(FoxgloveSinkKind sink)
        {
            return sink switch
            {
                FoxgloveSinkKind.LiveWebSocket => Volatile.Read(ref _liveWebSocketChannelFilter),
                FoxgloveSinkKind.McapRecording => Volatile.Read(ref _mcapRecordingChannelFilter),
                _ => throw new ArgumentOutOfRangeException(nameof(sink), sink, "Unknown Foxglove sink kind.")
            };
        }

        internal IReadOnlyCollection<AdvertiseChannel> FilterLiveChannels(IReadOnlyCollection<AdvertiseChannel> channels)
        {
            if (channels == null || channels.Count == 0)
                return channels ?? Array.Empty<AdvertiseChannel>();

            var filter = Volatile.Read(ref _liveWebSocketChannelFilter);
            if (filter == null)
                return channels;

            var filtered = new List<AdvertiseChannel>();
            foreach (var channel in channels)
            {
                if (channel != null && filter.AllowChannel(CreateFilterContext(FoxgloveSinkKind.LiveWebSocket, channel)))
                    filtered.Add(channel);
            }

            return filtered;
        }

        internal bool AllowLiveWebSocket(AdvertiseChannel channel)
            => AllowChannel(FoxgloveSinkKind.LiveWebSocket, channel);

        internal bool AllowMcapRecording(AdvertiseChannel channel)
            => AllowChannel(FoxgloveSinkKind.McapRecording, channel);

        internal bool AllowChannel(FoxgloveSinkKind sink, AdvertiseChannel channel)
        {
            if (channel == null)
                return false;

            var filter = GetSinkChannelFilter(sink);
            return filter == null || filter.AllowChannel(CreateFilterContext(sink, channel));
        }

        private static SinkChannelFilterContext CreateFilterContext(FoxgloveSinkKind sink, AdvertiseChannel channel)
            => new SinkChannelFilterContext(
                sink,
                channel.Id,
                channel.Topic,
                channel.Encoding,
                channel.SchemaName,
                channel.SchemaEncoding);
    }
}
