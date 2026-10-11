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
        // ── Time ──

        /// <summary>
        /// Broadcast the current time to all clients at up to the given rate.
        /// Throttled so it doesn't fire on every call — only sends when the
        /// wall-clock interval has elapsed.
        /// </summary>
        public void BroadcastTime(float rateHz = 10f)
        {
            if (!_timeBroadcaster.TryReserveBroadcast(DateTime.UtcNow.Ticks, rateHz))
                return;

            var frame = BinaryEncoding.EncodeTime(_clock.NowNs);
            BroadcastDataBinary(frame);
        }

        /// <summary>Broadcast droppable data binary frames when the transport supports priority queues.</summary>
        internal void BroadcastDataBinary(byte[] data)
        {
            if (_prioritizedTransport != null)
                _prioritizedTransport.BroadcastDataBinary(data);
            else
                _transport.BroadcastBinary(data);
        }

        /// <summary>Broadcast replay time/data frames through the clearable data queue.</summary>
        internal void BroadcastReplayBinary(byte[] data)
        {
            BroadcastDataBinary(data);
        }

        /// <summary>Send a replay time frame to one client.</summary>
        internal void SendReplayTimeToClient(uint clientId, ulong timeNs)
        {
            var frame = BinaryEncoding.EncodeTime(timeNs);
            if (_prioritizedTransport != null)
                _prioritizedTransport.SendDataBinary(clientId, frame);
            else
                _transport.SendBinary(clientId, frame);
        }

        /// <summary>
        /// Return per-client queue headroom for replay history pacing when the
        /// transport exposes managed queue statistics.
        /// </summary>
        internal bool TryGetReplayQueueHeadroom(
            int reserveFrames,
            int reserveBytes,
            out int frameHeadroom,
            out int byteHeadroom,
            out int maxFrameCapacity,
            out int maxByteCapacity)
            => TryGetReplayQueueHeadroom(
                clientId: null,
                reserveFrames,
                reserveBytes,
                out frameHeadroom,
                out byteHeadroom,
                out maxFrameCapacity,
                out maxByteCapacity);

        /// <summary>
        /// Return queue headroom for one client, or the minimum across all
        /// clients when <paramref name="clientId"/> is null.
        /// </summary>
        internal bool TryGetReplayQueueHeadroom(
            uint? clientId,
            int reserveFrames,
            int reserveBytes,
            out int frameHeadroom,
            out int byteHeadroom,
            out int maxFrameCapacity,
            out int maxByteCapacity)
        {
            frameHeadroom = int.MaxValue;
            byteHeadroom = int.MaxValue;
            maxFrameCapacity = int.MaxValue;
            maxByteCapacity = int.MaxValue;

            if (_transport is not IFoxgloveTransportStatsProvider provider)
                return false;

            var stats = provider.GetStatsSnapshot();
            if (stats == null || !stats.Supported)
                return false;

            if (stats.Clients == null || stats.Clients.Count == 0)
            {
                frameHeadroom = 0;
                byteHeadroom = 0;
                maxFrameCapacity = stats.MaxQueuedFramesPerClient;
                maxByteCapacity = stats.MaxQueuedBytesPerClient;
                return true;
            }

            if (stats.MaxQueuedFramesPerClient <= 0 || stats.MaxQueuedBytesPerClient <= 0)
                return false;

            var minFrames = int.MaxValue;
            var minBytes = int.MaxValue;
            var frameReserve = Math.Max(0, reserveFrames);
            var byteReserve = Math.Max(0, reserveBytes);
            maxFrameCapacity = stats.MaxQueuedFramesPerClient;
            maxByteCapacity = stats.MaxQueuedBytesPerClient;
            var foundClient = false;
            foreach (var client in stats.Clients)
            {
                if (clientId.HasValue && client.ClientId != clientId.Value)
                    continue;

                foundClient = true;
                minFrames = Math.Min(minFrames, stats.MaxQueuedFramesPerClient - client.QueuedFrames - frameReserve);
                minBytes = Math.Min(minBytes, stats.MaxQueuedBytesPerClient - client.QueuedBytes - byteReserve);
            }

            if (clientId.HasValue && !foundClient)
            {
                frameHeadroom = 0;
                byteHeadroom = 0;
                return true;
            }

            frameHeadroom = Math.Max(0, minFrames);
            byteHeadroom = Math.Max(0, minBytes);
            return true;
        }

        internal HashSet<uint> SnapshotSubscribedChannelIds()
        {
            var result = new HashSet<uint>();
            lock (_channelLifecycleLock)
                _subscriptions.CopySubscribedChannelIds(result);
            return result;
        }

        internal HashSet<uint> SnapshotSubscribedChannelIds(uint clientId)
        {
            var result = new HashSet<uint>();
            lock (_channelLifecycleLock)
                _subscriptions.CopySubscribedChannelIds(clientId, result);
            return result;
        }

        internal HashSet<uint> SnapshotSubscribedClientIds()
        {
            var result = new HashSet<uint>();
            lock (_channelLifecycleLock)
                _subscriptions.CopyClientIds(result);
            return result;
        }

        /// <summary>
        /// Enable protobuf encoding support. Repeating an already enabled value
        /// after the session starts is an idempotent no-op.
        /// </summary>
        public void EnableProtobuf()
        {
            lock (_encodingConfigurationLock)
            {
                if (_protobufEnabled)
                    return;
                ThrowIfEncodingConfigurationFrozen();
                _protobufEnabled = true;
            }
        }

        /// <summary>Whether protobuf encoding support is enabled.</summary>
        public bool IsProtobufEnabled => _protobufEnabled;

        /// <summary>
        /// Enables one Provider-owned message encoding for future serverInfo
        /// snapshots. The value is copied into the session and contains no
        /// Provider instance or callback.
        /// </summary>
        public void EnableMessageEncoding(string encoding)
        {
            if (string.IsNullOrWhiteSpace(encoding))
                throw new ArgumentException("Message encoding cannot be empty.", nameof(encoding));

            var normalizedEncoding = encoding.Trim().ToLowerInvariant();
            if (string.Equals(normalizedEncoding, "protobuf", StringComparison.Ordinal))
            {
                EnableProtobuf();
                return;
            }

            lock (_encodingConfigurationLock)
            {
                if (string.Equals(normalizedEncoding, "json", StringComparison.Ordinal)
                    || _additionalMessageEncodings.Contains(normalizedEncoding))
                    return;
                ThrowIfEncodingConfigurationFrozen();
                _additionalMessageEncodings.Add(normalizedEncoding);
            }
        }

        private void ThrowIfEncodingConfigurationFrozen()
        {
            if (_encodingConfigurationFrozen)
                throw new InvalidOperationException(
                    "Message encodings must be configured before the session starts.");
        }

        public bool IsMessageEncodingEnabled(string encoding)
        {
            if (string.IsNullOrWhiteSpace(encoding))
                return false;

            var normalizedEncoding = encoding.Trim().ToLowerInvariant();
            lock (_encodingConfigurationLock)
            {
                if (string.Equals(normalizedEncoding, "json", StringComparison.Ordinal))
                    return true;

                if (string.Equals(normalizedEncoding, "protobuf", StringComparison.Ordinal))
                    return _protobufEnabled;

                return _additionalMessageEncodings.Contains(normalizedEncoding);
            }
        }

        private bool IsClientPublishEncodingSupported(string encoding)
        {
            if (string.IsNullOrWhiteSpace(encoding))
                return false;

            foreach (var supportedEncoding in GetSupportedEncodings())
            {
                if (string.Equals(supportedEncoding, encoding.Trim(), StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <summary>Force a test log message for diagnostic verification.</summary>
        internal void ForceLoggerTest() => _logger.LogWarning("logger test");

        /// <summary>
        /// Drop stale queued data frames before publishing replay data after
        /// a seek. PlaybackState.didSeek tells Foxglove to reset panel state;
        /// the WebSocket session and subscriptions must remain intact.
        /// </summary>
        internal void ClearQueuedDataForPlaybackSeek()
        {
            if (FoxgloveReplayTrace.TryEvent("CLEAR_DATA", "reason=playbackSeek", out var trace))
                _logger.LogWarning(trace);
            if (_transport is IReplayResettableFoxgloveTransport resettable)
                resettable.ClearDataQueues();
        }

        private ServerInfo CreateServerInfo()
        {
            var supportedEncodings = GetSupportedEncodings();
            var info = new ServerInfo
            {
                Name = Name,
                Capabilities = new List<Capability>
                {
                    Capability.Parameters,
                    Capability.ParametersSubscribe,
                    Capability.Services,
                    Capability.Time,
                    Capability.ConnectionGraph,
                    Capability.ClientPublish
                },
                SupportedEncodings = supportedEncodings,
                SessionId = SessionId
            };

            var runtime = Volatile.Read(ref _runtime);
            if (runtime?.PlaybackEnabled == true)
            {
                info.Capabilities.Add(Capability.PlaybackControl);
                var startNs = runtime.GetPlaybackStartNs();
                var endNs = runtime.GetPlaybackEndNs();
                info.DataStartTime = new DataTimestamp { Sec = startNs / 1_000_000_000, Nsec = (uint)(startNs % 1_000_000_000) };
                info.DataEndTime = new DataTimestamp { Sec = endNs / 1_000_000_000, Nsec = (uint)(endNs % 1_000_000_000) };
            }
            if (runtime?.Assets?.HasRoots == true)
                info.Capabilities.Add(Capability.Assets);

            return info;
        }

        private List<string> GetSupportedEncodings()
        {
            var supportedEncodings = new List<string> { "json" };
            if (_protobufEnabled)
                supportedEncodings.Add("protobuf");
            foreach (var encoding in _additionalMessageEncodings)
            {
                if (!supportedEncodings.Contains(encoding))
                    supportedEncodings.Add(encoding);
            }
            supportedEncodings.Sort(StringComparer.Ordinal);
            return supportedEncodings;
        }

        private static string SerializeServerInfo(ServerInfo info)
        {
            return JsonConvert.SerializeObject(info,
                new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
        }

        private void SendSessionSnapshot(
            uint clientId,
            IReadOnlyCollection<AdvertiseChannel> channels = null,
            IReadOnlyCollection<ServiceDescriptor> services = null)
        {
            _transport.SendText(clientId, SerializeServerInfo(CreateServerInfo()));

            var svcs = services ?? _services.GetAll();
            if (svcs.Count > 0)
                _transport.SendText(clientId, JsonConvert.SerializeObject(new AdvertiseServices
                {
                    Services = svcs as List<ServiceDescriptor> ?? new List<ServiceDescriptor>(svcs)
                }));

            // Services intentionally precede topics. Foxglove panels can watch
            // the topic snapshot and use its first render as an in-order
            // readiness barrier before calling a service advertised here.
            // Callers that pass `channels` already filtered them; only the
            // fallback snapshot of all channels needs live filtering here.
            var chs = channels ?? FilterLiveChannels(_channels.GetAll());
            if (chs.Count > 0)
                _transport.SendText(clientId, JsonConvert.SerializeObject(new Advertise
                {
                    Channels = chs as List<AdvertiseChannel> ?? new List<AdvertiseChannel>(chs)
                }));
        }

    }
}
