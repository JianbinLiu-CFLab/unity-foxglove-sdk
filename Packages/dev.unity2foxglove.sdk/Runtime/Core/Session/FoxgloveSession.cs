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

        /// <summary>Whether protobuf encoding support is enabled for channel registration.</summary>
        private bool _protobufEnabled;

        // Session holds references via interface
        /// <summary>Runtime context for playback, assets, and lifecycle control.</summary>
        private IRuntimeContext _runtime;
        private readonly FoxgloveParameterStore _parameters;
        /// <summary>Per-client parameter update subscriptions.</summary>
        private readonly ParameterSubscriptionRegistry _paramSubs = new();
        /// <summary>Registered service descriptors and pending call queue.</summary>
        private readonly FoxgloveServiceRegistry _services;
        private readonly SessionGraphHandler _graph;
        private readonly SessionPlaybackHandler _playback;
        private readonly SessionClientPublishHandler _clientPublish;
        private readonly SessionAssetHandler _assets;
        /// <summary>Maximum queued playback control requests awaiting the runtime owner tick.</summary>
        internal const int MaxPendingPlaybackControls = SessionPlaybackHandler.MaxPendingPlaybackControls;
        /// <summary>Raised when a client-published binary message is received.</summary>
        public event Action<uint, uint, string, byte[]> OnClientMessage;

        private McapRecorder _recorder;
        private long _lastTimeBroadcastTicks;

        /// <summary>Server name sent in serverInfo.</summary>
        public string Name { get; }
        /// <summary>Unique session id. Sent in serverInfo and rotated on replay seeks.</summary>
        private string _sessionId;
        /// <summary>Unique session id per active visualization segment. Sent in serverInfo.</summary>
        public string SessionId => Volatile.Read(ref _sessionId);
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
        internal void SetRuntimeContext(IRuntimeContext ctx) => Volatile.Write(ref _runtime, ctx);
        /// <summary>Attach an MCAP recorder for session recording.</summary>
        internal void SetRecorder(McapRecorder r) => Volatile.Write(ref _recorder, r);

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
            _sessionId = Guid.NewGuid().ToString();
            _graph = new SessionGraphHandler(_transport, _logger, () => Volatile.Read(ref _recorder));
            _playback = new SessionPlaybackHandler(
                () => Volatile.Read(ref _runtime),
                _transport,
                _logger,
                ClearQueuedDataForPlaybackSeek);
            _clientPublish = new SessionClientPublishHandler(
                () => Volatile.Read(ref _recorder),
                _clock,
                _logger,
                _graph,
                (clientId, chId, topic, payload) => OnClientMessage?.Invoke(clientId, chId, topic, payload));
            _assets = new SessionAssetHandler(() => Volatile.Read(ref _runtime), _transport);

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

        /// <summary>Clear all transient session channels, subscriptions, and graph state.</summary>
        public void ClearSession()
        {
            _channels.Clear();
            _subscriptions.Clear();
            _paramSubs.Clear();
            _services.ClearPendingCalls();
            _graph.Clear();
            _playback.Clear();
            _clientPublish.Clear();
        }

        /// <summary>Stop the transport and detach all event handlers.</summary>
        public void Dispose()
        {
            Stop();
            _transport.OnClientConnected -= OnClientConnected;
            _transport.OnClientDisconnected -= OnClientDisconnected;
            _transport.OnTextReceived -= OnClientText;
            _transport.OnBinaryReceived -= OnClientBinary;
            OnClientMessage = null;
        }

        // ── Channel API ──

        /// <summary>
        /// Register a channel for advertisement, update the connection graph,
        /// record to MCAP if attached, and broadcast the advertise message.
        /// </summary>
        public void RegisterChannel(AdvertiseChannel channel)
        {
            _channels.Register(channel);
            _graph.SetUnityPublishedTopic(channel.Topic);
            var recorder = Volatile.Read(ref _recorder);
            recorder?.AddChannel(channel.Id, channel.Topic, channel.Encoding,
                channel.SchemaName, channel.SchemaEncoding ?? "", channel.Schema);
            _transport.BroadcastText(JsonConvert.SerializeObject(
                new Advertise { Channels = new List<AdvertiseChannel> { channel } }));
            _graph.BroadcastUpdate();
        }

        /// <summary>
        /// Remove a channel from advertisement, clean up connections and
        /// subscriptions, and broadcast the unadvertise message.
        /// </summary>
        public void UnregisterChannel(uint channelId)
        {
            var ch = _channels.Get(channelId);
            if (ch != null)
                _graph.RemoveUnityPublishedTopic(ch.Topic);
            if (!_channels.Remove(channelId)) return;
            foreach (var (clientId, subId, _) in _subscriptions.RemoveChannel(channelId))
            {
                if (ch != null)
                    _graph.RemoveSubscribedTopic(clientId, subId, ch.Topic);
            }
            _transport.BroadcastText(JsonConvert.SerializeObject(
                new Unadvertise { ChannelIds = new List<uint> { channelId } }));
            _graph.BroadcastUpdate();
        }

        /// <summary>
        /// Register a schema-based channel for advertisement and MCAP recording.
        /// Supports both JSON ("jsonschema") and protobuf ("protobuf") encoding.
        /// </summary>
        /// <param name="channelId">Foxglove channel ID.</param>
        /// <param name="topic">Topic name (e.g. "/tf").</param>
        /// <param name="encoding">Message encoding: "json" or "protobuf".</param>
        /// <param name="schemaName">Schema name (e.g. "foxglove.FrameTransform").</param>
        public void RegisterSchemaChannel(uint channelId, string topic, string schemaName, string encoding = "json")
        {
            var messageEncoding = string.IsNullOrEmpty(encoding) ? "json" : encoding;
            var expectedSchemaEncoding = ExpectedSchemaEncodingForMessageEncoding(messageEncoding);

            var found = false;
            SchemaEntry entry;
            if (expectedSchemaEncoding != null && _schemaRegistry is IEncodingAwareSchemaRegistry encodingAwareRegistry)
                found = encodingAwareRegistry.TryGetSchema(schemaName, expectedSchemaEncoding, out entry);
            else
                entry = default;

            if (!found && !_schemaRegistry.TryGetSchema(schemaName, out entry))
                throw new InvalidOperationException($"Schema not found: '{schemaName}'.");

            var schemaEncoding = entry.Encoding;
            var schema = entry.Content;
            if (expectedSchemaEncoding != null
                && !string.Equals(schemaEncoding, expectedSchemaEncoding, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Schema '{schemaName}' has schema encoding '{schemaEncoding}', " +
                    $"but message encoding '{messageEncoding}' requires '{expectedSchemaEncoding}'.");
            }

            // For protobuf, the schema content is already base64-encoded FileDescriptorSet bytes.
            // For JSON, the schema content is the raw JSON Schema text.
            RegisterChannel(new AdvertiseChannel
            {
                Id = channelId, Topic = topic,
                Encoding = messageEncoding, SchemaName = entry.Name,
                SchemaEncoding = schemaEncoding, Schema = schema
            });
        }

        private static string ExpectedSchemaEncodingForMessageEncoding(string messageEncoding)
        {
            if (string.Equals(messageEncoding, "json", StringComparison.OrdinalIgnoreCase))
                return "jsonschema";
            if (string.Equals(messageEncoding, "protobuf", StringComparison.OrdinalIgnoreCase))
                return "protobuf";
            return null;
        }

        /// <summary>
        /// Register a protobuf-encoded channel and advertise it.
        /// Uses the schema registry to look up FileDescriptorSet bytes.
        /// </summary>
        public void RegisterProtobufSchemaChannel(uint channelId, string topic, string schemaName)
        {
            RegisterSchemaChannel(channelId, topic, schemaName, "protobuf");
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
            var channel = _channels.Get(channelId);
            if (channel == null) return;
            payload ??= Array.Empty<byte>();
            var recorder = Volatile.Read(ref _recorder);
            recorder?.WriteMessage(channelId, logTimeNs, payload);
            foreach (var (clientId, subscriptionId) in _subscriptions.GetSubscribersForChannel(channelId))
            {
                var frame = BinaryEncoding.EncodeServerMessageData(subscriptionId, logTimeNs, payload);
                if (_transport is IPrioritizedFoxgloveTransport prioritized)
                {
                    if (FoxgloveReplayTrace.TryFrame("Live", channel.Topic, logTimeNs, clientId, subscriptionId, channelId, "data", out var trace))
                        _logger.LogWarning(trace);
                    prioritized.SendDataBinary(clientId, frame);
                }
                else
                {
                    if (FoxgloveReplayTrace.TryFrame("Live", channel.Topic, logTimeNs, clientId, subscriptionId, channelId, "fallback-control", out var trace))
                        _logger.LogWarning(trace);
                    _transport.SendBinary(clientId, frame);
                }
            }
        }

        /// <summary>
        /// Publish replay data without recording it again. Replay frames use
        /// the transport data queue so playback seeks can drop stale pre-seek
        /// frames while preserving protocol control frames.
        /// </summary>
        internal void PublishReplay(uint channelId, byte[] payload, ulong logTimeNs, string source = "Replay", string topic = null)
        {
            var channel = _channels.Get(channelId);
            if (channel == null) return;
            payload ??= Array.Empty<byte>();
            topic ??= channel.Topic;
            foreach (var (clientId, subscriptionId) in _subscriptions.GetSubscribersForChannel(channelId))
            {
                var frame = BinaryEncoding.EncodeServerMessageData(subscriptionId, logTimeNs, payload);
                if (_transport is IPrioritizedFoxgloveTransport prioritized)
                {
                    if (FoxgloveReplayTrace.TryFrame(source, topic, logTimeNs, clientId, subscriptionId, channelId, "data", out var trace))
                        _logger.LogWarning(trace);
                    prioritized.SendDataBinary(clientId, frame);
                }
                else
                {
                    if (FoxgloveReplayTrace.TryFrame(source, topic, logTimeNs, clientId, subscriptionId, channelId, "fallback-control", out var trace))
                        _logger.LogWarning(trace);
                    _transport.SendBinary(clientId, frame);
                }
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
            AdvertiseRegisteredService(id);
            return id;
        }

        /// <summary>
        /// Unregister a service endpoint, unadvertise it from clients, and
        /// remove its connection-graph provider edge.
        /// </summary>
        public bool UnregisterService(uint serviceId)
        {
            var service = _services.GetById(serviceId);
            if (!_services.Unregister(serviceId))
                return false;

            _transport.BroadcastText(JsonConvert.SerializeObject(new UnadvertiseServices
            {
                ServiceIds = new List<uint> { serviceId }
            }));

            if (service != null)
            {
                _graph.RemoveAdvertisedService(service.Name);
                _graph.BroadcastUpdate();
            }

            return true;
        }

        /// <summary>
        /// Advertise an already-registered service and update the connection graph.
        /// Used by runtime-owned service registrations that share this session registry.
        /// </summary>
        internal void AdvertiseRegisteredService(uint serviceId)
        {
            var service = _services.GetById(serviceId);
            if (service == null)
                return;

            var adv = new Protocol.AdvertiseServices { Services = new List<ServiceDescriptor> { service } };
            _transport.BroadcastText(JsonConvert.SerializeObject(adv));
            _graph.AddAdvertisedService(service.Name);
            _graph.BroadcastUpdate();
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
            var effectiveRate = rateHz > 0f && !float.IsNaN(rateHz) && !float.IsInfinity(rateHz)
                ? rateHz
                : 10f;
            var interval = Math.Max(1L, (long)(TimeSpan.TicksPerSecond / (double)effectiveRate));
            if (now - _lastTimeBroadcastTicks < interval)
                return;
            _lastTimeBroadcastTicks = now;

            var frame = BinaryEncoding.EncodeTime(_clock.NowNs);
            BroadcastDataBinary(frame);
        }

        /// <summary>Broadcast droppable data binary frames when the transport supports priority queues.</summary>
        internal void BroadcastDataBinary(byte[] data)
        {
            if (_transport is IPrioritizedFoxgloveTransport prioritized)
                prioritized.BroadcastDataBinary(data);
            else
                _transport.BroadcastBinary(data);
        }

        /// <summary>Broadcast replay time/data frames through the clearable data queue.</summary>
        internal void BroadcastReplayBinary(byte[] data)
        {
            BroadcastDataBinary(data);
        }

        /// <summary>
        /// Return per-client queue headroom for replay history pacing when the
        /// transport exposes managed queue statistics.
        /// </summary>
        internal bool TryGetReplayQueueHeadroom(
            int reserveFrames,
            int reserveBytes,
            out int frameHeadroom,
            out int byteHeadroom)
        {
            frameHeadroom = int.MaxValue;
            byteHeadroom = int.MaxValue;

            if (_transport is not IFoxgloveTransportStatsProvider provider)
                return false;

            var stats = provider.GetStatsSnapshot();
            if (stats == null || !stats.Supported)
                return false;

            if (stats.Clients == null || stats.Clients.Count == 0)
            {
                frameHeadroom = 0;
                byteHeadroom = 0;
                return true;
            }

            if (stats.MaxQueuedFramesPerClient <= 0 || stats.MaxQueuedBytesPerClient <= 0)
                return false;

            var minFrames = int.MaxValue;
            var minBytes = int.MaxValue;
            var frameReserve = Math.Max(0, reserveFrames);
            var byteReserve = Math.Max(0, reserveBytes);
            foreach (var client in stats.Clients)
            {
                minFrames = Math.Min(minFrames, stats.MaxQueuedFramesPerClient - client.QueuedFrames - frameReserve);
                minBytes = Math.Min(minBytes, stats.MaxQueuedBytesPerClient - client.QueuedBytes - byteReserve);
            }

            frameHeadroom = Math.Max(0, minFrames);
            byteHeadroom = Math.Max(0, minBytes);
            return true;
        }

        /// <summary>Enable protobuf encoding support, updating supportedEncodings to include "protobuf".</summary>
        public void EnableProtobuf() => _protobufEnabled = true;

        /// <summary>Whether protobuf encoding support is enabled.</summary>
        public bool IsProtobufEnabled => _protobufEnabled;

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
                SupportedEncodings = _protobufEnabled
                    ? new List<string> { "json", "protobuf" }
                    : new List<string> { "json" },
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

        private static string SerializeServerInfo(ServerInfo info)
        {
            return JsonConvert.SerializeObject(info,
                new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
        }

        private void SendSessionSnapshot(uint clientId)
        {
            _transport.SendText(clientId, SerializeServerInfo(CreateServerInfo()));

            var chs = _channels.GetAll();
            if (chs.Count > 0)
                _transport.SendText(clientId, JsonConvert.SerializeObject(new Advertise { Channels = chs }));

            var svcs = _services.GetAll();
            if (svcs.Count > 0)
                _transport.SendText(clientId, JsonConvert.SerializeObject(new AdvertiseServices { Services = svcs }));
        }

        private void BroadcastSessionSnapshot()
        {
            _transport.BroadcastText(SerializeServerInfo(CreateServerInfo()));

            var chs = _channels.GetAll();
            if (chs.Count > 0)
                _transport.BroadcastText(JsonConvert.SerializeObject(new Advertise { Channels = chs }));

            var svcs = _services.GetAll();
            if (svcs.Count > 0)
                _transport.BroadcastText(JsonConvert.SerializeObject(new AdvertiseServices { Services = svcs }));
        }

        // ── Transport event handlers ──

        /// <summary>
        /// On client connect: send serverInfo (with current capabilities),
        /// advertise all registered channels/services, and seed the connection
        /// graph. Capabilities like PlaybackControl and Assets are conditionally
        /// included based on runtime state.
        /// </summary>
        private void OnClientConnected(uint clientId)
        {
            SendSessionSnapshot(clientId);
            var chs = _channels.GetAll();
            var svcs = _services.GetAll();

            _graph.SeedUnityState(chs, svcs);
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
                    _graph.RemoveSubscribedTopic(clientId, subId, ch.Topic);
            }

            _paramSubs.RemoveClient(clientId);
            _services.RemoveClientCalls(clientId);
            _clientPublish.RemoveClient(clientId);

            _graph.RemoveClient(clientId);
            _graph.BroadcastUpdate();
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
