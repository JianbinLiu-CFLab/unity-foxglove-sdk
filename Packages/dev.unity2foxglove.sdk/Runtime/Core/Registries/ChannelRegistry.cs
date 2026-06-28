// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core/Registries
// Purpose: Thread-safe channel ID to descriptor mapping. Channels are
// advertised to Foxglove so clients can discover available topics.

using System;
using System.Collections.Generic;
using Unity.FoxgloveSDK.Protocol;

namespace Unity.FoxgloveSDK.Core
{
    /// <summary>
    /// Thread-safe channel ID → descriptor mapping.
    /// Channels are advertised to Foxglove so the user can see available topics.
    /// </summary>
    public class ChannelRegistry
    {
        private readonly Dictionary<uint, AdvertiseChannel> _channels = new Dictionary<uint, AdvertiseChannel>();
        private readonly object _lock = new object();

        /// <summary>Raised when a channel id is reused with a different descriptor.</summary>
        public event Action<AdvertiseChannel, AdvertiseChannel> ChannelOverwritten;

        /// <summary>Register a new channel. Overwrites if channelId already exists.</summary>
        public void Register(AdvertiseChannel channel)
        {
            if (channel == null) throw new ArgumentNullException(nameof(channel));

            AdvertiseChannel overwritten = null;
            lock (_lock)
            {
                if (_channels.TryGetValue(channel.Id, out var existing)
                    && IsConflictingDescriptor(existing, channel))
                {
                    overwritten = existing;
                }

                _channels[channel.Id] = channel;
            }

            if (overwritten != null)
                ChannelOverwritten?.Invoke(overwritten, channel);
        }

        /// <summary>Remove a channel by ID.</summary>
        public bool Remove(uint channelId)
        {
            lock (_lock)
            {
                return _channels.Remove(channelId);
            }
        }

        /// <summary>Get a channel descriptor by ID, or null.</summary>
        public AdvertiseChannel Get(uint channelId)
        {
            lock (_lock)
            {
                return _channels.TryGetValue(channelId, out var ch) ? ch : null;
            }
        }

        /// <summary>Snapshot of all registered channels.</summary>
        public List<AdvertiseChannel> GetAll()
        {
            lock (_lock)
            {
                return new List<AdvertiseChannel>(_channels.Values);
            }
        }

        /// <summary>Remove all channels.</summary>
        public void Clear()
        {
            lock (_lock)
            {
                _channels.Clear();
            }
        }

        /// <summary>Total number of registered channels.</summary>
        public int Count
        {
            get { lock (_lock) { return _channels.Count; } }
        }

        private static bool IsConflictingDescriptor(AdvertiseChannel left, AdvertiseChannel right)
        {
            if (left == null || right == null)
                return left != right;

            return !string.Equals(left.Topic ?? string.Empty, right.Topic ?? string.Empty, StringComparison.Ordinal)
                   || !string.Equals(left.Encoding ?? string.Empty, right.Encoding ?? string.Empty, StringComparison.Ordinal)
                   || !string.Equals(left.SchemaName ?? string.Empty, right.SchemaName ?? string.Empty, StringComparison.Ordinal)
                   || !string.Equals(left.SchemaEncoding ?? string.Empty, right.SchemaEncoding ?? string.Empty, StringComparison.Ordinal)
                   || !string.Equals(left.Schema ?? string.Empty, right.Schema ?? string.Empty, StringComparison.Ordinal);
        }
    }
}
