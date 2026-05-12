// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core
// Purpose: Maintains a publish/subscribe topology snapshot and per-client
// graph subscription state for the Foxglove ConnectionGraph capability.

using System.Collections.Generic;
using System.Linq;
using Unity.FoxgloveSDK.Protocol;

namespace Unity.FoxgloveSDK.Core
{
    /// <summary>
    /// Maintains a publish/subscribe topology snapshot and per-client graph subscription state.
    /// Used for ConnectionGraph capability (Phase 8).
    /// </summary>
    public class ConnectionGraphRegistry
    {
        /// <summary>Set of client IDs that are subscribed to graph updates.</summary>
        private readonly HashSet<uint> _graphSubscribers = new();
        /// <summary>Lock guarding all mutable state.</summary>
        private readonly object _lock = new();

        /// <summary>Map from topic name to set of publisher IDs.</summary>
        private readonly Dictionary<string, HashSet<string>> _publishedTopics = new();
        /// <summary>Map from topic name to set of subscriber IDs.</summary>
        private readonly Dictionary<string, HashSet<string>> _subscribedTopics = new();
        /// <summary>Map from service name to set of provider IDs.</summary>
        private readonly Dictionary<string, HashSet<string>> _advertisedServices = new();

        // ── Graph subscriber management ──

        /// <summary>Register a client for graph subscription updates.</summary>
        public void Subscribe(uint clientId)
        {
            lock (_lock) { _graphSubscribers.Add(clientId); }
        }

        /// <summary>Remove a client from graph subscription updates.</summary>
        public void Unsubscribe(uint clientId)
        {
            lock (_lock) { _graphSubscribers.Remove(clientId); }
        }

        /// <summary>Remove a client from graph subscription state (alias for Unsubscribe).</summary>
        public void RemoveClient(uint clientId)
        {
            lock (_lock) { _graphSubscribers.Remove(clientId); }
        }

        /// <summary>Get a snapshot of all graph subscriber client IDs.</summary>
        public IReadOnlyCollection<uint> GetSubscribers()
        {
            lock (_lock) { return _graphSubscribers.ToList(); }
        }

        // ── Topology updates ──

        /// <summary>Add a publisher to the given topic.</summary>
        public void AddPublishedTopic(string topic, string publisherId)
        {
            lock (_lock)
            {
                if (!_publishedTopics.ContainsKey(topic)) _publishedTopics[topic] = new();
                _publishedTopics[topic].Add(publisherId);
            }
        }

        /// <summary>Remove a publisher from the given topic. Removes the topic entry if empty.</summary>
        public void RemovePublishedTopic(string topic, string publisherId)
        {
            lock (_lock)
            {
                if (_publishedTopics.TryGetValue(topic, out var set))
                {
                    set.Remove(publisherId);
                    if (set.Count == 0) _publishedTopics.Remove(topic);
                }
            }
        }

        /// <summary>Replace all publishers for the given topic with a single publisher.</summary>
        public void SetPublishedTopic(string topic, string publisherId)
        {
            lock (_lock)
            {
                _publishedTopics[topic] = new HashSet<string> { publisherId };
            }
        }

        /// <summary>Add a subscriber to the given topic.</summary>
        public void AddSubscribedTopic(string topic, string subscriberId)
        {
            lock (_lock)
            {
                if (!_subscribedTopics.ContainsKey(topic)) _subscribedTopics[topic] = new();
                _subscribedTopics[topic].Add(subscriberId);
            }
        }

        /// <summary>Remove a subscriber from the given topic. Removes the topic entry if empty.</summary>
        public void RemoveSubscribedTopic(string topic, string subscriberId)
        {
            lock (_lock)
            {
                if (_subscribedTopics.TryGetValue(topic, out var set))
                {
                    set.Remove(subscriberId);
                    if (set.Count == 0) _subscribedTopics.Remove(topic);
                }
            }
        }

        /// <summary>Add a service provider for the given service name.</summary>
        public void AddAdvertisedService(string name, string providerId)
        {
            lock (_lock)
            {
                if (!_advertisedServices.ContainsKey(name)) _advertisedServices[name] = new();
                _advertisedServices[name].Add(providerId);
            }
        }

        /// <summary>Remove a service provider for the given service name. Removes the entry if empty.</summary>
        public void RemoveAdvertisedService(string name, string providerId)
        {
            lock (_lock)
            {
                if (_advertisedServices.TryGetValue(name, out var set))
                {
                    set.Remove(providerId);
                    if (set.Count == 0) _advertisedServices.Remove(name);
                }
            }
        }

        /// <summary>Build a ConnectionGraphUpdate snapshot of the full topology.</summary>
        public ConnectionGraphUpdate GetSnapshot()
        {
            lock (_lock)
            {
                return new ConnectionGraphUpdate
                {
                    PublishedTopics = _publishedTopics.Select(kv => new PublishedTopic
                    { Name = kv.Key, PublisherIds = kv.Value.ToList() }).ToList(),
                    SubscribedTopics = _subscribedTopics.Select(kv => new SubscribedTopic
                    { Name = kv.Key, SubscriberIds = kv.Value.ToList() }).ToList(),
                    AdvertisedServices = _advertisedServices.Select(kv => new AdvertisedService
                    { Name = kv.Key, ProviderIds = kv.Value.ToList() }).ToList()
                };
            }
        }
    }
}
