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
using Unity.FoxgloveSDK.Schemas.Ros2Msg;

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
        private readonly IPrioritizedFoxgloveTransport _prioritizedTransport;
        private readonly IFoxgloveClock _clock;
        private readonly ChannelRegistry _channels = new();
        private readonly SubscriptionRegistry _subscriptions = new();
        private readonly object _subscriberScratchLock = new();
        private readonly List<(uint clientId, uint subscriptionId)> _subscriberScratch = new();
        private readonly ISchemaRegistry _schemaRegistry;
        /// <summary>Optional logger for diagnostics and warnings.</summary>
        private readonly IFoxgloveLogger _logger;

        /// <summary>Whether protobuf encoding support is enabled for channel registration.</summary>
        private bool _protobufEnabled;
        /// <summary>Whether CDR encoding support is enabled for channel registration.</summary>
        private bool _cdrEnabled;

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
        private readonly HashSet<uint> _subscriptionBudgetWarnedClients = new();
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

        /// <summary>
        /// Return whether a registered channel has live subscriber or MCAP recording demand.
        /// </summary>
        public bool HasChannelDemand(uint channelId)
        {
            if (_channels.Get(channelId) == null)
                return false;

            if (_subscriptions.HasSubscribersForChannel(channelId))
                return true;

            return Volatile.Read(ref _recorder) != null;
        }

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
            _prioritizedTransport = _transport as IPrioritizedFoxgloveTransport;
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
            _subscriptionBudgetWarnedClients.Clear();
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
            var previous = _channels.Get(channel.Id);
            if (previous != null && !string.Equals(previous.Topic, channel.Topic, StringComparison.Ordinal))
                _graph.RemoveUnityPublishedTopic(previous.Topic);

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
        /// Supports JSON ("jsonschema"), protobuf ("protobuf"), and explicit
        /// schema-encoding channels such as ROS 2 .msg ("ros2msg" + "cdr").
        /// </summary>
        /// <param name="channelId">Foxglove channel ID.</param>
        /// <param name="topic">Topic name (e.g. "/tf").</param>
        /// <param name="encoding">Message encoding: "json", "protobuf", or "cdr".</param>
        /// <param name="schemaName">Schema name (e.g. "foxglove.FrameTransform").</param>
        /// <param name="schemaEncoding">Optional explicit schema encoding (e.g. "ros2msg").</param>
        public void RegisterSchemaChannel(
            uint channelId,
            string topic,
            string schemaName,
            string encoding = "json",
            string schemaEncoding = null)
        {
            var messageEncoding = string.IsNullOrEmpty(encoding) ? "json" : encoding;
            if (string.Equals(messageEncoding, "cdr", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrEmpty(schemaEncoding))
            {
                throw new InvalidOperationException(
                    "CDR schema channels require an explicit schemaEncoding, such as 'ros2msg'.");
            }

            var expectedSchemaEncoding = string.IsNullOrEmpty(schemaEncoding)
                ? ExpectedSchemaEncodingForMessageEncoding(messageEncoding)
                : schemaEncoding;

            var found = false;
            SchemaEntry entry;
            if (expectedSchemaEncoding != null && _schemaRegistry is IEncodingAwareSchemaRegistry encodingAwareRegistry)
                found = encodingAwareRegistry.TryGetSchema(schemaName, expectedSchemaEncoding, out entry);
            else
                entry = default;

            if (!found && !_schemaRegistry.TryGetSchema(schemaName, out entry))
                throw new InvalidOperationException($"Schema not found: '{schemaName}'.");

            var resolvedSchemaEncoding = entry.Encoding;
            var schema = entry.Content;
            if (expectedSchemaEncoding != null
                && !string.Equals(resolvedSchemaEncoding, expectedSchemaEncoding, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Schema '{schemaName}' has schema encoding '{resolvedSchemaEncoding}', " +
                    $"but message encoding '{messageEncoding}' requires '{expectedSchemaEncoding}'.");
            }

            // For protobuf, the schema content is already base64-encoded FileDescriptorSet bytes.
            // For JSON, the schema content is the raw JSON Schema text.
            RegisterChannel(new AdvertiseChannel
            {
                Id = channelId, Topic = topic,
                Encoding = messageEncoding, SchemaName = entry.Name,
                SchemaEncoding = resolvedSchemaEncoding, Schema = schema
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

        /// <summary>
        /// Register a ROS 2 .msg schema channel with CDR message encoding.
        /// Phase 90 only advertises schemas/channels; payload correctness
        /// requires a CDR writer in a later phase.
        /// </summary>
        public void RegisterRos2MsgSchemaChannel(uint channelId, string topic, string schemaName)
        {
            RegisterSchemaChannel(channelId, topic, schemaName, "cdr", "ros2msg");
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
            lock (_subscriberScratchLock)
            {
                _subscriptions.CopySubscribersForChannel(channelId, _subscriberScratch);
                try
                {
                    foreach (var (clientId, subscriptionId) in _subscriberScratch)
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
                    _subscriberScratch.Clear();
                }
            }
        }

        /// <summary>Publish a validated ROS 2 CDR payload using the current clock time.</summary>
        public void PublishRos2Cdr(uint channelId, byte[] payload) => PublishRos2Cdr(channelId, payload, _clock.NowNs);

        /// <summary>Publish a validated ROS 2 CDR payload with an explicit log timestamp.</summary>
        public void PublishRos2Cdr(uint channelId, byte[] payload, ulong logTimeNs)
        {
            Ros2CdrPayloadValidator.Validate(payload);
            Publish(channelId, payload, logTimeNs);
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
            lock (_subscriberScratchLock)
            {
                _subscriptions.CopySubscribersForChannel(channelId, _subscriberScratch);
                try
                {
                    foreach (var (clientId, subscriptionId) in _subscriberScratch)
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
                    _subscriberScratch.Clear();
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

        /// <summary>Enable CDR encoding support, updating supportedEncodings to include "cdr".</summary>
        public void EnableCdr() => _cdrEnabled = true;

        /// <summary>Whether CDR encoding support is enabled.</summary>
        public bool IsCdrEnabled => _cdrEnabled;

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
            var supportedEncodings = new List<string> { "json" };
            if (_protobufEnabled)
                supportedEncodings.Add("protobuf");
            if (_cdrEnabled)
                supportedEncodings.Add("cdr");

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

            var chs = channels ?? _channels.GetAll();
            if (chs.Count > 0)
                _transport.SendText(clientId, JsonConvert.SerializeObject(new Advertise
                {
                    Channels = chs as List<AdvertiseChannel> ?? new List<AdvertiseChannel>(chs)
                }));

            var svcs = services ?? _services.GetAll();
            if (svcs.Count > 0)
                _transport.SendText(clientId, JsonConvert.SerializeObject(new AdvertiseServices
                {
                    Services = svcs as List<ServiceDescriptor> ?? new List<ServiceDescriptor>(svcs)
                }));
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
            var chs = _channels.GetAll();
            var svcs = _services.GetAll();

            SendSessionSnapshot(clientId, chs, svcs);
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
            _subscriptionBudgetWarnedClients.Remove(clientId);

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
