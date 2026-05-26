// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core/Session
// Purpose: Connection graph ownership and broadcasting for FoxgloveSession.

using System;
using System.Collections.Generic;
using System.Threading;
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
        private int _dirty;

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
            Volatile.Write(ref _dirty, 0);
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
            MarkDirty();
        }

        public void RemoveUnityPublishedTopic(string topic)
        {
            _graph.RemovePublishedTopic(topic, UnityPublisherId);
            MarkDirty();
        }

        public void AddClientPublishedTopic(uint clientId, uint channelId, string topic)
        {
            _graph.AddPublishedTopic(topic, ClientChannelId(clientId, channelId));
            MarkDirty();
        }

        public void RemoveClientPublishedTopic(uint clientId, uint channelId, string topic)
        {
            _graph.RemovePublishedTopic(topic, ClientChannelId(clientId, channelId));
            MarkDirty();
        }

        public void AddSubscribedTopic(uint clientId, uint subscriptionId, string topic)
        {
            _graph.AddSubscribedTopic(topic, ClientSubscriptionId(clientId, subscriptionId));
            MarkDirty();
        }

        public void RemoveSubscribedTopic(uint clientId, uint subscriptionId, string topic)
        {
            _graph.RemoveSubscribedTopic(topic, ClientSubscriptionId(clientId, subscriptionId));
            MarkDirty();
        }

        public void AddAdvertisedService(string name)
        {
            _graph.AddAdvertisedService(name, UnityPublisherId);
            MarkDirty();
        }

        public void RemoveAdvertisedService(string name)
        {
            _graph.RemoveAdvertisedService(name, UnityPublisherId);
            MarkDirty();
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

            MarkDirty();
        }

        public void RemoveClient(uint clientId)
        {
            _graph.RemoveClient(clientId);
            MarkDirty();
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
            if (Interlocked.CompareExchange(ref _dirty, 0, 1) != 1)
                return;

            var recorder = _recorderProvider();
            if (recorder == null)
                return;

            try
            {
                recorder.WriteMetadata(GraphMetadataName, json);
            }
            catch (Exception ex) when (IsRecoverableMetadataException(ex))
            {
                Volatile.Write(ref _dirty, 1);
                _logger.LogWarning($"Connection graph metadata write failed; will retry after the next graph broadcast: {ex.Message}");
            }
        }

        private void MarkDirty() => Volatile.Write(ref _dirty, 1);

        private static bool IsRecoverableMetadataException(Exception ex)
        {
            return !(ex is OutOfMemoryException)
                   && !(ex is StackOverflowException)
                   && !(ex is AccessViolationException)
                   && !(ex is AppDomainUnloadedException);
        }

        private static string ClientChannelId(uint clientId, uint channelId) => $"client:{clientId}:{channelId}";

        private static string ClientSubscriptionId(uint clientId, uint subscriptionId) =>
            $"client:{clientId}:{subscriptionId}";
    }
}
