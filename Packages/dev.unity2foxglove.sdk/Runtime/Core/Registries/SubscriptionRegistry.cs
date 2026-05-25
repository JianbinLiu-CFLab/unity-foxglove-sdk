// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core/Registries
// Purpose: Tracks per-client subscription state. Maps clientId to
// (subscriptionId to channelId) for MessageData routing.

using System.Collections.Generic;

namespace Unity.FoxgloveSDK.Core
{
    /// <summary>
    /// Tracks per-client subscription state.
    /// Maps clientId → (subscriptionId → channelId).
    /// </summary>
    public class SubscriptionRegistry
    {
        private readonly Dictionary<uint, Dictionary<uint, uint>> _clients
            = new Dictionary<uint, Dictionary<uint, uint>>();

        private readonly Dictionary<uint, List<(uint clientId, uint subscriptionId)>> _byChannel
            = new Dictionary<uint, List<(uint clientId, uint subscriptionId)>>();

        private readonly object _lock = new object();

        /// <summary>Add a subscription for a client. Called when a "subscribe" message is received.</summary>
        public void AddSubscription(uint clientId, uint subscriptionId, uint channelId)
        {
            lock (_lock)
            {
                if (!_clients.TryGetValue(clientId, out var subs))
                {
                    subs = new Dictionary<uint, uint>();
                    _clients[clientId] = subs;
                }

                if (subs.TryGetValue(subscriptionId, out var previousChannelId))
                    RemoveReverseIndex(previousChannelId, clientId, subscriptionId);

                subs[subscriptionId] = channelId;
                AddReverseIndex(channelId, clientId, subscriptionId);
            }
        }

        /// <summary>
        /// Remove subscriptions by their IDs. Returns the (subscriptionId, channelId)
        /// pairs that were removed, so callers can clean up graph entries using the
        /// same subscriptionId that HandleSubscribe added.
        /// </summary>
        public List<(uint subscriptionId, uint channelId)> RemoveSubscriptions(uint clientId, IEnumerable<uint> subscriptionIds)
        {
            lock (_lock)
            {
                var removed = new List<(uint, uint)>();
                if (_clients.TryGetValue(clientId, out var subs))
                {
                    foreach (var sid in subscriptionIds)
                    {
                        if (subs.TryGetValue(sid, out var chId))
                        {
                            removed.Add((sid, chId));
                            subs.Remove(sid);
                            RemoveReverseIndex(chId, clientId, sid);
                        }
                    }
                }
                return removed;
            }
        }

        /// <summary>Remove all subscriptions for a client (e.g. on disconnect).</summary>
        public void RemoveClient(uint clientId)
        {
            lock (_lock)
            {
                if (_clients.TryGetValue(clientId, out var subs))
                {
                    foreach (var (subId, chId) in subs)
                        RemoveReverseIndex(chId, clientId, subId);
                    _clients.Remove(clientId);
                }
            }
        }

        /// <summary>
        /// Snapshot all (subscriptionId, channelId) pairs for a client and remove them,
        /// so callers can clean up graph entries before the data is gone.
        /// </summary>
        public List<(uint subscriptionId, uint channelId)> RemoveClientPreservingData(uint clientId)
        {
            lock (_lock)
            {
                var result = new List<(uint, uint)>();
                if (_clients.TryGetValue(clientId, out var subs))
                {
                    foreach (var (subId, chId) in subs)
                    {
                        result.Add((subId, chId));
                        RemoveReverseIndex(chId, clientId, subId);
                    }
                    _clients.Remove(clientId);
                }
                return result;
            }
        }

        /// <summary>
        /// Remove all subscriptions targeting a channel and return removed
        /// client/subscription pairs for connection graph cleanup.
        /// </summary>
        public List<(uint clientId, uint subscriptionId, uint channelId)> RemoveChannel(uint channelId)
        {
            lock (_lock)
            {
                var removed = new List<(uint, uint, uint)>();
                if (!_byChannel.TryGetValue(channelId, out var subscribers))
                    return removed;

                foreach (var (clientId, subId) in subscribers)
                {
                    if (_clients.TryGetValue(clientId, out var subs)
                        && subs.TryGetValue(subId, out var chId)
                        && chId == channelId)
                    {
                        subs.Remove(subId);
                        removed.Add((clientId, subId, chId));
                    }
                }

                _byChannel.Remove(channelId);
                return removed;
            }
        }

        /// <summary>
        /// Snapshot of (clientId, subscriptionId) pairs subscribed to a given channel.
        /// Returns a materialized list so callers don't hold the lock.
        /// </summary>
        public List<(uint clientId, uint subscriptionId)> GetSubscribersForChannel(uint channelId)
        {
            var result = new List<(uint, uint)>();
            CopySubscribersForChannel(channelId, result);
            return result;
        }

        /// <summary>
        /// Copy subscribers for a channel into a caller-owned list.
        /// </summary>
        public void CopySubscribersForChannel(uint channelId, List<(uint clientId, uint subscriptionId)> destination)
        {
            if (destination == null)
                return;

            lock (_lock)
            {
                destination.Clear();
                if (_byChannel.TryGetValue(channelId, out var subscribers))
                    destination.AddRange(subscribers);
            }
        }

        /// <summary>
        /// Return whether any client is currently subscribed to a channel.
        /// </summary>
        public bool HasSubscribersForChannel(uint channelId)
        {
            lock (_lock)
            {
                return _byChannel.TryGetValue(channelId, out var subscribers) && subscribers.Count > 0;
            }
        }

        /// <summary>Remove all state.</summary>
        public void Clear()
        {
            lock (_lock)
            {
                _clients.Clear();
                _byChannel.Clear();
            }
        }

        /// <summary>Total number of clients with active subscriptions.</summary>
        public int ClientCount
        {
            get { lock (_lock) { return _clients.Count; } }
        }

        private void AddReverseIndex(uint channelId, uint clientId, uint subscriptionId)
        {
            if (!_byChannel.TryGetValue(channelId, out var subscribers))
            {
                subscribers = new List<(uint clientId, uint subscriptionId)>();
                _byChannel[channelId] = subscribers;
            }

            subscribers.Add((clientId, subscriptionId));
        }

        private void RemoveReverseIndex(uint channelId, uint clientId, uint subscriptionId)
        {
            if (!_byChannel.TryGetValue(channelId, out var subscribers))
                return;

            for (var i = subscribers.Count - 1; i >= 0; i--)
            {
                var subscriber = subscribers[i];
                if (subscriber.clientId == clientId && subscriber.subscriptionId == subscriptionId)
                {
                    subscribers.RemoveAt(i);
                    break;
                }
            }

            if (subscribers.Count == 0)
                _byChannel.Remove(channelId);
        }
    }
}
