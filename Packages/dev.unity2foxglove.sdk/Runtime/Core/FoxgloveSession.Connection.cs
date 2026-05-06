// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core
// Purpose: FoxgloveSession partial — subscribe/unsubscribe dispatch,
// ConnectionGraph, ClientPublish, PlaybackControl, and Assets/fetchAsset.

using System;
using System.Collections.Generic;
using System.Linq;
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
                foreach (var sub in msg.Subscriptions)
                {
                    var ch = _channels.Get(sub.ChannelId);
                    if (ch != null)
                    {
                        _subscriptions.AddSubscription(clientId, sub.Id, sub.ChannelId);
                        _graph.AddSubscribedTopic(ch.Topic, $"client:{clientId}:{sub.Id}");
                        _graphDirty = true;
                    }
                }
            }
            catch (Exception ex) { _logger.LogWarning($"subscribe parse error: {ex.Message}"); }
            BroadcastGraphUpdate();
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
                        {
                            _graph.RemoveSubscribedTopic(ch.Topic, $"client:{clientId}:{subId}");
                            _graphDirty = true;
                        }
                    }
                }
            }
            catch (Exception ex) { _logger.LogWarning($"unsubscribe parse error: {ex.Message}"); }
            BroadcastGraphUpdate();
        }

        // ── ConnectionGraph ──

        /// <summary>
        /// Subscribe a client to connection graph updates and send the current
        /// snapshot immediately so the client's topology view is seeded.
        /// </summary>
        private void HandleSubscribeConnectionGraph(uint clientId)
        {
            _graph.Subscribe(clientId);
            var snapshot = _graph.GetSnapshot();
            _transport.SendText(clientId, JsonConvert.SerializeObject(snapshot));
        }

        private void HandleUnsubscribeConnectionGraph(uint clientId)
        {
            _graph.Unsubscribe(clientId);
        }

        private bool _graphDirty;

        /// <summary>
        /// Broadcast the current connection graph snapshot to all subscribed
        /// clients. When dirty, also writes the snapshot as MCAP metadata so
        /// the graph state is preserved in recordings.
        /// </summary>
        private void BroadcastGraphUpdate()
        {
            var json = JsonConvert.SerializeObject(_graph.GetSnapshot());
            foreach (var subId in _graph.GetSubscribers())
                _transport.SendText(subId, json);
            if (_graphDirty)
            {
                _recorder?.WriteMetadata("foxglove.connection_graph", json);
                _graphDirty = false;
            }
        }

        // ── ClientPublish ──

        private void HandleClientAdvertise(uint clientId, string json)
        {
            try
            {
                var msg = JsonConvert.DeserializeObject<Advertise>(json);
                foreach (var ch in msg.Channels ?? new List<AdvertiseChannel>())
                {
                    _clientChannels[(clientId, ch.Id)] = ch;
                    _graph.AddPublishedTopic(ch.Topic, $"client:{clientId}:{ch.Id}");
                    _graphDirty = true;
                }
                BroadcastGraphUpdate();
            }
            catch (Exception ex) { _logger.LogWarning($"Client advertise parse error from client {clientId}: {ex.Message}"); }
        }

        private void HandleClientUnadvertise(uint clientId, string json)
        {
            try
            {
                var msg = JsonConvert.DeserializeObject<Unadvertise>(json);
                foreach (var chId in msg.ChannelIds ?? new List<uint>())
                {
                    if (_clientChannels.TryGetValue((clientId, chId), out var ch))
                    {
                        _graph.RemovePublishedTopic(ch.Topic, $"client:{clientId}:{chId}");
                        _graphDirty = true;
                        _clientChannels.Remove((clientId, chId));
                    }
                }
                BroadcastGraphUpdate();
            }
            catch (Exception ex) { _logger.LogWarning($"Client unadvertise parse error from client {clientId}: {ex.Message}"); }
        }

        /// <summary>
        /// Decode a ClientPublish binary frame and dispatch to
        /// <see cref="FoxgloveSession.OnClientMessage"/> if the client has
        /// advertised the matching channel.
        /// </summary>
        private void HandleClientBinaryPublish(uint clientId, byte[] data)
        {
            if (BinaryEncoding.TryDecodeClientMessageData(data, out var chId, out var payload))
            {
                if (_clientChannels.TryGetValue((clientId, chId), out var ch))
                {
                    OnClientMessage?.Invoke(clientId, chId, ch.Topic, payload);
                    _recorder?.WriteClientMessage(clientId, chId, _clock.NowNs, payload, ch.Topic);
                }
            }
        }

        // ── PlaybackControl ──

        /// <summary>
        /// Decode and apply a playback control command (play, pause, seek).
        /// Responds with a PlaybackState binary frame. Returns true if the
        /// frame was recognized as a playback control request.
        /// </summary>
        private bool HandlePlaybackControlRequest(uint clientId, byte[] data)
        {
            if (_runtime?.PlaybackEnabled != true) return false;
            if (!BinaryEncoding.TryDecodePlaybackControlRequest(data, out var pbCmd, out var pbSpeed,
                    out var pbHasSeek, out var pbSeekNs, out var pbReqId))
                return false;

            _runtime.ApplyPlaybackCommand(pbCmd, pbSpeed, pbHasSeek, pbSeekNs);
            if (pbHasSeek) _runtime.ReplaySeek(pbSeekNs);
            if (pbCmd == 0) _runtime.ReplayPlay();
            else if (pbCmd == 1) _runtime.ReplayPause();
            var state = _runtime.GetPlaybackState(true, pbReqId);
            var pbFrame = BinaryEncoding.EncodePlaybackState(
                state.Status, state.CurrentTimeNs, state.Speed, state.DidSeek, state.RequestId);
            _transport.SendBinary(clientId, pbFrame);
            return true;
        }

        // ── Assets ──

        /// <summary>
        /// Handle a fetchAsset request: parse the URI, look up the asset root,
        /// read the file, and respond with a binary fetchAssetResponse frame
        /// (success or error).
        /// </summary>
        private void HandleFetchAsset(uint clientId, string json)
        {
            FetchAsset msg;
            try { msg = JsonConvert.DeserializeObject<FetchAsset>(json); }
            catch
            {
                _transport.SendBinary(clientId, BinaryEncoding.EncodeFetchAssetResponseError(0, "Malformed JSON"));
                return;
            }
            if (_runtime?.Assets == null || !_runtime.Assets.HasRoots)
            {
                _transport.SendBinary(clientId, BinaryEncoding.EncodeFetchAssetResponseError(msg.RequestId, "No asset roots registered"));
                return;
            }
            if (_runtime.Assets.TryRead(msg.Uri, out var data, out var error))
                _transport.SendBinary(clientId, BinaryEncoding.EncodeFetchAssetResponseSuccess(msg.RequestId, data));
            else
                _transport.SendBinary(clientId, BinaryEncoding.EncodeFetchAssetResponseError(msg.RequestId, error));
        }
    }
}
