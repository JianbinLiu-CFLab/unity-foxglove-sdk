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

        /// <summary>Remove subscriptions by their IDs.</summary>
        public void RemoveSubscriptions(uint clientId, IEnumerable<uint> subscriptionIds)
        {
            lock (_lock)
            {
                if (_clients.TryGetValue(clientId, out var subs))
                {
                    foreach (var sid in subscriptionIds)
                        subs.Remove(sid);
                }
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

        /// <summary>Remove all subscriptions targeting a channel (e.g. on unadvertise).</summary>
        public void RemoveChannel(uint channelId)
        {
            lock (_lock)
            {
                foreach (var subs in _clients.Values)
                {
                    var toRemove = new List<uint>();
                    foreach (var (subId, chId) in subs)
                    {
                        if (chId == channelId)
                            toRemove.Add(subId);
                    }
                    foreach (var sid in toRemove)
                        subs.Remove(sid);
                }
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

        public int ClientCount
        {
            get { lock (_lock) { return _clients.Count; } }
        }
    }
}
