using System.Collections.Generic;

namespace Unity.FoxgloveSDK.Core
{
    /// <summary>
    /// Tracks per-client parameter subscriptions.
    /// Empty parameterNames = subscribe to all.
    /// </summary>
    public class ParameterSubscriptionRegistry
    {
        // clientId → set of subscribed parameter names (null means "all")
        private readonly Dictionary<uint, HashSet<string>> _clients = new();
        private readonly object _lock = new();

        /// <summary>Subscribe a client. null or empty parameterNames subscribes to all.</summary>
        public void Subscribe(uint clientId, IEnumerable<string> parameterNames)
        {
            lock (_lock)
            {
                if (!_clients.TryGetValue(clientId, out var subs))
                {
                    subs = new HashSet<string>();
                    _clients[clientId] = subs;
                }

                if (parameterNames == null)
                {
                    // Empty list = "all": stored as null
                    _clients[clientId] = null;
                }
                else
                {
                    // If currently "all", switch to explicit set
                    if (subs == null)
                    {
                        subs = new HashSet<string>();
                        _clients[clientId] = subs;
                    }
                    foreach (var n in parameterNames)
                        subs.Add(n);
                }
            }
        }

        /// <summary>Unsubscribe. null or empty parameterNames clears all subscriptions.</summary>
        public void Unsubscribe(uint clientId, IEnumerable<string> parameterNames)
        {
            lock (_lock)
            {
                if (parameterNames == null || !_clients.TryGetValue(clientId, out var subs))
                {
                    _clients.Remove(clientId);
                    return;
                }

                foreach (var n in parameterNames)
                    subs?.Remove(n);
            }
        }

        /// <summary>Check if a client is subscribed to a given parameter name.</summary>
        public bool IsSubscribed(uint clientId, string parameterName)
        {
            lock (_lock)
            {
                if (!_clients.TryGetValue(clientId, out var subs)) return false;
                // null = subscribed to all
                if (subs == null) return true;
                return subs.Contains(parameterName);
            }
        }

        /// <summary>Remove all subscriptions for a client.</summary>
        public void RemoveClient(uint clientId)
        {
            lock (_lock) { _clients.Remove(clientId); }
        }

        public void Clear()
        {
            lock (_lock) { _clients.Clear(); }
        }
    }
}
