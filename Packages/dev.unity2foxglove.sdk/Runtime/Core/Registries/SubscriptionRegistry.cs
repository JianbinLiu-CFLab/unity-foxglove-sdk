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
                subs[subscriptionId] = channelId;
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
                _clients.Remove(clientId);
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
                        result.Add((subId, chId));
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
                foreach (var (clientId, subs) in _clients)
                {
                    var toRemove = new List<uint>();
                    foreach (var (subId, chId) in subs)
                    {
                        if (chId == channelId)
                        {
                            toRemove.Add(subId);
                            removed.Add((clientId, subId, chId));
                        }
                    }
                    foreach (var sid in toRemove)
                        subs.Remove(sid);
                }
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
            lock (_lock)
            {
                foreach (var (clientId, subs) in _clients)
                {
                    foreach (var (subId, chId) in subs)
                    {
                        if (chId == channelId)
                            result.Add((clientId, subId));
                    }
                }
            }
            return result;
        }

        /// <summary>Remove all state.</summary>
        public void Clear()
        {
            lock (_lock)
            {
                _clients.Clear();
            }
        }

        /// <summary>Total number of clients with active subscriptions.</summary>
        public int ClientCount
        {
            get { lock (_lock) { return _clients.Count; } }
        }
    }
}
