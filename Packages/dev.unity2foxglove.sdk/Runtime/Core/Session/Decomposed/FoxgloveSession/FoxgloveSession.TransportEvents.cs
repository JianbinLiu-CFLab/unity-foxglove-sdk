// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core/Session
// Purpose: Foxglove WebSocket session — owns channel/advertise/subscribe
// lifecycle, client messaging dispatch, connection graph, and MCAP recording
// integration. Split into partial classes for readability (Connection,
// Parameters, Services).

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Transport;
using Unity.FoxgloveSDK.Schemas;

namespace Unity.FoxgloveSDK.Core
{
    /// <summary>
    /// Core session that handles Foxglove WebSocket protocol logic:
    /// channel lifecycle, subscription routing, binary message encoding,
    /// client text/binary dispatch, time broadcast, connection graph,
    /// and serverInfo on connect.
    ///
    /// Split across source files for readability:
    /// FoxgloveSession.Connection.cs (subscribe/unsubscribe,
    /// ConnectionGraph, ClientPublish, PlaybackControl, Assets),
    /// FoxgloveSession.Parameters.cs, FoxgloveSession.Services.cs, and
    /// the Phase192 lifecycle, channel, publishing, service, time, and
    /// transport sections.
    /// </summary>
    /// <remarks>Phase192 source sections: FoxgloveSession.Lifecycle.cs, FoxgloveSession.Channels.cs, FoxgloveSession.SchemaChannels.cs, FoxgloveSession.Publishing.cs, FoxgloveSession.ServiceRegistration.cs, FoxgloveSession.TimeAndEncoding.cs, FoxgloveSession.TransportEvents.cs</remarks>
    public partial class FoxgloveSession : IDisposable
    {
        // ── Transport event handlers ──

        /// <summary>
        /// On client connect: send serverInfo (with current capabilities),
        /// advertise all registered channels/services, and seed the connection
        /// graph. Capabilities like PlaybackControl and Assets are conditionally
        /// included based on runtime state.
        /// </summary>
        private void OnClientConnected(uint clientId)
        {
            lock (_channelLifecycleLock)
            {
                // Keep the service snapshot, its client-visible frames, and
                // graph seeding in the same lifecycle epoch as register /
                // unregister. Otherwise an unregister can remove a service
                // after this snapshot is captured and the stale service can
                // be re-seeded into the graph after the client is notified.
                lock (_serviceLifecycleLock)
                {
                    var chs = FilterLiveChannels(_channels.GetAll());
                    var svcs = _services.GetAll();

                    SendSessionSnapshot(clientId, chs, svcs);
                    _graph.SeedUnityState(chs, svcs);
                }
            }
        }

        private IReadOnlyCollection<AdvertiseChannel> FilterLiveChannels(IReadOnlyCollection<AdvertiseChannel> channels)
        {
            var filtered = _channelFilter.FilterLiveChannels(channels);
            if (filtered.Count == 0 || _recordingOnlyChannels.Count == 0)
                return filtered;

            var live = new List<AdvertiseChannel>(filtered.Count);
            foreach (var channel in filtered)
            {
                // Keep late-client snapshots on the same predicate as immediate
                // advertisements. In particular, recording-only Provider channels must
                // never become visible merely because the client connected
                // after their registration.
                if (channel != null && !_recordingOnlyChannels.Contains(channel.Id))
                    live.Add(channel);
            }

            return live;
        }

        private bool AllowLiveWebSocket(AdvertiseChannel channel)
            => channel != null
               && !_recordingOnlyChannels.Contains(channel.Id)
               && _channelFilter.AllowLiveWebSocket(channel);

        private bool AllowMcapRecording(AdvertiseChannel channel)
            => _channelFilter.AllowMcapRecording(channel);

        private bool HasMirrorDemand(AdvertiseChannel channel)
        {
            var sink = Volatile.Read(ref _mirrorSink);
            if (sink == null)
                return false;

            try
            {
                return sink.HasChannelDemand(channel);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Mirror sink demand check failed for channel {channel.Id}: {ex.Message}");
                return false;
            }
        }

        private void TryRegisterMirrorChannel(IFoxgloveMirrorSink sink, AdvertiseChannel channel)
        {
            try
            {
                sink.RegisterChannel(channel);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Mirror sink channel registration failed for channel {channel.Id}: {ex.Message}");
            }
        }

        private void TryUnregisterMirrorChannel(IFoxgloveMirrorSink sink, uint channelId)
        {
            try
            {
                sink.UnregisterChannel(channelId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Mirror sink channel removal failed for channel {channelId}: {ex.Message}");
            }
        }

        private void TryMirrorPublish(IFoxgloveMirrorSink sink, AdvertiseChannel channel, ulong logTimeNs, byte[] payload)
        {
            try
            {
                sink.Publish(channel, logTimeNs, payload);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Mirror sink publish failed for channel {channel.Id}: {ex.Message}");
            }
        }

        /// <summary>
        /// On client disconnect: clean up subscriptions, parameter subs,
        /// connection graph, pending service calls, and client-published
        /// channels associated with this client.
        /// </summary>
        private void OnClientDisconnected(uint clientId)
        {
            lock (_channelLifecycleLock)
            {
                // Snapshot and remove subscriptions before cleaning graph, so we
                // can remove the subscribed-topic entries using the correct subscriber id.
                var removedSubs = _subscriptions.RemoveClientPreservingData(clientId);
                foreach (var (subId, chId) in removedSubs)
                {
                    var ch = _channels.Get(chId);
                    if (ch != null)
                        _graph.RemoveSubscribedTopic(clientId, subId, ch.Topic);
                }

                _paramSubs.RemoveClient(clientId);
                _services.RemoveClientCalls(clientId);
                _clientPublish.RemoveClient(clientId);
                _graph.RemoveClient(clientId);
                _graph.BroadcastUpdate();
            }
            lock (_subscriptionBudgetWarnedClientsLock)
                _subscriptionBudgetWarnedClients.Remove(clientId);

            var runtime = Volatile.Read(ref _runtime);
            if (runtime is IClientReplayDisconnectContext replayDisconnect)
                replayDisconnect.CancelReplayForClient(clientId);
        }

        /// <summary>
        /// Dispatch client JSON messages by opcode. Malformed JSON is logged
        /// as a warning; unknown ops are logged but not fatal.
        /// </summary>
        private void OnClientText(uint clientId, string json)
        {
            string op = null;
            try
            {
                op = TryReadOpField(json);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning($"Malformed JSON from client {clientId}: {ex.GetType().Name}");
                return;
            }

            if (op == null)
            {
                _logger.LogWarning($"Missing or null 'op' field from client {clientId}");
                return;
            }

            switch (op)
            {
                case "subscribe": HandleSubscribe(clientId, json); break;
                case "unsubscribe": HandleUnsubscribe(clientId, json); break;
                case "getParameters": HandleGetParameters(clientId, json); break;
                case "setParameters": HandleSetParameters(clientId, json); break;
                case "subscribeParameterUpdates": HandleSubscribeParameterUpdates(clientId, json); break;
                case "unsubscribeParameterUpdates": HandleUnsubscribeParameterUpdates(clientId, json); break;
                case "subscribeConnectionGraph": HandleSubscribeConnectionGraph(clientId); break;
                case "unsubscribeConnectionGraph": HandleUnsubscribeConnectionGraph(clientId); break;
                case "advertise": HandleClientAdvertise(clientId, json); break;
                case "unadvertise": HandleClientUnadvertise(clientId, json); break;
                case "fetchAsset": HandleFetchAsset(clientId, json); break;
                default: _logger.LogWarning($"Unknown op '{op}' from client {clientId}"); break;
            }
        }

        /// <summary>
        /// Read the top-level "op" field from a Foxglove control-message JSON
        /// string without allocating a full JObject. Returns the string value,
        /// empty string for null, or null when the field is absent.
        /// Matches <c>JObject.Parse(json)["op"]?.ToString()</c> exactly, including
        /// the duplicate-key rule: JObject's default DuplicatePropertyNameHandling
        /// is Replace (last value wins), so this keeps the LAST top-level "op"
        /// rather than returning on the first match. Internal for direct test access.
        /// </summary>
        internal static string TryReadOpField(string json)
        {
            string result = null;
            using (var reader = new JsonTextReader(new StringReader(json)))
            {
                while (reader.Read())
                {
                    if (reader.Depth == 1
                        && reader.TokenType == JsonToken.PropertyName
                        && string.Equals(reader.Value as string, "op", StringComparison.Ordinal))
                    {
                        reader.Read();
                        // JValue(null)?.ToString() == ""; last top-level "op" wins.
                        result = reader.TokenType == JsonToken.Null ? "" : reader.Value?.ToString();
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Dispatch client binary frames: try PlaybackControl first,
        /// then ClientPublish, then ServiceCallRequest.
        /// </summary>
        private void OnClientBinary(uint clientId, byte[] data)
        {
            if (data != null
                && data.Length != 0
                && data[0] == ClientOpcode.PlaybackControlRequest)
            {
                if (!HandlePlaybackControlRequest(clientId, data))
                {
                    _logger.LogWarning(
                        $"PlaybackControl request from client {clientId} was malformed or unavailable.");
                }
                return;
            }
            if (BinaryEncoding.TryDecodeClientMessageData(data, out var chId, out var payload))
            {
                HandleClientBinaryPublish(clientId, chId, payload);
                return;
            }
            HandleServiceCallRequest(clientId, data);
        }
    }
}
