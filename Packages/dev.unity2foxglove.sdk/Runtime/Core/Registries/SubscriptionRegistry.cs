// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core/Registries
// Purpose: Tracks per-client subscription state. Maps clientId to
// (subscriptionId to channelId) for MessageData routing.

using System;
using System.Collections.Generic;

namespace Unity.FoxgloveSDK.Core
{
    /// <summary>
    /// Tracks per-client subscription state.
    /// Maps clientId -> (subscriptionId -> channelId).
    /// </summary>
    public class SubscriptionRegistry
    {
        internal const int MaxSubscriptionsPerClient = 1024;
        internal const int MaxTotalSubscriptions = 8192;

        private readonly Dictionary<uint, Dictionary<uint, uint>> _clients
            = new Dictionary<uint, Dictionary<uint, uint>>();

        private readonly Dictionary<uint, HashSet<(uint clientId, uint subscriptionId)>> _byChannel
            = new Dictionary<uint, HashSet<(uint clientId, uint subscriptionId)>>();

        private int _totalSubscriptionCount;
        private readonly object _lock = new object();

        /// <summary>Add a subscription for a client. Throws if subscription budgets reject it.</summary>
        public void AddSubscription(uint clientId, uint subscriptionId, uint channelId)
        {
            if (!TryAddSubscription(clientId, subscriptionId, channelId, out var error))
                throw new InvalidOperationException(error);
        }

        /// <summary>Try to add one subscription for a client without throwing on budget rejection.</summary>
        public bool TryAddSubscription(uint clientId, uint subscriptionId, uint channelId, out string error)
        {
            lock (_lock)
            {
                return TryAddSubscriptionLocked(clientId, subscriptionId, channelId, out _, out error);
            }
        }

        /// <summary>
        /// Try to apply a subscribe batch atomically. Over-budget batches are
        /// rejected without mutating client subscription or reverse-index state.
        /// </summary>

        public bool TryAddSubscriptions(
            uint clientId,
            IEnumerable<(uint subscriptionId, uint channelId)> subscriptions,
            out List<SubscriptionRegistryChange> changes,
            out string error)
        {
            lock (_lock)
            {
                changes = new List<SubscriptionRegistryChange>();
                error = null;

                var deduped = new Dictionary<uint, uint>();
                if (subscriptions != null)
                {
                    foreach (var (subscriptionId, channelId) in subscriptions)
                        deduped[subscriptionId] = channelId;
                }

                if (!_clients.TryGetValue(clientId, out var subs))
                    subs = null;

                var currentClientCount = subs?.Count ?? 0;
                var newUniqueCount = 0;
                foreach (var subscriptionId in deduped.Keys)
                {
                    if (subs == null || !subs.ContainsKey(subscriptionId))
                        newUniqueCount++;
                }

                var resultingCount = currentClientCount + newUniqueCount;
                if (resultingCount > MaxSubscriptionsPerClient)
                {
                    error = $"Too many subscriptions for client {clientId}";
                    return false;
                }

                var totalAfter = TotalSubscriptionCountLocked() - currentClientCount + resultingCount;
                if (totalAfter > MaxTotalSubscriptions)
                {
                    error = "Too many total subscriptions";
                    return false;
                }

                if (deduped.Count == 0)
                    return true;

                if (subs == null)
                {
                    subs = new Dictionary<uint, uint>();
                    _clients[clientId] = subs;
                }

                foreach (var (subscriptionId, channelId) in deduped)
                {
                    var hadPrevious = subs.TryGetValue(subscriptionId, out var previousChannelId);
                    if (hadPrevious)
                        RemoveReverseIndex(previousChannelId, clientId, subscriptionId);

                    subs[subscriptionId] = channelId;
                    AddReverseIndex(channelId, clientId, subscriptionId);
                    changes.Add(new SubscriptionRegistryChange(
                        subscriptionId,
                        channelId,
                        hadPrevious,
                        hadPrevious ? previousChannelId : 0));
                }

                return true;
            }
        }

        private bool TryAddSubscriptionLocked(
            uint clientId,
            uint subscriptionId,
            uint channelId,
            out SubscriptionRegistryChange change,
            out string error)
        {
            change = default;
            error = null;

            if (!_clients.TryGetValue(clientId, out var subs))
                subs = null;

            var currentClientCount = subs?.Count ?? 0;
            var isNewSubscription = subs == null || !subs.ContainsKey(subscriptionId);
            if (currentClientCount + (isNewSubscription ? 1 : 0) > MaxSubscriptionsPerClient)
            {
                error = $"Too many subscriptions for client {clientId}";
                return false;
            }

            if (isNewSubscription && TotalSubscriptionCountLocked() + 1 > MaxTotalSubscriptions)
            {
                error = "Too many total subscriptions";
                return false;
            }

            if (subs == null)
            {
                subs = new Dictionary<uint, uint>();
                _clients[clientId] = subs;
            }

            var hadPrevious = subs.TryGetValue(subscriptionId, out var previousChannelId);
            if (hadPrevious)
                RemoveReverseIndex(previousChannelId, clientId, subscriptionId);

            subs[subscriptionId] = channelId;
            AddReverseIndex(channelId, clientId, subscriptionId);
            change = new SubscriptionRegistryChange(
                subscriptionId,
                channelId,
                hadPrevious,
                hadPrevious ? previousChannelId : 0);
            return true;
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

                    if (subs.Count == 0)
                        _clients.Remove(clientId);
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

                var subscriberSnapshot = new List<(uint clientId, uint subscriptionId)>(subscribers);
                foreach (var (clientId, subId) in subscriberSnapshot)
                {
                    if (_clients.TryGetValue(clientId, out var subs)
                        && subs.TryGetValue(subId, out var chId)
                        && chId == channelId)
                    {
                        subs.Remove(subId);
                        RemoveReverseIndex(chId, clientId, subId);
                        removed.Add((clientId, subId, chId));
                    }
                }

                _byChannel.Remove(channelId);
                RecalculateTotalSubscriptionCountLocked();

                // Check every client named by the reverse index, including
                // stale entries that did not produce a forward-map removal.
                foreach (var (clientId, _) in subscriberSnapshot)
                {
                    if (_clients.TryGetValue(clientId, out var clientSubs) && clientSubs.Count == 0)
                        _clients.Remove(clientId);
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
                {
                    if (subscribers.Count == 0)
                        return;

                    foreach (var subscriber in subscribers)
                        destination.Add(subscriber);
                }
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
                _totalSubscriptionCount = 0;
            }
        }

        /// <summary>Total number of clients with active subscriptions.</summary>
        public int ClientCount
        {
            get { lock (_lock) { return _clients.Count; } }
        }

        private int TotalSubscriptionCountLocked() => _totalSubscriptionCount;

        private void RecalculateTotalSubscriptionCountLocked()
        {
            var count = 0;
            foreach (var subscriptions in _clients.Values)
                count += subscriptions.Count;
            _totalSubscriptionCount = count;
        }

        private void AddReverseIndex(uint channelId, uint clientId, uint subscriptionId)
        {
            if (!_byChannel.TryGetValue(channelId, out var subscribers))
            {
                subscribers = new HashSet<(uint clientId, uint subscriptionId)>();
                _byChannel[channelId] = subscribers;
            }

            if (subscribers.Add((clientId, subscriptionId)))
                _totalSubscriptionCount++;
        }

        private void RemoveReverseIndex(uint channelId, uint clientId, uint subscriptionId)
        {
            if (!_byChannel.TryGetValue(channelId, out var subscribers))
                return;

            if (subscribers.Remove((clientId, subscriptionId)))
                _totalSubscriptionCount--;

            if (subscribers.Count == 0)
                _byChannel.Remove(channelId);
        }
    }
    public readonly struct SubscriptionRegistryChange
    {

        public SubscriptionRegistryChange(
            uint subscriptionId,
            uint channelId,
            bool hadPrevious,
            uint previousChannelId)
        {
            SubscriptionId = subscriptionId;
            ChannelId = channelId;
            HadPrevious = hadPrevious;
            PreviousChannelId = previousChannelId;
        }

        public uint SubscriptionId { get; }

        public uint ChannelId { get; }

        public bool HadPrevious { get; }

        public uint PreviousChannelId { get; }
    }
}
