// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core
// Purpose: Top-level SDK entry point that owns transport, session, clock,
// parameter store, service registry, asset registry, recording controller,
// and replay controller. Delegates public API to these managed components.

using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Transport;
using Unity.FoxgloveSDK.Schemas;
using static Unity.FoxgloveSDK.Transport.TransportStatsSnapshot;

namespace Unity.FoxgloveSDK.Core
{
    /// <summary>
    /// Top-level SDK runtime. Owns the WebSocket transport, session,
    /// playback clock, schema registry, parameter store, service
    /// registry, asset registry, recording controller, and replay
    /// controller. All public SDK workflows (start/stop, channel
    /// registration, publish, recording, replay, service drain) flow
    /// through this class.
    ///
    /// <para>Default constructor wires a ManagedWsBackend, SystemClock,
    /// DefaultSchemaRegistry, and ConsoleLogger. Use the parameterized
    /// constructor to inject custom backends for testing.</para>
    ///
    /// <para>Call <c>Tick</c> periodically (every frame from Unity) to
    /// drain service calls, tick replay, and broadcast time.</para>
    /// </summary>
    public partial class FoxgloveRuntime : IDisposable, IRuntimeContext, IClientReplayBackfillContext, IClientReplayDisconnectContext
    {
        /// <summary>
        /// Active session; null before Start or after Stop. Runtime lifecycle APIs
        /// are owner-thread operations and must not be called concurrently.
        /// </summary>
        private FoxgloveSession _session;
        // A session is removed from the public active slot before teardown so
        // callbacks cannot observe it as live. Keep a private owner reference
        // when one of its cleanup steps fails, allowing a later Stop/Dispose to
        // retry without leaking its transport handlers.
        private FoxgloveSession _sessionPendingCleanup;
        private readonly IFoxgloveTransport _transport;
        private readonly IFoxgloveClock _wallClock;
        private readonly PlaybackClock _playbackClock;
        private readonly ISchemaRegistry _schemaRegistry;
        private readonly ISchemaRegistry _publicSchemaRegistry;
        private readonly IFoxgloveLogger _logger;
        private bool _protobufSchemasRegistered;
        private readonly HashSet<string> _additionalMessageEncodings =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly string[] _singleParameterBroadcastName = new string[1];
        // Runtime-owned start-time routing policy. Like parameters and services,
        // these survive Stop/Start and are re-applied to the next session; Stop
        // deliberately does not clear them.
        private ISinkChannelFilter _liveWebSocketChannelFilter;
        private ISinkChannelFilter _mcapRecordingChannelFilter;
        private IFoxgloveMirrorSink _mirrorSink;

        // Runtime-owned definitions survive Stop/Start cycles so
        // parameters and services are re-advertised on restart.
        /// <summary>Runtime-owned parameter store; survives Stop/Start cycles.</summary>
        private readonly FoxgloveParameterStore _parameters;
        /// <summary>Runtime-owned service registry; survives Stop/Start cycles.</summary>
        private readonly FoxgloveServiceRegistry _services = new();
        /// <summary>Runtime-owned asset registry for fetchAsset capability.</summary>
        private readonly FoxgloveAssetRegistry _assets;

        /// <summary>Recording lifecycle controller.</summary>
        private readonly RecordingController _recording;
        /// <summary>Replay lifecycle controller.</summary>
        private readonly ReplayController _replay;
        private readonly ReplayOrchestrator _replayOrchestrator;
        private readonly TickCoordinator _tickCoordinator;
        private readonly ExternalReplayCursorController _externalReplayCursorController = new();
        private readonly RuntimeStopCleanupState _stopCleanup = new RuntimeStopCleanupState();
        private int _disposed;
        private int _disposeRequested;
        private int _disposing;
        private bool _stopCleanupComplete = true;
        // Replay itself may coexist with live output. The manager explicitly
        // enables this gate only for its "Disable Live Publishers" policy.
        private bool _suppressLivePublishersForReplay;
        private bool _parametersCleared;
        private bool _servicesCleared;
        private bool _recordingDisposed;
        private bool _replayDisposed;
        private bool _transportDisposed;
        private bool _stopped = true;

        /// <summary>Current nanosecond timestamp from the playback clock.</summary>
        public ulong NowNs => _playbackClock.NowNs;

        /// <summary>
        /// Default constructor. Wires <c>ManagedWsBackend</c>, <c>SystemClock</c>,
        /// <c>DefaultSchemaRegistry</c>, and optional logger.
        /// </summary>
        public FoxgloveRuntime(IFoxgloveLogger logger = null)
            : this(new ManagedWsBackend(logger), new SystemClock(), new DefaultSchemaRegistry(), logger) { }

        /// <summary>Add a browser origin to the transport's CSWSH allowlist. No-op if unsupported.</summary>
        public void AddAllowedOrigin(string origin)
        {
            ThrowIfSessionCleanupPending();
            if (_transport is IOriginGuardedFoxgloveTransport originGuard)
                originGuard.AddAllowedOrigin(origin);
        }

        /// <summary>Clear the transport's browser origin allowlist, blocking all browser clients.</summary>
        public void ClearAllowedOrigins()
        {
            ThrowIfSessionCleanupPending();
            if (_transport is IOriginGuardedFoxgloveTransport originGuard)
                originGuard.ClearAllowedOrigins();
        }

        /// <summary>Full-injection constructor for custom transport, clock, schema registry, and logger.</summary>
        public FoxgloveRuntime(IFoxgloveTransport transport, IFoxgloveClock clock, ISchemaRegistry schemaRegistry, IFoxgloveLogger logger = null)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _wallClock = clock ?? new SystemClock();
            _playbackClock = new PlaybackClock(_wallClock);
            _schemaRegistry = schemaRegistry ?? throw new ArgumentNullException(nameof(schemaRegistry));
            _logger = logger ?? new ConsoleLogger();
            _parameters = new FoxgloveParameterStore(_logger, () => _sessionPendingCleanup == null);
            _assets = new FoxgloveAssetRegistry(() => _sessionPendingCleanup == null);
            _publicSchemaRegistry = new GuardedSchemaRegistry(
                _schemaRegistry,
                () => _sessionPendingCleanup == null);
            FoxgloveSchemaDefinitions.RegisterCoreSchemas(_schemaRegistry);
            TryRegisterProtobufSchemas();
            _recording = new RecordingController(_logger, _playbackClock, _schemaRegistry);
            _replay = new ReplayController(_logger, _recording, _playbackClock, _schemaRegistry);
            _replayOrchestrator = new ReplayOrchestrator(_logger);
            _tickCoordinator = new TickCoordinator(
                new ReplaySnapshotStateMachine(ResolveReplaySnapshotCapacity(_transport)));
        }

        private static int ResolveReplaySnapshotCapacity(IFoxgloveTransport transport)
        {
            if (transport is IFoxgloveTransportCapacityProvider capacityProvider
                && capacityProvider.MaxClients > 0)
                return capacityProvider.MaxClients;
            if (transport is IFoxgloveTransportStatsProvider provider)
            {
                var snapshot = provider.GetStatsSnapshot();
                if (snapshot != null && snapshot.MaxClients > 0)
                    return snapshot.MaxClients;
            }

            return int.MaxValue;
        }

        /// <summary>Active session; null before Start or after Stop.</summary>
        public FoxgloveSession Session => _session;
        /// <summary>Session that still owns teardown callbacks after it leaves the active slot.</summary>
        internal FoxgloveSession CleanupSession => _session ?? _sessionPendingCleanup;
        /// <summary>Whether a retired session still requires a cleanup retry.</summary>
        internal bool HasPendingSessionCleanup => _sessionPendingCleanup != null;
        /// <summary>Whether the session is currently running.</summary>
        public bool IsRunning => _session?.IsRunning ?? false;
        /// <summary>Whether a registered channel has live subscriber or MCAP recording demand.</summary>
        public bool HasChannelDemand(uint channelId) => _session?.HasChannelDemand(channelId) ?? false;
        /// <summary>Schema registry used by this runtime.</summary>
        public ISchemaRegistry Schemas => _publicSchemaRegistry;
        /// <summary>
        /// Adds one Provider-owned message encoding to future session
        /// serverInfo snapshots. Configuration is frozen while running.
        /// </summary>
        public void EnableMessageEncoding(string encoding)
        {
            ThrowIfSessionCleanupPending();
            if (_session != null)
                throw new InvalidOperationException(
                    "Message encodings must be configured before the runtime starts.");
            if (string.IsNullOrWhiteSpace(encoding))
                throw new ArgumentException("Message encoding cannot be empty.", nameof(encoding));
            _additionalMessageEncodings.Add(encoding.Trim().ToLowerInvariant());
        }
        /// <summary>Runtime-owned parameter store.</summary>
        public FoxgloveParameterStore Parameters => _parameters;

        /// <summary>
        /// Set an optional per-sink channel filter. Null allows all channels for the sink.
        /// Per-sink filters are a start-time routing policy, not a runtime hot-swap:
        /// they must be configured before the session starts (while
        /// <c>_session == null</c>) so the session runs under a fixed policy.
        /// Like parameters and services, configured filters persist across
        /// Stop/Start cycles and are re-applied to the next session.
        /// </summary>
        /// <exception cref="InvalidOperationException">The session is already started.</exception>
        public void SetSinkChannelFilter(FoxgloveSinkKind sink, ISinkChannelFilter filter)
        {
            ThrowIfSessionCleanupPending();
            if (_session != null)
                throw new InvalidOperationException(
                    "Sink channel filters must be configured before the session starts; " +
                    "stop the server before changing a per-sink filter.");

            switch (sink)
            {
                case FoxgloveSinkKind.LiveWebSocket:
                    Volatile.Write(ref _liveWebSocketChannelFilter, filter);
                    break;
                case FoxgloveSinkKind.McapRecording:
                    Volatile.Write(ref _mcapRecordingChannelFilter, filter);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(sink), sink, "Unknown Foxglove sink kind.");
            }
        }

        /// <summary>Return the configured per-sink channel filter, or null when the sink allows all channels.</summary>
        public ISinkChannelFilter GetSinkChannelFilter(FoxgloveSinkKind sink)
        {
            return sink switch
            {
                FoxgloveSinkKind.LiveWebSocket => Volatile.Read(ref _liveWebSocketChannelFilter),
                FoxgloveSinkKind.McapRecording => Volatile.Read(ref _mcapRecordingChannelFilter),
                _ => throw new ArgumentOutOfRangeException(nameof(sink), sink, "Unknown Foxglove sink kind.")
            };
        }

        /// <summary>Attach or detach an optional live-data mirror sink.</summary>
        public void SetMirrorSink(IFoxgloveMirrorSink sink)
        {
            ThrowIfSessionCleanupPending();
            Volatile.Write(ref _mirrorSink, sink);
            _session?.SetMirrorSink(sink);
        }

        /// <summary>Return the currently configured mirror sink, or null when disabled.</summary>
        public IFoxgloveMirrorSink GetMirrorSink() => Volatile.Read(ref _mirrorSink);

        /// <summary>Register a named parameter. Can be called before Start; stored for later advertisement.</summary>
        public void RegisterParameter(string name, JToken value, string type, bool writable)
        {
            ThrowIfSessionCleanupPending();
            _parameters.Register(name, value, type, writable);
        }

        /// <summary>Register a parameter with a lease that owns only this registration.</summary>
        public FoxgloveParameterStore.ParameterRegistration RegisterParameterOwned(
            string name, JToken value, string type, bool writable)
        {
            ThrowIfSessionCleanupPending();
            return _parameters.RegisterOwned(name, value, type, writable);
        }

        /// <summary>Unregister a named parameter. Safe no-op for unknown names.</summary>
        public bool UnregisterParameter(string name)
        {
            ThrowIfSessionCleanupPending();
            return _parameters.Unregister(name);
        }

        /// <summary>
        /// Update a writable runtime-owned parameter and notify Foxglove clients
        /// subscribed to parameter updates.
        /// </summary>
        public bool TrySetParameter(string name, JToken value)
        {
            ThrowIfSessionCleanupPending();
            if (value == null || value.Type == JTokenType.Null)
                return false;
            if (!_parameters.TrySetFromClient(name, value))
                return false;
            _singleParameterBroadcastName[0] = name;
            try
            {
                _session?.BroadcastParameterValues(_singleParameterBroadcastName);
            }
            finally
            {
                _singleParameterBroadcastName[0] = null;
            }
            return true;
        }

        /// <summary>Snapshot of currently advertised services.</summary>
        public IReadOnlyCollection<ServiceDescriptor> GetServicesSnapshot() => _services.GetAll();

        /// <summary>
        /// Register a service and re-advertise to connected clients.
        /// <para>If a <c>handler</c> is provided, calls are dispatched to it during drain.</para>
        /// </summary>
        public uint RegisterService(ServiceDescriptor descriptor, Func<JToken, JToken> handler = null)
        {
            ThrowIfSessionCleanupPending();
            // Once a session is active, let it stage the registry mutation and
            // client-visible advertisement under one lifecycle lock. This
            // prevents a request from observing the descriptor between those
            // two operations.
            if (_session != null)
                return _session.RegisterServiceFromRuntime(descriptor, handler);

            var id = handler != null
                ? _services.Register(descriptor, handler)
                : _services.Register(descriptor);
            // Before Start there are no connected clients; the next session
            // snapshot advertises the retained runtime-owned definition.
            return id;
        }

        /// <summary>
        /// Unregister a service and notify connected clients when the runtime
        /// is currently serving a session.
        /// </summary>
        public bool UnregisterService(uint serviceId)
        {
            ThrowIfSessionCleanupPending();
            if (serviceId == 0)
                return false;

            return _session != null
                ? _session.UnregisterService(serviceId)
                : _services.Unregister(serviceId);
        }

        /// <summary>
        /// Removes a Manager-owned service while the runtime is completing a
        /// retired-session cleanup epoch. This bypasses the public mutation
        /// guard but still routes through the active session when one exists so
        /// client-visible unadvertise ordering is preserved.
        /// </summary>
        internal bool UnregisterServiceDuringCleanup(uint serviceId)
        {
            if (serviceId == 0)
                return false;

            return _session != null
                ? _session.UnregisterService(serviceId)
                : _services.Unregister(serviceId);
        }

    }

    /// <summary>Shared executable generation predicate for main-thread client events.</summary>
    internal static class ClientEventGenerationGate
    {
        internal static bool IsCurrent(ulong eventGeneration, ulong currentGeneration)
            => eventGeneration == currentGeneration;
    }
}
