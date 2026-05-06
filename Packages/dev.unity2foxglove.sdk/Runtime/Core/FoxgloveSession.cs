// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core
// Purpose: Foxglove WebSocket session — owns channel/advertise/subscribe
// lifecycle, client messaging dispatch, connection graph, and MCAP recording
// integration. Split into partial classes for readability (Connection,
// Parameters, Services).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
    /// FoxgloveSession.Parameters.cs, and
    /// FoxgloveSession.Services.cs.
    /// </summary>
    public partial class FoxgloveSession : IDisposable
    {
        private readonly IFoxgloveTransport _transport;
        private readonly IFoxgloveClock _clock;
        private readonly ChannelRegistry _channels = new();
        private readonly SubscriptionRegistry _subscriptions = new();
        private readonly ISchemaRegistry _schemaRegistry;
        private readonly IFoxgloveLogger _logger;

        // Session holds references via interface
        private IRuntimeContext _runtime;
        private readonly FoxgloveParameterStore _parameters;
        private readonly ParameterSubscriptionRegistry _paramSubs = new();
        private readonly FoxgloveServiceRegistry _services;
        private readonly ConnectionGraphRegistry _graph = new();
        private readonly Dictionary<(uint clientId, uint chId), AdvertiseChannel> _clientChannels = new();
        public event Action<uint, uint, string, byte[]> OnClientMessage;

        private McapRecorder _recorder;
        private long _lastTimeBroadcastTicks;

        /// <summary>Server name sent in serverInfo.</summary>
        public string Name { get; }
        /// <summary>Unique session id per start. Sent in serverInfo.</summary>
        public string SessionId { get; }
        /// <summary>Whether the transport is currently listening.</summary>
        public bool IsRunning => _transport.IsRunning;
        public ISchemaRegistry Schemas => _schemaRegistry;
        public ChannelRegistry Channels => _channels;
        public FoxgloveParameterStore Parameters => _parameters;
        internal FoxgloveServiceRegistry Services => _services;
        internal IFoxgloveTransport Transport => _transport;

        internal void SetRuntimeContext(IRuntimeContext ctx) => _runtime = ctx;
        internal void SetRecorder(McapRecorder r) => _recorder = r;

        public FoxgloveSession(string name,
            IFoxgloveTransport transport,
            IFoxgloveClock clock = null,
            ISchemaRegistry schemaRegistry = null,
            IFoxgloveLogger logger = null,
            FoxgloveParameterStore paramStore = null,
            FoxgloveServiceRegistry serviceRegistry = null)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _clock = clock ?? new SystemClock();
            _schemaRegistry = schemaRegistry ?? new DefaultSchemaRegistry();
            _logger = logger ?? new ConsoleLogger();
            _parameters = paramStore ?? new FoxgloveParameterStore();
            _services = serviceRegistry ?? new FoxgloveServiceRegistry();
            SessionId = Guid.NewGuid().ToString();

            _transport.OnClientConnected += OnClientConnected;
            _transport.OnClientDisconnected += OnClientDisconnected;
            _transport.OnTextReceived += OnClientText;
            _transport.OnBinaryReceived += OnClientBinary;
        }

        // ── Lifecycle ──

        public void Start(string host, int port) => _transport.Start(host, port);
        public void Stop() => _transport.Stop();

        public void ClearSession()
        {
            _channels.Clear();
            _subscriptions.Clear();
            _paramSubs.Clear();
        }

        public void Dispose()
        {
            Stop();
            _transport.OnClientConnected -= OnClientConnected;
            _transport.OnClientDisconnected -= OnClientDisconnected;
            _transport.OnTextReceived -= OnClientText;
            _transport.OnBinaryReceived -= OnClientBinary;
        }

        // ── Channel API ──

        public void RegisterChannel(AdvertiseChannel channel)
        {
            _channels.Register(channel);
            _graph.SetPublishedTopic(channel.Topic, "unity");
            _graphDirty = true;
            _recorder?.AddChannel(channel.Id, channel.Topic, channel.Encoding,
                channel.SchemaName, channel.SchemaEncoding ?? "", channel.Schema);
            _transport.BroadcastText(JsonConvert.SerializeObject(
                new Advertise { Channels = new List<AdvertiseChannel> { channel } }));
            BroadcastGraphUpdate();
        }

        public void UnregisterChannel(uint channelId)
        {
            var ch = _channels.Get(channelId);
            if (ch != null)
            {
                _graph.RemovePublishedTopic(ch.Topic, "unity");
                _graphDirty = true;
            }
            if (!_channels.Remove(channelId)) return;
            _subscriptions.RemoveChannel(channelId);
            _transport.BroadcastText(JsonConvert.SerializeObject(
                new Unadvertise { ChannelIds = new List<uint> { channelId } }));
            BroadcastGraphUpdate();
        }

        public void RegisterSchemaChannel(uint channelId, string topic, string schemaName)
        {
            if (!_schemaRegistry.TryGetSchema(schemaName, out var entry))
                throw new InvalidOperationException($"Schema not found: '{schemaName}'.");
            RegisterChannel(new AdvertiseChannel
            {
                Id = channelId, Topic = topic,
                Encoding = "json", SchemaName = entry.Name,
                SchemaEncoding = entry.Encoding, Schema = entry.Content
            });
        }

        // ── Publish ──

        /// <summary>
        /// Publish raw bytes to a channel. Encodes the binary MessageData frame
        /// per-client with the correct subscriptionId and logTime. Also writes
        /// to the MCAP recorder if attached.
        /// </summary>
        public void Publish(uint channelId, byte[] payload) => Publish(channelId, payload, _clock.NowNs);

        public void Publish(uint channelId, byte[] payload, ulong logTimeNs)
        {
            if (_channels.Get(channelId) == null) return;
            _recorder?.WriteMessage(channelId, logTimeNs, payload);
            foreach (var (clientId, subscriptionId) in _subscriptions.GetSubscribersForChannel(channelId))
            {
                _transport.SendBinary(clientId,
                    BinaryEncoding.EncodeServerMessageData(subscriptionId, logTimeNs, payload));
            }
        }

        public void PublishJson(uint channelId, object message) => PublishJson(channelId, message, _clock.NowNs);

        public void PublishJson(uint channelId, object message, ulong logTimeNs)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            Publish(channelId, Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(message)), logTimeNs);
        }

        public uint RegisterService(Protocol.ServiceDescriptor descriptor)
        {
            var id = _services.Register(descriptor);
            var adv = new Protocol.AdvertiseServices { Services = new List<ServiceDescriptor> { _services.GetById(id) } };
            _transport.BroadcastText(JsonConvert.SerializeObject(adv));
            _graph.AddAdvertisedService(descriptor.Name, "unity");
            _graphDirty = true;
            BroadcastGraphUpdate();
            return id;
        }

        // ── Time ──

        /// <summary>
        /// Broadcast the current time to all clients at up to the given rate.
        /// Throttled so it doesn't fire on every call — only sends when the
        /// wall-clock interval has elapsed.
        /// </summary>
        public void BroadcastTime(float rateHz = 10f)
        {
            var now = DateTime.UtcNow.Ticks;
            var effectiveRate = rateHz > 0 ? rateHz : 10f;
            var interval = TimeSpan.TicksPerSecond / (long)effectiveRate;
            if (effectiveRate > TimeSpan.TicksPerSecond)
                interval = 1L;
            if (now - _lastTimeBroadcastTicks < interval)
                return;
            _lastTimeBroadcastTicks = now;

            var frame = BinaryEncoding.EncodeTime(_clock.NowNs);
            _transport.BroadcastBinary(frame);
        }

        internal void ForceLoggerTest() => _logger.LogWarning("logger test");

        // ── Transport event handlers ──

        /// <summary>
        /// On client connect: send serverInfo (with current capabilities),
        /// advertise all registered channels/services, and seed the connection
        /// graph. Capabilities like PlaybackControl and Assets are conditionally
        /// included based on runtime state.
        /// </summary>
        private void OnClientConnected(uint clientId)
        {
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
                SupportedEncodings = new List<string> { "json" },
                SessionId = SessionId
            };

            if (_runtime?.PlaybackEnabled == true)
            {
                info.Capabilities.Add(Capability.PlaybackControl);
                var startNs = _runtime.GetPlaybackStartNs();
                var endNs = _runtime.GetPlaybackEndNs();
                info.DataStartTime = new DataTimestamp { Sec = startNs / 1_000_000_000, Nsec = (uint)(startNs % 1_000_000_000) };
                info.DataEndTime = new DataTimestamp { Sec = endNs / 1_000_000_000, Nsec = (uint)(endNs % 1_000_000_000) };
            }
            if (_runtime?.Assets?.HasRoots == true)
                info.Capabilities.Add(Capability.Assets);

            _transport.SendText(clientId, JsonConvert.SerializeObject(info,
                new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }));

            var chs = _channels.GetAll();
            if (chs.Count > 0)
                _transport.SendText(clientId, JsonConvert.SerializeObject(new Advertise { Channels = chs }));

            var svcs = _services.GetAll();
            if (svcs.Count > 0)
                _transport.SendText(clientId, JsonConvert.SerializeObject(new AdvertiseServices { Services = svcs }));

            foreach (var ch in chs)
                _graph.SetPublishedTopic(ch.Topic, "unity");
            foreach (var svc in svcs)
                _graph.AddAdvertisedService(svc.Name, "unity");
            _graphDirty = true;
        }

        /// <summary>
        /// On client disconnect: clean up subscriptions, parameter subs,
        /// connection graph, pending service calls, and client-published
        /// channels associated with this client.
        /// </summary>
        private void OnClientDisconnected(uint clientId)
        {
            _subscriptions.RemoveClient(clientId);
            _paramSubs.RemoveClient(clientId);
            _graph.RemoveClient(clientId);
            _graphDirty = true;
            _services.RemoveClientCalls(clientId);
            var toRemove = _clientChannels.Keys.Where(k => k.clientId == clientId).ToList();
            foreach (var k in toRemove) _clientChannels.Remove(k);
        }

        /// <summary>
        /// Dispatch client JSON messages by opcode. Malformed JSON is logged
        /// as a warning; unknown ops are logged but not fatal.
        /// </summary>
        private void OnClientText(uint clientId, string json)
        {
            string op;
            try { op = JObject.Parse(json)["op"]?.ToString(); }
            catch { _logger.LogWarning($"Malformed JSON from client {clientId}"); return; }

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
        /// Dispatch client binary frames: try PlaybackControl first,
        /// then ClientPublish, then ServiceCallRequest.
        /// </summary>
        private void OnClientBinary(uint clientId, byte[] data)
        {
            if (HandlePlaybackControlRequest(clientId, data)) return;
            if (BinaryEncoding.TryDecodeClientMessageData(data, out var chId, out _))
            {
                HandleClientBinaryPublish(clientId, data);
                return;
            }
            HandleServiceCallRequest(clientId, data);
        }
    }
}
