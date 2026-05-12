// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core
// Purpose: Tracks per-client parameter subscriptions. Null means "all";
// empty list is also treated as "all". Used for ParametersSubscribe push.

using System.Collections.Generic;
using System.Linq;

namespace Unity.FoxgloveSDK.Core
{
    /// <summary>
    /// Tracks per-client parameter subscriptions.
    /// Null means "all"; empty list also treated as "all".
    /// </summary>
    public class ParameterSubscriptionRegistry
    {
        // clientId → set of subscribed parameter names (null means "all")
        private readonly Dictionary<uint, HashSet<string>> _clients = new();
        private readonly object _lock = new();

        /// <summary>Subscribe. null or empty parameterNames subscribes to all.</summary>
        public void Subscribe(uint clientId, IEnumerable<string> parameterNames)
        {
            lock (_lock)
            {
                var names = parameterNames?.ToList();
                if (names == null || names.Count == 0)
                {
                    _clients[clientId] = null; // "all"
                    return;
                }

                if (!_clients.TryGetValue(clientId, out var subs) || subs == null)
                    subs = new HashSet<string>();

                foreach (var n in names)
                    subs.Add(n);

                _clients[clientId] = subs;
            }
        }

        /// <summary>Unsubscribe. null or empty parameterNames clears all.</summary>
        public void Unsubscribe(uint clientId, IEnumerable<string> parameterNames)
        {
            lock (_lock)
            {
                var names = parameterNames?.ToList();
                if (names == null || names.Count == 0)
                {
                    _clients.Remove(clientId);
                    return;
                }

                if (_clients.TryGetValue(clientId, out var subs) && subs != null)
                {
                    foreach (var n in names)
                        subs.Remove(n);
                }
            }
        }

        /// <summary>Get all client IDs that have any active subscription.</summary>
        public List<uint> GetSubscribedClientIds()
        {
            lock (_lock) { return _clients.Keys.ToList(); }
        }

        /// <summary>Check if a client is subscribed to a given parameter name.</summary>
        public bool IsSubscribed(uint clientId, string parameterName)
        {
            lock (_lock)
            {
                if (!_clients.TryGetValue(clientId, out var subs)) return false;
                if (subs == null) return true; // "all"
                return subs.Contains(parameterName);
            }
        }

        /// <summary>Remove all subscriptions for a client.</summary>
        public void RemoveClient(uint clientId)
        {
            lock (_lock) { _clients.Remove(clientId); }
        }

        /// <summary>Remove all client subscriptions.</summary>
        public void Clear()
        {
            lock (_lock) { _clients.Clear(); }
        }
    }
}
