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
        private readonly IFoxgloveTransport _transport;
        private readonly IPrioritizedFoxgloveTransport _prioritizedTransport;
        private readonly IFoxgloveClock _clock;
        private readonly object _channelLifecycleLock = new object();
        // Serializes service registry changes with client-visible advertise /
        // unadvertise frames. Service calls take the same lock while they are
        // admitted so a request cannot race a staged removal.
        private readonly object _serviceLifecycleLock = new object();
        private readonly ChannelRegistry _channels = new();
        private readonly HashSet<uint> _recordingOnlyChannels = new();
        private readonly SubscriptionRegistry _subscriptions = new();
        private readonly object _subscriberScratchLock = new();
        private readonly List<(uint clientId, uint subscriptionId)> _subscriberScratch = new();
        private readonly object _paramSubScratchLock = new();
        private readonly List<uint> _paramSubScratch = new();
        private readonly object _parameterBroadcastScratchLock = new();
        private readonly HashSet<string> _parameterBroadcastSeen = new();
        private readonly List<string> _parameterBroadcastNames = new();
        private readonly List<FoxgloveServiceCall> _pendingServiceCallsScratch = new();
        private readonly List<FoxgloveServiceCall> _completedServiceCallsScratch = new();
        private readonly ISchemaRegistry _schemaRegistry;
        private static readonly ThreadLocal<JsonSerializer> JsonSerializerCache =
            new ThreadLocal<JsonSerializer>(JsonSerializer.CreateDefault);
        [ThreadStatic]
        private static List<(uint clientId, uint subscriptionId)> s_publishSubscriberScratch;
        [ThreadStatic]
        private static List<(uint subscriptionId, uint channelId)> s_clientReplaySubscriptionScratch;
        [ThreadStatic]
        private static List<AdvertiseChannel> s_singleAdvertiseChannels;
        [ThreadStatic]
        private static List<uint> s_singleUnadvertiseChannelIds;
        [ThreadStatic]
        private static MemoryStream s_jsonPublishStream;
        /// <summary>Optional logger for diagnostics and warnings.</summary>
        private readonly IFoxgloveLogger _logger;

        /// <summary>Whether protobuf encoding support is enabled for channel registration.</summary>
        private bool _protobufEnabled;
        private readonly HashSet<string> _additionalMessageEncodings =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly object _encodingConfigurationLock = new object();
        private bool _encodingConfigurationFrozen;

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
        private readonly SessionTimeBroadcaster _timeBroadcaster = new();
        private readonly SessionChannelFilter _channelFilter = new();
        private readonly HashSet<uint> _subscriptionBudgetWarnedClients = new();
        private readonly object _subscriptionBudgetWarnedClientsLock = new();
        /// <summary>Maximum queued playback control requests awaiting the runtime owner tick.</summary>
        internal const int MaxPendingPlaybackControls = SessionPlaybackHandler.MaxPendingPlaybackControls;
        /// <summary>Raised when a client-published binary message is received.</summary>
        public event Action<uint, uint, string, byte[]> OnClientMessage;
        /// <summary>Raised with the client-advertised channel encoding for inbound routing.</summary>
        public event Action<uint, uint, string, string, byte[]> OnClientMessageWithEncoding;

        private McapRecorder _recorder;
        private IFoxgloveMirrorSink _mirrorSink;
        private bool _clientConnectedSubscribed;
        private bool _clientDisconnectedSubscribed;
        private bool _textReceivedSubscribed;
        private bool _binaryReceivedSubscribed;
        private bool _disposeStopComplete;
        private bool _disposeClientConnectedComplete;
        private bool _disposeClientDisconnectedComplete;
        private bool _disposeTextReceivedComplete;
        private bool _disposeBinaryReceivedComplete;
        private int _disposed;

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
        internal void SetRecorder(McapRecorder r)
        {
            lock (_channelLifecycleLock)
            {
                Volatile.Read(ref _recorder)?.ClearServerChannelAdmissionRejections();
                r?.ClearServerChannelAdmissionRejections();
                Volatile.Write(ref _recorder, r);
                if (r == null)
                    return;

                try
                {
                    foreach (var channel in _channels.GetAll())
                    {
                        if (AllowMcapRecording(channel))
                            EnsureRecorderChannel(r, channel);
                    }
                }
                catch
                {
                    r.ClearServerChannelAdmissionRejections();
                    Volatile.Write(ref _recorder, null);
                    throw;
                }
            }
        }

        /// <summary>Attach an optional mirror sink and seed it with current server-side channels.</summary>
        internal void SetMirrorSink(IFoxgloveMirrorSink sink, bool replayExistingChannels = true)
        {
            lock (_channelLifecycleLock)
            {
                var previous = Interlocked.Exchange(ref _mirrorSink, sink);
                if (previous != null && !ReferenceEquals(previous, sink))
                {
                    foreach (var channel in _channels.GetAll())
                    {
                        if (!_recordingOnlyChannels.Contains(channel.Id))
                            TryUnregisterMirrorChannel(previous, channel.Id);
                    }
                }

                if (sink == null || !replayExistingChannels)
                    return;

                foreach (var channel in _channels.GetAll())
                {
                    if (!_recordingOnlyChannels.Contains(channel.Id))
                        TryRegisterMirrorChannel(sink, channel);
                }
            }
        }

        /// <summary>Set an optional per-sink channel filter. Null allows all channels for the sink.</summary>
        internal void SetSinkChannelFilter(FoxgloveSinkKind sink, ISinkChannelFilter filter)
        {
            lock (_channelLifecycleLock)
            {
                _channelFilter.SetSinkChannelFilter(sink, filter);
                if (sink == FoxgloveSinkKind.McapRecording)
                    Volatile.Read(ref _recorder)?.ClearServerChannelAdmissionRejections();
            }
        }

        /// <summary>Return the configured per-sink channel filter, or null when the sink allows all channels.</summary>
        internal ISinkChannelFilter GetSinkChannelFilter(FoxgloveSinkKind sink)
            => _channelFilter.GetSinkChannelFilter(sink);

        /// <summary>
        /// Return whether a registered channel has live subscriber or MCAP recording demand.
        /// </summary>
        public bool HasChannelDemand(uint channelId)
        {
            lock (_channelLifecycleLock)
            {
                var channel = _channels.Get(channelId);
                if (channel == null)
                    return false;

                if (_subscriptions.HasSubscribersForChannel(channelId) && AllowLiveWebSocket(channel))
                    return true;

                var recorder = Volatile.Read(ref _recorder);
                if (recorder != null
                    && AllowMcapRecording(channel)
                    && recorder.HasServerChannel(channelId))
                    return true;

                if (_recordingOnlyChannels.Contains(channelId))
                    return false;

                return HasMirrorDemand(channel);
            }
        }

        /// <summary>
        /// Return whether an MCAP-only channel has an attached recorder that
        /// accepts it. Live subscribers and mirrors never create recording
        /// demand for these hidden channels.
        /// </summary>
        internal bool HasRecordingDemand(uint channelId)
        {
            lock (_channelLifecycleLock)
            {
                if (!_recordingOnlyChannels.Contains(channelId))
                    return false;

                var channel = _channels.Get(channelId);
                var recorder = Volatile.Read(ref _recorder);
                return channel != null
                       && recorder != null
                       && AllowMcapRecording(channel)
                       && recorder.HasServerChannel(channelId);
            }
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
            _parameters = paramStore ?? new FoxgloveParameterStore(_logger);
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
                (clientId, chId, topic, encoding, payload) =>
                {
                    OnClientMessage?.Invoke(clientId, chId, topic, payload);
                    OnClientMessageWithEncoding?.Invoke(clientId, chId, topic, encoding, payload);
                },
                IsClientPublishEncodingSupported);
            _assets = new SessionAssetHandler(() => Volatile.Read(ref _runtime), _transport);

            try
            {
                // Mark ownership before each add. A custom event accessor may add
                // the delegate and then throw, in which case constructor rollback
                // must still attempt to detach it.
                _clientConnectedSubscribed = true;
                _transport.OnClientConnected += OnClientConnected;
                _clientDisconnectedSubscribed = true;
                _transport.OnClientDisconnected += OnClientDisconnected;
                _textReceivedSubscribed = true;
                _transport.OnTextReceived += OnClientText;
                _binaryReceivedSubscribed = true;
                _transport.OnBinaryReceived += OnClientBinary;
            }
            catch
            {
                RollBackConstructorSubscriptions();
                throw;
            }
        }

    }
}
