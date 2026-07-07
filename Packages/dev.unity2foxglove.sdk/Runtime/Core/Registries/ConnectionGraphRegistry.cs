// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core/Registries
// Purpose: Maintains a publish/subscribe topology snapshot and per-client
// graph subscription state for the Foxglove ConnectionGraph capability.

using System;
using System.Collections.Generic;
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

        /// <summary>Register a client and return the topology snapshot from the same lock epoch.</summary>
        public ConnectionGraphUpdate SubscribeAndGetSnapshot(uint clientId)
        {
            lock (_lock)
            {
                _graphSubscribers.Add(clientId);
                return BuildSnapshotLocked();
            }
        }

        /// <summary>Remove a client from graph subscription updates.</summary>
        public void Unsubscribe(uint clientId)
            => RemoveClient(clientId);

        /// <summary>Remove a disconnected client from graph subscription state.</summary>
        public void RemoveClient(uint clientId)
        {
            lock (_lock) { _graphSubscribers.Remove(clientId); }
        }

        /// <summary>Get a snapshot of all graph subscriber client IDs.</summary>
        public IReadOnlyCollection<uint> GetSubscribers()
        {
            lock (_lock)
            {
                if (_graphSubscribers.Count == 0)
                    return Array.Empty<uint>();

                var result = new List<uint>(_graphSubscribers.Count);
                foreach (var clientId in _graphSubscribers)
                    result.Add(clientId);
                return result;
            }
        }

        /// <summary>Copy all graph subscriber client IDs into a caller-owned list.</summary>
        public void CopySubscribersTo(List<uint> destination)
        {
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));

            lock (_lock)
            {
                destination.Clear();
                foreach (var clientId in _graphSubscribers)
                    destination.Add(clientId);
            }
        }

        /// <summary>Clear graph subscribers and all topology state.</summary>
        public void Clear()
        {
            lock (_lock)
            {
                _graphSubscribers.Clear();
                _publishedTopics.Clear();
                _subscribedTopics.Clear();
                _advertisedServices.Clear();
            }
        }

        // ── Topology updates ──

        /// <summary>Add a publisher to the given topic.</summary>
        public void AddPublishedTopic(string topic, string publisherId)
        {
            RequireTopic(topic, nameof(topic));
            RequireIdentifier(publisherId, nameof(publisherId));

            lock (_lock)
            {
                if (!_publishedTopics.ContainsKey(topic)) _publishedTopics[topic] = new();
                _publishedTopics[topic].Add(publisherId);
            }
        }

        /// <summary>Remove a publisher from the given topic. Removes the topic entry if empty.</summary>
        public void RemovePublishedTopic(string topic, string publisherId)
        {
            RequireTopic(topic, nameof(topic));
            RequireIdentifier(publisherId, nameof(publisherId));

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
            RequireTopic(topic, nameof(topic));
            RequireIdentifier(publisherId, nameof(publisherId));

            lock (_lock)
            {
                _publishedTopics[topic] = new HashSet<string> { publisherId };
            }
        }

        /// <summary>Add a subscriber to the given topic.</summary>
        public void AddSubscribedTopic(string topic, string subscriberId)
        {
            RequireTopic(topic, nameof(topic));
            RequireIdentifier(subscriberId, nameof(subscriberId));

            lock (_lock)
            {
                if (!_subscribedTopics.ContainsKey(topic)) _subscribedTopics[topic] = new();
                _subscribedTopics[topic].Add(subscriberId);
            }
        }

        /// <summary>Remove a subscriber from the given topic. Removes the topic entry if empty.</summary>
        public void RemoveSubscribedTopic(string topic, string subscriberId)
        {
            RequireTopic(topic, nameof(topic));
            RequireIdentifier(subscriberId, nameof(subscriberId));

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
            RequireTopic(name, nameof(name));
            RequireIdentifier(providerId, nameof(providerId));

            lock (_lock)
            {
                if (!_advertisedServices.ContainsKey(name)) _advertisedServices[name] = new();
                _advertisedServices[name].Add(providerId);
            }
        }

        /// <summary>Remove a service provider for the given service name. Removes the entry if empty.</summary>
        public void RemoveAdvertisedService(string name, string providerId)
        {
            RequireTopic(name, nameof(name));
            RequireIdentifier(providerId, nameof(providerId));

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
                return BuildSnapshotLocked();
            }
        }

        private ConnectionGraphUpdate BuildSnapshotLocked()
        {
            var snapshot = new ConnectionGraphUpdate
            {
                PublishedTopics = new List<PublishedTopic>(_publishedTopics.Count),
                SubscribedTopics = new List<SubscribedTopic>(_subscribedTopics.Count),
                AdvertisedServices = new List<AdvertisedService>(_advertisedServices.Count)
            };
            CopyTopologyPublished(_publishedTopics, snapshot.PublishedTopics);
            CopyTopologySubscribed(_subscribedTopics, snapshot.SubscribedTopics);
            CopyTopologyServices(_advertisedServices, snapshot.AdvertisedServices);
            return snapshot;
        }

        private static void CopyTopologyPublished(
            Dictionary<string, HashSet<string>> source,
            List<PublishedTopic> destination)
        {
            foreach (var kv in source)
            {
                destination.Add(new PublishedTopic
                {
                    Name = kv.Key,
                    PublisherIds = CopyIds(kv.Value)
                });
            }
        }

        private static void CopyTopologySubscribed(
            Dictionary<string, HashSet<string>> source,
            List<SubscribedTopic> destination)
        {
            foreach (var kv in source)
            {
                destination.Add(new SubscribedTopic
                {
                    Name = kv.Key,
                    SubscriberIds = CopyIds(kv.Value)
                });
            }
        }

        private static void CopyTopologyServices(
            Dictionary<string, HashSet<string>> source,
            List<AdvertisedService> destination)
        {
            foreach (var kv in source)
            {
                destination.Add(new AdvertisedService
                {
                    Name = kv.Key,
                    ProviderIds = CopyIds(kv.Value)
                });
            }
        }

        private static List<string> CopyIds(HashSet<string> ids)
        {
            var result = new List<string>(ids.Count);
            foreach (var id in ids)
                result.Add(id);
            return result;
        }

        private static void RequireTopic(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Topic or service name is required.", parameterName);
        }

        private static void RequireIdentifier(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Graph identifier is required.", parameterName);
        }
    }
}
