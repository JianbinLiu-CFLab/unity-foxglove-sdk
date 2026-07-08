// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Threading;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Protocol;

namespace Unity.FoxgloveSDK.RemoteGateway
{
    internal sealed class RemoteGatewayMirrorSink : IFoxgloveMirrorSink, IDisposable
    {
        private readonly RemoteGatewayChannelRegistry _channels;
        private int _enabled;
        private long _mirroredMessageCount;
        private long _droppedMessageCount;
        private long _channelRegistrationFailureCount;
        private int _disposed;

        internal RemoteGatewayMirrorSink(RemoteGatewayChannelRegistry channels)
        {
            _channels = channels ?? throw new ArgumentNullException(nameof(channels));
        }

        internal bool Enabled => Volatile.Read(ref _enabled) != 0;
        internal long MirroredMessageCount => Interlocked.Read(ref _mirroredMessageCount);
        internal long DroppedMessageCount => Interlocked.Read(ref _droppedMessageCount);
        internal long ChannelRegistrationFailureCount => Interlocked.Read(ref _channelRegistrationFailureCount);

        internal void Enable() => Volatile.Write(ref _enabled, 1);
        internal void Disable() => Volatile.Write(ref _enabled, 0);

        public bool HasChannelDemand(AdvertiseChannel channel)
            => Enabled && channel != null;

        public void RegisterChannel(AdvertiseChannel channel)
        {
            if (!HasChannelDemand(channel))
                return;

            if (!_channels.RegisterChannel(channel))
                Interlocked.Increment(ref _channelRegistrationFailureCount);
        }

        public void UnregisterChannel(uint channelId)
            => _channels.UnregisterChannel(channelId);

        public void Publish(AdvertiseChannel channel, ulong logTimeNs, byte[] payload)
        {
            if (!HasChannelDemand(channel))
                return;

            if (_channels.Publish(channel.Id, logTimeNs, payload))
                Interlocked.Increment(ref _mirroredMessageCount);
            else
                Interlocked.Increment(ref _droppedMessageCount);
        }

        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
                return;

            Disable();
            _channels.Dispose();
        }
    }
}
