// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core/Session
// Purpose: Connection graph ownership and broadcasting for FoxgloveSession.

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Core
{
    /// <summary>Owns connection graph topology, subscribers, broadcasts, and MCAP graph metadata.</summary>
    internal sealed class SessionGraphHandler
    {
        private const string UnityPublisherId = "unity";
        private const string GraphMetadataName = "foxglove.connection_graph";

        private readonly IFoxgloveTransport _transport;
        private readonly IFoxgloveLogger _logger;
        private readonly Func<McapRecorder> _recorderProvider;
        private readonly ConnectionGraphRegistry _graph = new();
        private bool _dirty;

        public SessionGraphHandler(
            IFoxgloveTransport transport,
            IFoxgloveLogger logger,
            Func<McapRecorder> recorderProvider)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _logger = logger ?? new ConsoleLogger();
            _recorderProvider = recorderProvider ?? (() => null);
        }

        public void Clear()
        {
            _graph.Clear();
            _dirty = false;
        }

        public void Subscribe(uint clientId)
        {
            _graph.Subscribe(clientId);
            _transport.SendText(clientId, JsonConvert.SerializeObject(_graph.GetSnapshot()));
        }

        public void Unsubscribe(uint clientId)
        {
            _graph.Unsubscribe(clientId);
        }

        public void SetUnityPublishedTopic(string topic)
        {
            _graph.SetPublishedTopic(topic, UnityPublisherId);
            _dirty = true;
        }

        public void RemoveUnityPublishedTopic(string topic)
        {
            _graph.RemovePublishedTopic(topic, UnityPublisherId);
            _dirty = true;
        }

        public void AddClientPublishedTopic(uint clientId, uint channelId, string topic)
        {
            _graph.AddPublishedTopic(topic, ClientChannelId(clientId, channelId));
            _dirty = true;
        }

        public void RemoveClientPublishedTopic(uint clientId, uint channelId, string topic)
        {
            _graph.RemovePublishedTopic(topic, ClientChannelId(clientId, channelId));
            _dirty = true;
        }

        public void AddSubscribedTopic(uint clientId, uint subscriptionId, string topic)
        {
            _graph.AddSubscribedTopic(topic, ClientSubscriptionId(clientId, subscriptionId));
            _dirty = true;
        }

        public void RemoveSubscribedTopic(uint clientId, uint subscriptionId, string topic)
        {
            _graph.RemoveSubscribedTopic(topic, ClientSubscriptionId(clientId, subscriptionId));
            _dirty = true;
        }

        public void AddAdvertisedService(string name)
        {
            _graph.AddAdvertisedService(name, UnityPublisherId);
            _dirty = true;
        }

        public void RemoveAdvertisedService(string name)
        {
            _graph.RemoveAdvertisedService(name, UnityPublisherId);
            _dirty = true;
        }

        public void SeedUnityState(IReadOnlyCollection<AdvertiseChannel> channels, IReadOnlyCollection<ServiceDescriptor> services)
        {
            if (channels != null)
            {
                foreach (var ch in channels)
                    _graph.SetPublishedTopic(ch.Topic, UnityPublisherId);
            }

            if (services != null)
            {
                foreach (var svc in services)
                    _graph.AddAdvertisedService(svc.Name, UnityPublisherId);
            }

            _dirty = true;
        }

        public void RemoveClient(uint clientId)
        {
            _graph.RemoveClient(clientId);
            _dirty = true;
        }

        public void BroadcastUpdate()
        {
            var json = JsonConvert.SerializeObject(_graph.GetSnapshot());
            foreach (var subId in _graph.GetSubscribers())
                _transport.SendText(subId, json);

            FlushMetadataSnapshotIfDirty(json);
        }

        private void FlushMetadataSnapshotIfDirty(string json)
        {
            if (!_dirty)
                return;

            var recorder = _recorderProvider();
            recorder?.WriteMetadata(GraphMetadataName, json);
            _dirty = false;
        }

        private static string ClientChannelId(uint clientId, uint channelId) => $"client:{clientId}:{channelId}";

        private static string ClientSubscriptionId(uint clientId, uint subscriptionId) =>
            $"client:{clientId}:{subscriptionId}";
    }
}
