// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core
// Purpose: Tracks client-advertised channels and routes client-published
// binary messages for FoxgloveSession.

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Core
{
    internal sealed class SessionClientPublishHandler
    {
        private readonly Func<McapRecorder> _recorderProvider;
        private readonly IFoxgloveClock _clock;
        private readonly IFoxgloveLogger _logger;
        private readonly SessionGraphHandler _graph;
        private readonly Action<uint, uint, string, byte[]> _messageCallback;
        private readonly Dictionary<(uint clientId, uint chId), AdvertiseChannel> _clientChannels = new();
        private readonly object _clientChannelsLock = new();

        public SessionClientPublishHandler(
            Func<McapRecorder> recorderProvider,
            IFoxgloveClock clock,
            IFoxgloveLogger logger,
            SessionGraphHandler graph,
            Action<uint, uint, string, byte[]> messageCallback)
        {
            _recorderProvider = recorderProvider ?? (() => null);
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _logger = logger ?? new ConsoleLogger();
            _graph = graph ?? throw new ArgumentNullException(nameof(graph));
            _messageCallback = messageCallback ?? ((_, _, _, _) => { });
        }

        public void Clear()
        {
            lock (_clientChannelsLock)
                _clientChannels.Clear();
        }

        public void RemoveClient(uint clientId)
        {
            lock (_clientChannelsLock)
            {
                var toRemove = _clientChannels.Keys.Where(k => k.clientId == clientId).ToList();
                foreach (var k in toRemove)
                {
                    if (_clientChannels.Remove(k, out var ch))
                        _graph.RemoveClientPublishedTopic(clientId, k.chId, ch.Topic);
                }
            }
        }

        public void Advertise(uint clientId, string json)
        {
            try
            {
                var msg = JsonConvert.DeserializeObject<Advertise>(json);
                var channels = msg?.Channels ?? new List<AdvertiseChannel>();
                if (channels.Any(ch => ch == null
                                       || string.IsNullOrWhiteSpace(ch.Topic)
                                       || string.IsNullOrWhiteSpace(ch.Encoding)))
                {
                    _logger.LogWarning($"Client advertise rejected from client {clientId}: channel topic and encoding are required");
                    return;
                }

                lock (_clientChannelsLock)
                {
                    foreach (var ch in channels)
                    {
                        _clientChannels[(clientId, ch.Id)] = ch;
                    }
                }

                foreach (var ch in channels)
                {
                    _graph.AddClientPublishedTopic(clientId, ch.Id, ch.Topic);
                }

                _graph.BroadcastUpdate();
            }
            catch (Exception ex) { _logger.LogWarning($"Client advertise parse error from client {clientId}: {ex.Message}"); }
        }

        public void Unadvertise(uint clientId, string json)
        {
            try
            {
                var msg = JsonConvert.DeserializeObject<Unadvertise>(json);
                foreach (var chId in msg.ChannelIds ?? new List<uint>())
                {
                    AdvertiseChannel ch;
                    lock (_clientChannelsLock)
                    {
                        if (!_clientChannels.TryGetValue((clientId, chId), out ch))
                            continue;
                        _clientChannels.Remove((clientId, chId));
                    }

                    _graph.RemoveClientPublishedTopic(clientId, chId, ch.Topic);
                }

                _graph.BroadcastUpdate();
            }
            catch (Exception ex) { _logger.LogWarning($"Client unadvertise parse error from client {clientId}: {ex.Message}"); }
        }

        public void RouteBinary(uint clientId, byte[] data)
        {
            if (!BinaryEncoding.TryDecodeClientMessageData(data, out var chId, out var payload))
                return;

            AdvertiseChannel ch;
            lock (_clientChannelsLock)
            {
                if (!_clientChannels.TryGetValue((clientId, chId), out ch))
                    return;
            }

            _messageCallback(clientId, chId, ch.Topic, payload);
            var recorder = _recorderProvider();
            recorder?.WriteClientMessage(clientId, chId, _clock.NowNs, payload,
                ch.Topic, ch.Encoding, ch.SchemaName, ch.SchemaEncoding, ch.Schema);
        }
    }
}
