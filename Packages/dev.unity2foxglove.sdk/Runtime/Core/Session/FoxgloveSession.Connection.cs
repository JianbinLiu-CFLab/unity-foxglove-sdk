// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core/Session
// Purpose: FoxgloveSession partial — subscribe/unsubscribe dispatch,
// ConnectionGraph, ClientPublish, PlaybackControl, and Assets/fetchAsset.

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Unity.FoxgloveSDK.Protocol;

namespace Unity.FoxgloveSDK.Core
{
    public partial class FoxgloveSession
    {
        // ── Subscribe/unsubscribe ──

        /// <summary>
        /// Parse a subscribe message and add per-client subscription entries.
        /// Each subscription maps a client-supplied subscriptionId to a channelId.
        /// </summary>
        private void HandleSubscribe(uint clientId, string json)
        {
            try
            {
                var msg = JsonConvert.DeserializeObject<SubscribeMessage>(json);
                if (msg?.Subscriptions != null)
                {
                    var requested = new List<(uint subscriptionId, uint channelId)>();
                    foreach (var sub in msg.Subscriptions)
                    {
                        var ch = _channels.Get(sub.ChannelId);
                        if (ch != null)
                            requested.Add((sub.Id, sub.ChannelId));
                    }

                    if (!_subscriptions.TryAddSubscriptions(clientId, requested, out var changes, out var error))
                    {
                        WarnSubscriptionBudgetRejected(clientId, error);
                        return;
                    }

                    foreach (var change in changes)
                    {
                        if (change.HadPrevious && change.PreviousChannelId != change.ChannelId)
                        {
                            var previous = _channels.Get(change.PreviousChannelId);
                            if (previous != null)
                                _graph.RemoveSubscribedTopic(clientId, change.SubscriptionId, previous.Topic);
                        }

                        var ch = _channels.Get(change.ChannelId);
                        if (ch != null)
                            _graph.AddSubscribedTopic(clientId, change.SubscriptionId, ch.Topic);
                    }
                }
                _graph.BroadcastUpdate();
            }
            catch (Exception ex) { _logger.LogWarning($"subscribe error: {ex.Message}"); }
        }

        private void WarnSubscriptionBudgetRejected(uint clientId, string error)
        {
            if (_subscriptionBudgetWarnedClients.Add(clientId))
                _logger.LogWarning(
                    $"subscribe batch rejected atomically from client {clientId}; no subscriptions from this batch were applied: {error}");
        }

        /// <summary>
        /// Parse an unsubscribe message and remove per-client subscriptions
        /// by subscriptionId, cleaning up the connection graph.
        /// </summary>
        private void HandleUnsubscribe(uint clientId, string json)
        {
            try
            {
                var msg = JsonConvert.DeserializeObject<UnsubscribeMessage>(json);
                if (msg.SubscriptionIds != null)
                {
                    var removed = _subscriptions.RemoveSubscriptions(clientId, msg.SubscriptionIds);
                    foreach (var (subId, chId) in removed)
                    {
                        var ch = _channels.Get(chId);
                        if (ch != null)
                            _graph.RemoveSubscribedTopic(clientId, subId, ch.Topic);
                    }
                }
                _graph.BroadcastUpdate();
            }
            catch (Exception ex) { _logger.LogWarning($"unsubscribe error: {ex.Message}"); }
        }

        // ── ConnectionGraph ──

        /// <summary>
        /// Subscribe a client to connection graph updates and send the current
        /// snapshot immediately so the client's topology view is seeded.
        /// </summary>
        private void HandleSubscribeConnectionGraph(uint clientId)
        {
            _graph.Subscribe(clientId);
        }

        /// <summary>Unsubscribe a client from connection graph updates.</summary>
        private void HandleUnsubscribeConnectionGraph(uint clientId)
        {
            _graph.Unsubscribe(clientId);
        }

        // ── ClientPublish ──

        private void HandleClientAdvertise(uint clientId, string json)
            => _clientPublish.Advertise(clientId, json);

        private void HandleClientUnadvertise(uint clientId, string json)
            => _clientPublish.Unadvertise(clientId, json);

        /// <summary>
        /// Decode a ClientPublish binary frame and dispatch to
        /// <see cref="FoxgloveSession.OnClientMessage"/> if the client has
        /// advertised the matching channel.
        /// </summary>
        private void HandleClientBinaryPublish(uint clientId, byte[] data)
            => _clientPublish.RouteBinary(clientId, data);

        // ── PlaybackControl ──

        /// <summary>
        /// Decode a playback control command and queue it for the runtime
        /// owner thread. The WebSocket receive loop runs on a transport
        /// thread, while replay cursor and clock updates are drained from
        /// Unity's main-thread tick.
        /// </summary>
        private bool HandlePlaybackControlRequest(uint clientId, byte[] data) =>
            _playback.HandleRequest(clientId, data);

        /// <summary>
        /// Apply queued playback control requests on the runtime owner thread.
        /// </summary>
        internal void DrainPlaybackControls() => _playback.Drain();

        // ── Assets ──

        /// <summary>
        /// Handle a fetchAsset request: parse the URI, look up the asset root,
        /// read the file, and respond with a binary fetchAssetResponse frame
        /// (success or error).
        /// </summary>
        private void HandleFetchAsset(uint clientId, string json)
            => _assets.Fetch(clientId, json);
    }
}
