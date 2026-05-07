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
        /// <summary>Optional logger for diagnostics and warnings.</summary>
        private readonly IFoxgloveLogger _logger;

        // Session holds references via interface
        /// <summary>Runtime context for playback, assets, and lifecycle control.</summary>
        private IRuntimeContext _runtime;
        private readonly FoxgloveParameterStore _parameters;
        /// <summary>Per-client parameter update subscriptions.</summary>
        private readonly ParameterSubscriptionRegistry _paramSubs = new();
        /// <summary>Registered service descriptors and pending call queue.</summary>
        private readonly FoxgloveServiceRegistry _services;
        private readonly ConnectionGraphRegistry _graph = new();
        private readonly Dictionary<(uint clientId, uint chId), AdvertiseChannel> _clientChannels = new();
        /// <summary>Lock protecting <c>_clientChannels</c> concurrent access.</summary>
        private readonly object _clientChannelsLock = new();
        /// <summary>Raised when a client-published binary message is received.</summary>
        public event Action<uint, uint, string, byte[]> OnClientMessage;

        private McapRecorder _recorder;
        private long _lastTimeBroadcastTicks;

        /// <summary>Server name sent in serverInfo.</summary>
        public string Name { get; }
        /// <summary>Unique session id per start. Sent in serverInfo.</summary>
        public string SessionId { get; }
        /// <summary>Whether the transport is currently listening.</summary>
        public bool IsRunning => _transport.IsRunning;
        /// <summary>Schema registry attached to this session.</summary>
        public ISchemaRegistry Schemas => _schemaRegistry;
        /// <summary>Channel registry managing all advertised channels.</summary>
        public ChannelRegistry Channels => _channels;
        /// <summary>Parameter store shared across clients.</summary>
        public FoxgloveParameterStore Parameters => _parameters;
        /// <summary>Service registry for this session.</summary>
        internal FoxgloveServiceRegistry Services => _services;
        /// <summary>Underlying WebSocket transport.</summary>
        internal IFoxgloveTransport Transport => _transport;

        /// <summary>Inject the runtime context for playback and asset access.</summary>
        internal void SetRuntimeContext(IRuntimeContext ctx) => _runtime = ctx;
        /// <summary>Attach an MCAP recorder for session recording.</summary>
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

        /// <summary>Start the WebSocket transport on the given host and port.</summary>
        public void Start(string host, int port) => _transport.Start(host, port);
        /// <summary>Stop the WebSocket transport.</summary>
        public void Stop() => _transport.Stop();

        /// <summary>Clear all channels, subscriptions, and parameter subscriptions.</summary>
        public void ClearSession()
        {
            _channels.Clear();
            _subscriptions.Clear();
            _paramSubs.Clear();
        }

        /// <summary>Stop the transport and detach all event handlers.</summary>
        public void Dispose()
        {
            Stop();
            _transport.OnClientConnected -= OnClientConnected;
            _transport.OnClientDisconnected -= OnClientDisconnected;
            _transport.OnTextReceived -= OnClientText;
            _transport.OnBinaryReceived -= OnClientBinary;
        }

        // ── Channel API ──

        /// <summary>
        /// Register a channel for advertisement, update the connection graph,
        /// record to MCAP if attached, and broadcast the advertise message.
        /// </summary>
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

        /// <summary>
        /// Remove a channel from advertisement, clean up connections and
        /// subscriptions, and broadcast the unadvertise message.
        /// </summary>
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

        /// <summary>
        /// Look up a schema by name and register it as a JSON-encoded channel.
        /// Throws if the schema is not found in the registry.
        /// </summary>
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
            if (_channels.Get(channelId) == null) return;
            _recorder?.WriteMessage(channelId, logTimeNs, payload);
            foreach (var (clientId, subscriptionId) in _subscriptions.GetSubscribersForChannel(channelId))
            {
                _transport.SendBinary(clientId,
                    BinaryEncoding.EncodeServerMessageData(subscriptionId, logTimeNs, payload));
            }
        }

        /// <summary>Serialize an object to JSON and publish to the channel at the current clock time.</summary>
        public void PublishJson(uint channelId, object message) => PublishJson(channelId, message, _clock.NowNs);

        /// <summary>Serialize an object to JSON and publish to the channel with an explicit log timestamp.</summary>
        public void PublishJson(uint channelId, object message, ulong logTimeNs)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            Publish(channelId, Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(message)), logTimeNs);
        }

        /// <summary>
        /// Register a service endpoint, advertise it to all clients, and
        /// update the connection graph. Returns the assigned service ID.
        /// </summary>
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

        /// <summary>Force a test log message for diagnostic verification.</summary>
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
            // Snapshot and remove subscriptions before cleaning graph, so we
            // can remove the subscribed-topic entries using the correct subscriber id.
            var removedSubs = _subscriptions.RemoveClientPreservingData(clientId);
            foreach (var (subId, chId) in removedSubs)
            {
                var ch = _channels.Get(chId);
                if (ch != null)
                    _graph.RemoveSubscribedTopic(ch.Topic, $"client:{clientId}:{subId}");
            }

            _paramSubs.RemoveClient(clientId);
            _services.RemoveClientCalls(clientId);

            lock (_clientChannelsLock)
            {
                var toRemove = _clientChannels.Keys.Where(k => k.clientId == clientId).ToList();
                foreach (var k in toRemove)
                {
                    if (_clientChannels.Remove(k, out var ch))
                        _graph.RemovePublishedTopic(ch.Topic, $"client:{clientId}:{k.chId}");
                }
            }

            _graph.RemoveClient(clientId);
            _graphDirty = true;
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
