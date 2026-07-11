// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core/Session
// Purpose: Tracks client-advertised channels and routes client-published
// binary messages for FoxgloveSession.

using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Core
{
    /// <summary>
    /// Owns channels advertised by Foxglove clients and routes client-published payloads
    /// to recording, graph metadata, and the session callback.
    /// </summary>
    internal sealed class SessionClientPublishHandler
    {
        internal const int MaxClientPublishedChannelsPerClient = 256;
        internal const int MaxTotalClientPublishedChannels = 4096;
        internal const int MaxClientPublishedSchemaBytes = 1024 * 1024;

        private readonly Func<McapRecorder> _recorderProvider;
        private readonly IFoxgloveClock _clock;
        private readonly IFoxgloveLogger _logger;
        private readonly SessionGraphHandler _graph;
        private readonly Action<uint, uint, string, string, byte[]> _messageCallback;
        private readonly Dictionary<(uint clientId, uint chId), AdvertiseChannel> _clientChannels = new();
        private readonly HashSet<uint> _budgetWarnedClients = new();
        private readonly List<(uint clientId, uint chId)> _clientChannelRemovalScratch = new();
        private readonly object _clientChannelsLock = new();

        public SessionClientPublishHandler(
            Func<McapRecorder> recorderProvider,
            IFoxgloveClock clock,
            IFoxgloveLogger logger,
            SessionGraphHandler graph,
            Action<uint, uint, string, string, byte[]> messageCallback)
        {
            _recorderProvider = recorderProvider ?? (() => null);
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _logger = logger ?? new ConsoleLogger();
            _graph = graph ?? throw new ArgumentNullException(nameof(graph));
            _messageCallback = messageCallback ?? ((_, _, _, _, _) => { });
        }

        public void Clear()
        {
            lock (_clientChannelsLock)
            {
                _clientChannels.Clear();
                _budgetWarnedClients.Clear();
            }
        }

        public void RemoveClient(uint clientId)
        {
            var removedGraphTopics = new List<(uint channelId, string topic)>();
            lock (_clientChannelsLock)
            {
                _clientChannelRemovalScratch.Clear();
                foreach (var key in _clientChannels.Keys)
                {
                    if (key.clientId == clientId)
                        _clientChannelRemovalScratch.Add(key);
                }

                foreach (var k in _clientChannelRemovalScratch)
                {
                    if (_clientChannels.Remove(k, out var ch))
                        removedGraphTopics.Add((k.chId, ch.Topic));
                }

                _clientChannelRemovalScratch.Clear();
                _budgetWarnedClients.Remove(clientId);
            }

            foreach (var (channelId, topic) in removedGraphTopics)
                _graph.RemoveClientPublishedTopic(clientId, channelId, topic);
        }

        public void Advertise(uint clientId, string json)
        {
            try
            {
                var msg = JsonConvert.DeserializeObject<Advertise>(json);
                var channels = msg?.Channels ?? new List<AdvertiseChannel>();
                var deduped = new Dictionary<uint, AdvertiseChannel>();
                foreach (var ch in channels)
                {
                    if (ch == null
                        || string.IsNullOrWhiteSpace(ch.Topic)
                        || string.IsNullOrWhiteSpace(ch.Encoding))
                    {
                        _logger.LogWarning(
                            $"Client advertise batch rejected atomically from client {clientId}; no channels from this batch were applied: channel topic and encoding are required");
                        return;
                    }

                    if (SchemaByteCount(ch) > MaxClientPublishedSchemaBytes)
                    {
                        WarnBudgetRejected(clientId, "client-published channel schema exceeds the byte budget");
                        return;
                    }

                    deduped[ch.Id] = ch;
                }

                var staleGraphTopics = new List<(uint channelId, string topic)>();
                List<AdvertiseChannel> acceptedChannels = null;
                string budgetRejectionReason = null;
                bool shouldLogBudgetRejection = false;
                lock (_clientChannelsLock)
                {
                    var newChannelCount = 0;
                    foreach (var ch in deduped.Values)
                    {
                        if (!_clientChannels.ContainsKey((clientId, ch.Id)))
                            newChannelCount++;
                    }

                    var clientChannelCount = 0;
                    foreach (var key in _clientChannels.Keys)
                    {
                        if (key.clientId == clientId)
                            clientChannelCount++;
                    }

                    if (clientChannelCount + newChannelCount > MaxClientPublishedChannelsPerClient)
                    {
                        budgetRejectionReason = "client-published channel count exceeds the per-client budget";
                        shouldLogBudgetRejection = TryMarkBudgetRejectedUnderLock(clientId);
                    }
                    else if (_clientChannels.Count + newChannelCount > MaxTotalClientPublishedChannels)
                    {
                        budgetRejectionReason = "client-published channel count exceeds the total budget";
                        shouldLogBudgetRejection = TryMarkBudgetRejectedUnderLock(clientId);
                    }
                    else
                    {
                        acceptedChannels = new List<AdvertiseChannel>(deduped.Values);
                        foreach (var ch in acceptedChannels)
                        {
                            var key = (clientId, ch.Id);
                            if (_clientChannels.TryGetValue(key, out var previous)
                                && !string.Equals(previous.Topic, ch.Topic, StringComparison.Ordinal))
                            {
                                staleGraphTopics.Add((ch.Id, previous.Topic));
                            }

                            _clientChannels[key] = ch;
                        }
                    }
                }

                if (budgetRejectionReason != null)
                {
                    LogBudgetRejected(clientId, budgetRejectionReason, shouldLogBudgetRejection);
                    return;
                }

                foreach (var (channelId, topic) in staleGraphTopics)
                    _graph.RemoveClientPublishedTopic(clientId, channelId, topic);

                foreach (var ch in acceptedChannels)
                {
                    _graph.AddClientPublishedTopic(clientId, ch.Id, ch.Topic);
                }

                _graph.BroadcastUpdate();
            }
            catch (Exception ex) { _logger.LogWarning($"Client advertise parse error from client {clientId}: {ex.Message}"); }
        }

        private void WarnBudgetRejected(uint clientId, string reason)
        {
            bool shouldLog;
            lock (_clientChannelsLock)
            {
                shouldLog = TryMarkBudgetRejectedUnderLock(clientId);
            }

            LogBudgetRejected(clientId, reason, shouldLog);
        }

        private bool TryMarkBudgetRejectedUnderLock(uint clientId)
        {
            // Caller must hold _clientChannelsLock; the wrapper above covers outside-lock callers.
            return _budgetWarnedClients.Add(clientId);
        }

        private void LogBudgetRejected(uint clientId, string reason, bool shouldLog)
        {
            if (!shouldLog)
                return;

            _logger.LogWarning(
                $"Client advertise batch rejected atomically from client {clientId}; no channels from this batch were applied: {reason}");
        }

        private static int SchemaByteCount(AdvertiseChannel channel)
        {
            return string.IsNullOrEmpty(channel.Schema) ? 0 : Encoding.UTF8.GetByteCount(channel.Schema);
        }

        public void Unadvertise(uint clientId, string json)
        {
            try
            {
                var msg = JsonConvert.DeserializeObject<Unadvertise>(json);
                var graphChanged = false;
                foreach (var chId in msg?.ChannelIds ?? new List<uint>())
                {
                    AdvertiseChannel ch;
                    lock (_clientChannelsLock)
                    {
                        if (!_clientChannels.TryGetValue((clientId, chId), out ch))
                            continue;
                        _clientChannels.Remove((clientId, chId));
                    }

                    _graph.RemoveClientPublishedTopic(clientId, chId, ch.Topic);
                    graphChanged = true;
                }

                if (graphChanged)
                    _graph.BroadcastUpdate();
            }
            catch (Exception ex) { _logger.LogWarning($"Client unadvertise parse error from client {clientId}: {ex.Message}"); }
        }

        public void RouteBinary(uint clientId, uint chId, byte[] payload)
        {
            AdvertiseChannel ch;
            lock (_clientChannelsLock)
            {
                if (!_clientChannels.TryGetValue((clientId, chId), out ch))
                    return;
            }

            _messageCallback(clientId, chId, ch.Topic, ch.Encoding, payload);
            var recorder = _recorderProvider();
            recorder?.WriteClientMessage(clientId, chId, _clock.NowNs, payload,
                ch.Topic, ch.Encoding, ch.SchemaName, ch.SchemaEncoding, ch.Schema);
        }
    }
}
