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
        // ── Status API ──

        /// <summary>
        /// Broadcast an official Foxglove status message on the diagnostics
        /// control plane. This is separate from <see cref="Publish(uint, byte[])"/>,
        /// which sends telemetry data-plane channel messages.
        /// </summary>
        /// <param name="level">Status severity encoded with official numeric values.</param>
        /// <param name="message">Human-readable diagnostic message. Null becomes empty text.</param>
        /// <param name="id">Optional stable status identifier for later replacement or removal.</param>
        public void PublishStatus(FoxgloveStatusLevel level, string message, string id = null)
        {
            _transport.BroadcastText(JsonConvert.SerializeObject(new StatusMessage
            {
                Level = level,
                Message = message ?? string.Empty,
                Id = string.IsNullOrEmpty(id) ? null : id
            }));
        }

        /// <summary>
        /// Broadcast an official Foxglove removeStatus message on the diagnostics
        /// control plane. Empty identifiers are ignored; an empty resulting set
        /// sends nothing.
        /// </summary>
        /// <param name="ids">Status identifiers to clear from Foxglove Problems.</param>
        public void RemoveStatus(IEnumerable<string> ids)
        {
            if (ids == null)
                return;

            var statusIds = new List<string>();
            foreach (var id in ids)
            {
                if (!string.IsNullOrEmpty(id))
                    statusIds.Add(id);
            }

            if (statusIds.Count == 0)
                return;

            _transport.BroadcastText(JsonConvert.SerializeObject(new RemoveStatusMessage
            {
                StatusIds = statusIds
            }));
        }

        // ── Channel API ──

        /// <summary>
        /// Register a channel for advertisement, update the connection graph,
        /// record to MCAP if attached, and broadcast the advertise message.
        /// </summary>
        public void RegisterChannel(AdvertiseChannel channel)
        {
            if (channel == null)
                throw new ArgumentNullException(nameof(channel));
            RegisterChannelCore(channel, recordingOnly: false);
        }

        /// <summary>
        /// Register an MCAP-only external-boundary channel. It is neither
        /// advertised to live clients nor mirrored into another transport.
        /// </summary>
        internal void RegisterRecordingOnlyChannel(AdvertiseChannel channel)
        {
            if (channel == null)
                throw new ArgumentNullException(nameof(channel));
            RegisterChannelCore(channel, recordingOnly: true);
        }

        private void RegisterChannelCore(AdvertiseChannel channel, bool recordingOnly)
        {
            lock (_channelLifecycleLock)
                RegisterChannelCoreLocked(channel, recordingOnly);
        }

        private void RegisterChannelCoreLocked(AdvertiseChannel channel, bool recordingOnly)
        {
            var previous = _channels.Get(channel.Id);
            var recorder = Volatile.Read(ref _recorder);
            var recorderChannelCompatible = false;
            var recorderHasChannel = recorder != null
                                     && recorder.TryGetServerChannelCompatibility(
                                         channel.Id,
                                         channel.Topic,
                                         channel.Encoding,
                                         channel.SchemaName,
                                         channel.SchemaEncoding ?? string.Empty,
                                         channel.Schema,
                                         out recorderChannelCompatible);
            if (recorderHasChannel && !recorderChannelCompatible)
            {
                throw new InvalidOperationException(
                    $"Channel id {channel.Id} cannot replace its descriptor while its existing MCAP channel is attached.");
            }

            var wasRecordingOnly = _recordingOnlyChannels.Contains(channel.Id);
            var sameDescriptor = previous != null && SameChannelDescriptor(previous, channel);
            var allowMcapRecording = recorder != null && AllowMcapRecording(channel);
            if (recorder != null && !allowMcapRecording)
                recorder.ClearServerChannelAdmissionRejection(channel.Id);
            if (recordingOnly && wasRecordingOnly && sameDescriptor)
            {
                if (recorder != null && !recorderHasChannel && allowMcapRecording)
                    EnsureRecorderChannel(recorder, channel);

                return;
            }

            var previousLiveAllowed = previous != null && AllowLiveWebSocket(previous);
            var topicChanged = previous != null
                               && !string.Equals(
                                   previous.Topic,
                                   channel.Topic,
                                   StringComparison.Ordinal);

            // Recorder admission may perform durable writes. Complete it
            // before mutating live/hidden registry, graph, mirror, or
            // subscription state so an I/O failure leaves the prior session
            // topology intact.
            if (recorder != null && !recorderHasChannel && allowMcapRecording)
                EnsureRecorderChannel(recorder, channel);

            var liveAllowed = !recordingOnly && _channelFilter.AllowLiveWebSocket(channel);
            if (previous != null && wasRecordingOnly != recordingOnly)
                _channels.Replace(channel);
            else
                _channels.Register(channel);

            var removedSubscriptions = new List<(uint clientId, uint subscriptionId, uint channelId)>();
            var graphChanged = false;
            var advertiseAttempted = false;
            var unadvertiseAttempted = false;
            var preexistingLiveAdvertisement = previousLiveAllowed
                                                && liveAllowed;
            try
            {
                if (recordingOnly)
                    _recordingOnlyChannels.Add(channel.Id);
                else
                    _recordingOnlyChannels.Remove(channel.Id);

                if (previousLiveAllowed && (topicChanged || !liveAllowed))
                {
                    _graph.RemoveUnityPublishedTopic(previous.Topic);
                    graphChanged = true;
                }

                if (liveAllowed)
                {
                    _graph.SetUnityPublishedTopic(channel.Topic);
                    graphChanged = true;
                }
                else if (previousLiveAllowed)
                {
                    removedSubscriptions.AddRange(_subscriptions.RemoveChannel(channel.Id));
                    foreach (var (clientId, subId, _) in removedSubscriptions)
                        _graph.RemoveSubscribedTopic(clientId, subId, previous.Topic);
                    graphChanged = true;
                }

                if (liveAllowed)
                {
                    advertiseAttempted = true;
                    _transport.BroadcastText(SerializeSingleAdvertise(channel));
                }
                else if (previousLiveAllowed)
                {
                    unadvertiseAttempted = true;
                    _transport.BroadcastText(SerializeSingleUnadvertise(channel.Id));
                }

                if (graphChanged)
                    _graph.BroadcastUpdate();

                var mirrorSink = Volatile.Read(ref _mirrorSink);
                if (mirrorSink != null)
                {
                    if (previous != null && !wasRecordingOnly && recordingOnly)
                        TryUnregisterMirrorChannel(mirrorSink, channel.Id);
                    if (!recordingOnly)
                        TryRegisterMirrorChannel(mirrorSink, channel);
                }
            }
            catch
            {
                if (advertiseAttempted && !preexistingLiveAdvertisement)
                    TryBroadcastChannelCompensation(
                        SerializeSingleUnadvertise(channel.Id),
                        "unadvertise");
                else if (unadvertiseAttempted)
                    TryBroadcastChannelCompensation(
                        SerializeSingleAdvertise(previous),
                        "advertise");

                RestoreChannelRegistration(
                    channel,
                    previous,
                    wasRecordingOnly,
                    previousLiveAllowed,
                    liveAllowed,
                    removedSubscriptions);
                if (graphChanged)
                    TryBroadcastChannelGraphCompensation();
                throw;
            }
        }

        private static bool SameChannelDescriptor(AdvertiseChannel left, AdvertiseChannel right)
            => left != null
               && right != null
               && string.Equals(left.Topic, right.Topic, StringComparison.Ordinal)
               && string.Equals(left.Encoding, right.Encoding, StringComparison.Ordinal)
               && string.Equals(left.SchemaName, right.SchemaName, StringComparison.Ordinal)
               && string.Equals(
                   left.SchemaEncoding ?? string.Empty,
                   right.SchemaEncoding ?? string.Empty,
                   StringComparison.Ordinal)
               && string.Equals(left.Schema, right.Schema, StringComparison.Ordinal);

        private static bool EnsureRecorderChannel(McapRecorder recorder, AdvertiseChannel channel)
        {
            if (recorder.TryGetServerChannelCompatibility(
                    channel.Id,
                    channel.Topic,
                    channel.Encoding,
                    channel.SchemaName,
                    channel.SchemaEncoding ?? string.Empty,
                    channel.Schema,
                    out var compatible))
            {
                if (!compatible)
                {
                    throw new InvalidOperationException(
                        $"Channel id {channel.Id} cannot replace its descriptor while its existing MCAP channel is attached.");
                }

                return true;
            }

            if (recorder.IsServerChannelAdmissionRejected(
                    channel.Id,
                    channel.Topic,
                    channel.Encoding,
                    channel.SchemaName,
                    channel.SchemaEncoding ?? string.Empty,
                    channel.Schema))
            {
                return false;
            }

            recorder.AddChannel(
                channel.Id,
                channel.Topic,
                channel.Encoding,
                channel.SchemaName,
                channel.SchemaEncoding ?? string.Empty,
                channel.Schema);
            return recorder.HasServerChannel(channel.Id);
        }

        /// <summary>
        /// Remove a channel from advertisement, clean up connections and
        /// subscriptions, and broadcast the unadvertise message.
        /// </summary>
        public void UnregisterChannel(uint channelId)
        {
            lock (_channelLifecycleLock)
            {
                var ch = _channels.Get(channelId);
                if (ch == null)
                    return;

                var wasRecordingOnly = _recordingOnlyChannels.Contains(channelId);
                var liveAllowed = ch != null && AllowLiveWebSocket(ch);
                var removedSubscriptions = new List<(uint clientId, uint subscriptionId, uint channelId)>();
                var graphChanged = false;
                var unadvertiseAttempted = false;
                _channels.Remove(channelId);
                _recordingOnlyChannels.Remove(channelId);
                try
                {
                    if (liveAllowed)
                    {
                        _graph.RemoveUnityPublishedTopic(ch.Topic);
                        removedSubscriptions.AddRange(_subscriptions.RemoveChannel(channelId));
                        foreach (var (clientId, subId, _) in removedSubscriptions)
                            _graph.RemoveSubscribedTopic(clientId, subId, ch.Topic);
                        graphChanged = true;
                        _graph.BroadcastUpdate();

                        unadvertiseAttempted = true;
                        _transport.BroadcastText(SerializeSingleUnadvertise(channelId));
                    }

                    var mirrorSink = Volatile.Read(ref _mirrorSink);
                    if (!wasRecordingOnly && mirrorSink != null)
                        TryUnregisterMirrorChannel(mirrorSink, channelId);
                    Volatile.Read(ref _recorder)?.ClearServerChannelAdmissionRejection(channelId);
                }
                catch
                {
                    if (unadvertiseAttempted)
                        TryBroadcastChannelCompensation(
                            SerializeSingleAdvertise(ch),
                            "advertise");

                    _channels.Replace(ch);
                    if (wasRecordingOnly)
                        _recordingOnlyChannels.Add(channelId);
                    if (liveAllowed)
                    {
                        _graph.SetUnityPublishedTopic(ch.Topic);
                        foreach (var (clientId, subId, restoredChannelId) in removedSubscriptions)
                        {
                            if (_subscriptions.TryAddSubscription(clientId, subId, restoredChannelId, out _))
                                _graph.AddSubscribedTopic(clientId, subId, ch.Topic);
                        }
                    }
                    if (graphChanged)
                        TryBroadcastChannelGraphCompensation();
                    throw;
                }
            }
        }

        private void RestoreChannelRegistration(
            AdvertiseChannel attempted,
            AdvertiseChannel previous,
            bool previousRecordingOnly,
            bool previousLiveAllowed,
            bool attemptedLiveAllowed,
            List<(uint clientId, uint subscriptionId, uint channelId)> removedSubscriptions)
        {
            if (previous == null)
                _channels.Remove(attempted.Id);
            else
                _channels.Replace(previous);

            if (previousRecordingOnly)
                _recordingOnlyChannels.Add(attempted.Id);
            else
                _recordingOnlyChannels.Remove(attempted.Id);

            if (attemptedLiveAllowed && (!previousLiveAllowed
                                         || !string.Equals(
                                             attempted.Topic,
                                             previous?.Topic,
                                             StringComparison.Ordinal)))
                _graph.RemoveUnityPublishedTopic(attempted.Topic);
            if (previousLiveAllowed && (!attemptedLiveAllowed
                                        || !string.Equals(
                                            attempted.Topic,
                                            previous?.Topic,
                                            StringComparison.Ordinal)))
                _graph.SetUnityPublishedTopic(previous.Topic);

            if (removedSubscriptions != null)
            {
                foreach (var (clientId, subId, channelId) in removedSubscriptions)
                {
                    if (_subscriptions.TryAddSubscription(clientId, subId, channelId, out _))
                        _graph.AddSubscribedTopic(clientId, subId, previous.Topic);
                }
            }
        }

        private void TryBroadcastChannelCompensation(string json, string operation)
        {
            try
            {
                _transport.BroadcastText(json);
            }
            catch (Exception compensationFailure)
            {
                _logger.LogWarning(
                    $"Channel {operation} compensation failed: " +
                    $"{compensationFailure.GetType().Name}: {compensationFailure.Message}");
            }
        }

        private void TryBroadcastChannelGraphCompensation()
        {
            try
            {
                _graph.BroadcastUpdate();
            }
            catch (Exception compensationFailure)
            {
                _logger.LogWarning(
                    "Channel graph rollback failed: " +
                    $"{compensationFailure.GetType().Name}: {compensationFailure.Message}");
            }
        }

        private string SerializeSingleAdvertise(AdvertiseChannel channel)
        {
            var channels = s_singleAdvertiseChannels ??= new List<AdvertiseChannel>(1);
            channels.Clear();
            channels.Add(channel);
            try
            {
                return JsonConvert.SerializeObject(new Advertise { Channels = channels });
            }
            finally
            {
                channels.Clear();
            }
        }

        private string SerializeSingleUnadvertise(uint channelId)
        {
            var channelIds = s_singleUnadvertiseChannelIds ??= new List<uint>(1);
            channelIds.Clear();
            channelIds.Add(channelId);
            try
            {
                return JsonConvert.SerializeObject(new Unadvertise { ChannelIds = channelIds });
            }
            finally
            {
                channelIds.Clear();
            }
        }

        /// <summary>
        /// Register a schema-based channel for advertisement and MCAP recording.
        /// Supports JSON ("jsonschema"), protobuf ("protobuf"), and explicit
        /// Provider-owned message/schema encoding pairs.
        /// </summary>
        /// <param name="channelId">Foxglove channel ID.</param>
        /// <param name="topic">Topic name (e.g. "/tf").</param>
        /// <param name="encoding">Message encoding advertised to clients.</param>
        /// <param name="schemaName">Schema name (e.g. "foxglove.FrameTransform").</param>
        /// <param name="schemaEncoding">Optional explicit schema encoding.</param>
    }
}
