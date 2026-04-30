using System.Collections.Generic;

namespace Unity.FoxgloveSDK.Core
{
    /// <summary>
    /// Tracks per-client subscription state.
    /// Maps clientId → (subscriptionId → channelId).
    /// </summary>
    public class SubscriptionRegistry
    {
        /// <summary>Per-client: subscriptionId → channelId.</summary>
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

        /// <summary>
        /// Get all (clientId, subscriptionId) pairs that are subscribed to a given channel.
        /// Used when publishing to route MessageData frames.
        /// </summary>
        public IEnumerable<(uint clientId, uint subscriptionId)> GetSubscribersForChannel(uint channelId)
        {
            lock (_lock)
            {
                foreach (var (clientId, subs) in _clients)
                {
                    foreach (var (subId, chId) in subs)
                    {
                        if (chId == channelId)
                            yield return (clientId, subId);
                    }
                }
            }
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
