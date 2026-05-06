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
        // Per-client graph subscription state
        private readonly HashSet<uint> _graphSubscribers = new();
        private readonly object _lock = new();

        // Topic → publisher IDs (string)
        private readonly Dictionary<string, HashSet<string>> _publishedTopics = new();
        // Topic → subscriber IDs
        private readonly Dictionary<string, HashSet<string>> _subscribedTopics = new();
        // Service name → provider IDs
        private readonly Dictionary<string, HashSet<string>> _advertisedServices = new();

        // ── Graph subscriber management ──

        public void Subscribe(uint clientId)
        {
            lock (_lock) { _graphSubscribers.Add(clientId); }
        }

        public void Unsubscribe(uint clientId)
        {
            lock (_lock) { _graphSubscribers.Remove(clientId); }
        }

        public void RemoveClient(uint clientId)
        {
            lock (_lock) { _graphSubscribers.Remove(clientId); }
        }

        public IReadOnlyCollection<uint> GetSubscribers()
        {
            lock (_lock) { return _graphSubscribers.ToList(); }
        }

        // ── Topology updates ──

        public void AddPublishedTopic(string topic, string publisherId)
        {
            lock (_lock)
            {
                if (!_publishedTopics.ContainsKey(topic)) _publishedTopics[topic] = new();
                _publishedTopics[topic].Add(publisherId);
            }
        }

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

        public void SetPublishedTopic(string topic, string publisherId)
        {
            lock (_lock)
            {
                _publishedTopics[topic] = new HashSet<string> { publisherId };
            }
        }

        public void AddSubscribedTopic(string topic, string subscriberId)
        {
            lock (_lock)
            {
                if (!_subscribedTopics.ContainsKey(topic)) _subscribedTopics[topic] = new();
                _subscribedTopics[topic].Add(subscriberId);
            }
        }

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

        public void AddAdvertisedService(string name, string providerId)
        {
            lock (_lock)
            {
                if (!_advertisedServices.ContainsKey(name)) _advertisedServices[name] = new();
                _advertisedServices[name].Add(providerId);
            }
        }

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
