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
        // ── Publish ──

        /// <summary>
        /// Publish raw bytes to a channel using the current clock time.
        /// Encodes and sends binary MessageData frames per-subscriber.
        /// </summary>
        public void Publish(uint channelId, byte[] payload) => Publish(channelId, payload, _clock.NowNs);

        /// <summary>
        /// Publish raw bytes to a channel with an explicit log timestamp.
        /// Writes to MCAP recorder and sends to all subscribers.
        /// </summary>
        public void Publish(uint channelId, byte[] payload, ulong logTimeNs)
        {
            AdvertiseChannel channel;
            bool recordingOnly;
            lock (_channelLifecycleLock)
            {
                channel = _channels.Get(channelId);
                if (channel == null) return;
                recordingOnly = _recordingOnlyChannels.Contains(channelId);
            }

            payload ??= Array.Empty<byte>();
            var recorder = Volatile.Read(ref _recorder);
            if (recorder != null && AllowMcapRecording(channel) && recorder.HasServerChannel(channelId))
                WriteMessageSafely(recorder, channel, logTimeNs, payload);
            var mirrorSink = Volatile.Read(ref _mirrorSink);
            if (!recordingOnly && mirrorSink != null)
                TryMirrorPublish(mirrorSink, channel, logTimeNs, payload);
            if (!AllowLiveWebSocket(channel))
                return;
            var subscribers = CopySubscribersForPublish(channelId);
            try
            {
                foreach (var (clientId, subscriptionId) in subscribers)
                {
                    var frame = BinaryEncoding.EncodeServerMessageData(subscriptionId, logTimeNs, payload);
                    if (_prioritizedTransport != null)
                    {
                        if (FoxgloveReplayTrace.TryFrame("Live", channel.Topic, logTimeNs, clientId, subscriptionId, channelId, "data", out var trace))
                            _logger.LogWarning(trace);
                        _prioritizedTransport.SendDataBinary(clientId, frame);
                    }
                    else
                    {
                        if (FoxgloveReplayTrace.TryFrame("Live", channel.Topic, logTimeNs, clientId, subscriptionId, channelId, "fallback-control", out var trace))
                            _logger.LogWarning(trace);
                        _transport.SendBinary(clientId, frame);
                    }
                }
            }
            finally
            {
                subscribers.Clear();
            }
        }

        private void WriteMessageSafely(McapRecorder recorder, AdvertiseChannel channel, ulong logTimeNs, byte[] payload)
        {
            try
            {
                recorder.WriteMessage(
                    channel.Id,
                    logTimeNs,
                    payload,
                    channel.Encoding,
                    channel.ComponentLogicalSchemaName,
                    channel.ComponentShapeIdentity);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"MCAP recording failed; live publish continues: {ex.Message}");
            }
        }

        private List<(uint clientId, uint subscriptionId)> CopySubscribersForPublish(uint channelId)
        {
            // Keep the allocation-free normal path, but do not share an active
            // snapshot with a transport that synchronously reenters Publish.
            var subscribers = s_publishSubscriberScratch;
            if (subscribers == null)
            {
                subscribers = new List<(uint clientId, uint subscriptionId)>();
                s_publishSubscriberScratch = subscribers;
            }
            else if (subscribers.Count != 0)
            {
                subscribers = new List<(uint clientId, uint subscriptionId)>();
            }

            lock (_subscriberScratchLock)
            {
                _subscriptions.CopySubscribersForChannel(channelId, _subscriberScratch);
                try
                {
                    subscribers.AddRange(_subscriberScratch);
                }
                finally
                {
                    _subscriberScratch.Clear();
                }
            }

            return subscribers;
        }

        /// <summary>
        /// Publish replay data without recording it again. Replay frames use
        /// the transport data queue so playback seeks can drop stale pre-seek
        /// frames while preserving protocol control frames.
        /// </summary>
        internal void PublishReplay(uint channelId, byte[] payload, ulong logTimeNs, string source = "Replay", string topic = null)
        {
            AdvertiseChannel channel;
            lock (_channelLifecycleLock)
            {
                channel = _channels.Get(channelId);
                if (channel == null) return;
            }

            payload ??= Array.Empty<byte>();
            topic ??= channel.Topic;
            var subscribers = CopySubscribersForPublish(channelId);
            try
            {
                foreach (var (clientId, subscriptionId) in subscribers)
                {
                    var frame = BinaryEncoding.EncodeServerMessageData(subscriptionId, logTimeNs, payload);
                    if (_prioritizedTransport != null)
                    {
                        if (FoxgloveReplayTrace.TryFrame(source, topic, logTimeNs, clientId, subscriptionId, channelId, "data", out var trace))
                            _logger.LogWarning(trace);
                        _prioritizedTransport.SendDataBinary(clientId, frame);
                    }
                    else
                    {
                        if (FoxgloveReplayTrace.TryFrame(source, topic, logTimeNs, clientId, subscriptionId, channelId, "fallback-control", out var trace))
                            _logger.LogWarning(trace);
                        _transport.SendBinary(clientId, frame);
                    }
                }
            }
            finally
            {
                subscribers.Clear();
            }
        }

        /// <summary>
        /// Publish replay data to one client without allowing another client's
        /// queue headroom to throttle this client's history drain.
        /// </summary>
        internal void PublishReplayToClient(
            uint clientId,
            uint channelId,
            byte[] payload,
            ulong logTimeNs,
            string source = "Replay",
            string topic = null)
        {
            AdvertiseChannel channel;
            lock (_channelLifecycleLock)
            {
                channel = _channels.Get(channelId);
                if (channel == null) return;
            }

            var subscriptions = s_clientReplaySubscriptionScratch;
            if (subscriptions == null)
            {
                subscriptions = new List<(uint subscriptionId, uint channelId)>();
                s_clientReplaySubscriptionScratch = subscriptions;
            }

            lock (_subscriberScratchLock)
            {
                _subscriptions.CopySubscriptionsForClient(clientId, subscriptions);
            }

            payload ??= Array.Empty<byte>();
            topic ??= channel.Topic;
            try
            {
                foreach (var (subscriptionId, subscribedChannelId) in subscriptions)
                {
                    if (subscribedChannelId != channelId)
                        continue;

                    var frame = BinaryEncoding.EncodeServerMessageData(subscriptionId, logTimeNs, payload);
                    if (_prioritizedTransport != null)
                    {
                        if (FoxgloveReplayTrace.TryFrame(source, topic, logTimeNs, clientId, subscriptionId, channelId, "data", out var trace))
                            _logger.LogWarning(trace);
                        _prioritizedTransport.SendDataBinary(clientId, frame);
                    }
                    else
                    {
                        if (FoxgloveReplayTrace.TryFrame(source, topic, logTimeNs, clientId, subscriptionId, channelId, "fallback-control", out var trace))
                            _logger.LogWarning(trace);
                        _transport.SendBinary(clientId, frame);
                    }
                }
            }
            finally
            {
                subscriptions.Clear();
            }
        }

        /// <summary>Serialize an object to JSON and publish to the channel at the current clock time.</summary>
        public void PublishJson(uint channelId, object message) => PublishJson(channelId, message, _clock.NowNs);

        /// <summary>Serialize an object to JSON and publish to the channel with an explicit log timestamp.</summary>
        public void PublishJson(uint channelId, object message, ulong logTimeNs)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            var stream = s_jsonPublishStream ??= new MemoryStream(1024);
            stream.SetLength(0);
            stream.Position = 0;
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, leaveOpen: true))
            using (var jsonWriter = new JsonTextWriter(writer))
            {
                JsonSerializerCache.Value.Serialize(jsonWriter, message);
            }

            Publish(channelId, stream.ToArray(), logTimeNs);
        }

    }
}
