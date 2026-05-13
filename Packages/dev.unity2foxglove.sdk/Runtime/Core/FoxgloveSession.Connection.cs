// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core
// Purpose: FoxgloveSession partial — subscribe/unsubscribe dispatch,
// ConnectionGraph, ClientPublish, PlaybackControl, and Assets/fetchAsset.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
                        _graph.AddSubscribedTopic(clientId, sub.Id, ch.Topic);
                    }
                }
            }
            catch (Exception ex) { _logger.LogWarning($"subscribe parse error: {ex.Message}"); }
            _graph.BroadcastUpdate();
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
            }
            catch (Exception ex) { _logger.LogWarning($"unsubscribe parse error: {ex.Message}"); }
            _graph.BroadcastUpdate();
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

        private void HandleClientUnadvertise(uint clientId, string json)
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

        /// <summary>
        /// Decode a ClientPublish binary frame and dispatch to
        /// <see cref="FoxgloveSession.OnClientMessage"/> if the client has
        /// advertised the matching channel.
        /// </summary>
        private void HandleClientBinaryPublish(uint clientId, byte[] data)
        {
            if (BinaryEncoding.TryDecodeClientMessageData(data, out var chId, out var payload))
            {
                AdvertiseChannel ch;
                lock (_clientChannelsLock)
                {
                    if (!_clientChannels.TryGetValue((clientId, chId), out ch))
                        return;
                }
                OnClientMessage?.Invoke(clientId, chId, ch.Topic, payload);
                var recorder = Volatile.Read(ref _recorder);
                recorder?.WriteClientMessage(clientId, chId, _clock.NowNs, payload,
                    ch.Topic, ch.Encoding, ch.SchemaName, ch.SchemaEncoding, ch.Schema);
            }
        }

        // ── PlaybackControl ──

        /// <summary>
        /// Decode a playback control command and queue it for the runtime
        /// owner thread. The WebSocket receive loop runs on a transport
        /// thread, while replay cursor and clock updates are drained from
        /// Unity's main-thread tick.
        /// </summary>
        private bool HandlePlaybackControlRequest(uint clientId, byte[] data)
        {
            var runtime = Volatile.Read(ref _runtime);
            if (runtime?.PlaybackEnabled != true) return false;
            if (!BinaryEncoding.TryDecodePlaybackControlRequest(data, out var playbackCommand, out var playbackSpeed,
                    out var playbackHasSeek, out var playbackSeekNs, out var playbackRequestId))
                return false;

            lock (_playbackControlsLock)
            {
                while (_pendingPlaybackControls.Count >= MaxPendingPlaybackControls)
                    _pendingPlaybackControls.Dequeue();
                _pendingPlaybackControls.Enqueue(new PendingPlaybackControl(
                    clientId, playbackCommand, playbackSpeed, playbackHasSeek, playbackSeekNs, playbackRequestId));
            }

            return true;
        }

        /// <summary>
        /// Apply queued playback control requests on the runtime owner thread.
        /// </summary>
        internal void DrainPlaybackControls()
        {
            while (true)
            {
                PendingPlaybackControl request;
                lock (_playbackControlsLock)
                {
                    if (_pendingPlaybackControls.Count == 0)
                        return;
                    request = _pendingPlaybackControls.Dequeue();
                }

                var runtime = Volatile.Read(ref _runtime);
                if (runtime?.PlaybackEnabled != true)
                    continue;

                if (request.HasSeek)
                    FoxgloveReplayTrace.ResetBudget();

                if (FoxgloveReplayTrace.TryEvent(
                    "CONTROL",
                    $"client={request.ClientId} command={request.Command} speed={request.Speed} hasSeek={request.HasSeek} seek={request.SeekNs} requestId={request.RequestId}",
                    out var controlTrace))
                    _logger.LogWarning(controlTrace);

                var state = runtime.ApplyPlaybackControl(
                    request.Command, request.Speed, request.HasSeek, request.SeekNs, request.RequestId);
                if (request.HasSeek)
                    ClearQueuedDataForPlaybackSeek();

                var playbackFrame = BinaryEncoding.EncodePlaybackState(
                    state.Status, state.CurrentTimeNs, state.Speed, state.DidSeek, state.RequestId);
                if (FoxgloveReplayTrace.TryEvent(
                    "STATE",
                    $"client={request.ClientId} status={state.Status} time={state.CurrentTimeNs} speed={state.Speed} didSeek={state.DidSeek} requestId={state.RequestId}",
                    out var stateTrace))
                    _logger.LogWarning(stateTrace);
                _transport.BroadcastBinary(playbackFrame);
            }
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
            if (msg == null || string.IsNullOrWhiteSpace(msg.Uri))
            {
                _transport.SendBinary(clientId,
                    BinaryEncoding.EncodeFetchAssetResponseError(msg?.RequestId ?? 0, "Asset URI is required"));
                return;
            }
            var runtime = Volatile.Read(ref _runtime);
            if (runtime?.Assets == null || !runtime.Assets.HasRoots)
            {
                _transport.SendBinary(clientId, BinaryEncoding.EncodeFetchAssetResponseError(msg.RequestId, "No asset roots registered"));
                return;
            }
            if (runtime.Assets.TryRead(msg.Uri, out var data, out var error))
                _transport.SendBinary(clientId, BinaryEncoding.EncodeFetchAssetResponseSuccess(msg.RequestId, data));
            else
                _transport.SendBinary(clientId, BinaryEncoding.EncodeFetchAssetResponseError(msg.RequestId, error));
        }
    }
}
