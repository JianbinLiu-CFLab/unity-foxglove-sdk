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
    public partial class FoxgloveRuntime : IDisposable, IRuntimeContext
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
            _recording = new RecordingController(_logger, _playbackClock);
            _replay = new ReplayController(_logger, _recording, _playbackClock);
            _replayOrchestrator = new ReplayOrchestrator(_logger);
            _tickCoordinator = new TickCoordinator(new ReplaySnapshotStateMachine());
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

        /// <summary>
        /// Start the WebSocket server. Creates a new FoxgloveSession,
        /// attaches recording/replay controllers, and wires replay
        /// message forwarding. Protobuf encoding is enabled automatically
        /// when the proto assembly is available.
        /// </summary>
        public void Start(string name, string host = "127.0.0.1", int port = 8765)
        {
            if (Volatile.Read(ref _disposeRequested) != 0)
                throw new ObjectDisposedException(nameof(FoxgloveRuntime));
            if (_session != null)
                throw new InvalidOperationException("Session already started. Call Stop() first.");

            // Do not overlap a new session with a retired session or an attached
            // forwarder whose cleanup failed. Best-effort replay-history cleanup
            // may report a failure without poisoning the next session epoch.
            if (_sessionPendingCleanup != null || !_stopCleanup.IsReadyForStart)
            {
                ExceptionDispatchInfo cleanupFailure = null;
                RunStopCleanup(ref cleanupFailure);
                if (!_stopCleanup.IsReadyForStart)
                {
                    if (cleanupFailure != null)
                        cleanupFailure.Throw();
                    throw new InvalidOperationException(
                        "The previous runtime session still owns cleanup resources.");
                }
                if (cleanupFailure != null)
                    _logger.LogWarning(
                        $"Ignoring non-critical Stop cleanup failure before restart: {cleanupFailure.SourceException.Message}");
            }

            FoxgloveSession session = null;
            try
            {
                // Begin the cleanup epoch before the factory subscribes the
                // transport. Factory failures therefore enter the same rollback
                // state as transport Start failures.
                _stopCleanup.Reset();
                _stopCleanupComplete = false;
                session = SessionFactory.Create(
                    name,
                    _transport, _playbackClock, _schemaRegistry, _logger,
                    _parameters, _services, _recording,
                    _protobufSchemasRegistered, _additionalMessageEncodings,
                    this,
                    Volatile.Read(ref _liveWebSocketChannelFilter),
                    Volatile.Read(ref _mcapRecordingChannelFilter),
                    Volatile.Read(ref _mirrorSink));
                session.Start(host, port);
                _session = session;
                ClearReplaySuppressionWarnings();
                _replayOrchestrator.Attach(_replay, session);
                _stopped = false;
            }
            catch (Exception)
            {
                // Run every cleanup step independently. The original Start
                // failure remains primary; cleanup failures are retained in
                // the per-step state for a later Stop/Dispose retry.
                ExceptionDispatchInfo cleanupFailure = null;
                RunStopCleanup(ref cleanupFailure, session);
                if (cleanupFailure != null)
                    _logger.LogWarning(
                        $"Startup cleanup was incomplete; preserving the original Start exception: {cleanupFailure.SourceException.Message}");
                throw;
            }
        }

        /// <summary>Fires when the replay engine forwards a message (e.g. for UI update).</summary>
        public event Action<string, byte[]> OnReplayMessage
        {
            add => _replayOrchestrator.OnReplayMessage += value;
            remove => _replayOrchestrator.OnReplayMessage -= value;
        }

        /// <summary>Fires when replay data is forwarded with channel, schema, and log-time context.</summary>
        public event Action<ReplayMessageContext> OnReplayMessageContext
        {
            add => _replayOrchestrator.OnReplayMessageContext += value;
            remove => _replayOrchestrator.OnReplayMessageContext -= value;
        }

        /// <summary>Fires after a replay batch has been forwarded to scene listeners.</summary>
        public event Action<ReplayBatchContext> OnReplayBatchCompleted
        {
            add => _replayOrchestrator.OnReplayBatchCompleted += value;
            remove => _replayOrchestrator.OnReplayBatchCompleted -= value;
        }

        /// <summary>Test-only hook to fire replay without loading an MCAP file.</summary>
        internal void FireReplayForTests(string topic, byte[] data)
            => _replay.FireForTests(topic, data);

        /// <summary>Test-only hook to fire context-rich replay without loading an MCAP file.</summary>
        internal void FireReplayContextForTests(ReplayMessageContext context)
            => _replay.FireContextForTests(context);

        /// <summary>
        /// Stop the server, detach recording/replay, and dispose the session.
        /// </summary>
        public void Stop()
        {
            if (_stopped && _session == null)
            {
                if (_sessionPendingCleanup == null && _stopCleanup.IsResourceCleanupComplete)
                    return;
            }

            ExceptionDispatchInfo firstFailure = null;
            RunStopCleanup(ref firstFailure);
            firstFailure?.Throw();
        }

        /// <summary>
        /// Runs each Stop action once per cleanup epoch and retains only failed
        /// steps for later retry. The active session is retired before any
        /// callback can observe it as running.
        /// </summary>
        private void RunStopCleanup(
            ref ExceptionDispatchInfo firstFailure,
            FoxgloveSession startupSession = null)
        {
            _stopped = true;
            _stopCleanup.TryCleanup(
                RuntimeStopCleanupStep.ReplaySuppressionWarnings,
                ClearReplaySuppressionWarnings,
                ref firstFailure);
            _stopCleanup.TryCleanup(
                RuntimeStopCleanupStep.ReplaySnapshot,
                _tickCoordinator.ClearPendingReplaySnapshot,
                ref firstFailure);
            _stopCleanup.TryCleanup(
                RuntimeStopCleanupStep.ReplaySceneSnapshot,
                _tickCoordinator.ClearPendingReplaySceneSnapshot,
                ref firstFailure);
            _stopCleanup.TryCleanup(
                RuntimeStopCleanupStep.ReplayPanelHistory,
                _replay.CancelPanelHistory,
                ref firstFailure);
            _stopCleanup.TryCleanup(
                RuntimeStopCleanupStep.ReplayOrchestrator,
                () => _replayOrchestrator.Detach(_replay),
                ref firstFailure);

            var session = _session ?? _sessionPendingCleanup ?? startupSession;
            _session = null;
            if (session == null)
                _stopCleanup.MarkComplete(RuntimeStopCleanupStep.Session);
            else
                _sessionPendingCleanup = session;

            _stopCleanup.TryCleanup(
                RuntimeStopCleanupStep.Recording,
                _recording.DetachFromSession,
                ref firstFailure);
            _stopCleanup.TryCleanup(
                RuntimeStopCleanupStep.Session,
                () => session?.Dispose(),
                ref firstFailure);
            if (_stopCleanup.IsCompleted(RuntimeStopCleanupStep.Session))
                _sessionPendingCleanup = null;

            // Resource ownership is represented by the required per-step
            // latches, not by one failure result from the current invocation.
            _stopCleanupComplete = _stopCleanup.IsResourceCleanupComplete;
        }

        // ── Channel API ──

        /// <summary>Register an advertise channel on the session.</summary>
        public void RegisterChannel(AdvertiseChannel channel)
        {
            if (_session == null) throw new InvalidOperationException("Session not started.");
            if (ReplayEnabled)
            {
                WarnReplaySuppressed(nameof(RegisterChannel), channel?.Id);
                return;
            }

            _session.RegisterChannel(channel);
        }

        /// <summary>Register a channel visible only to the attached MCAP recorder.</summary>
        internal void RegisterRecordingOnlyChannel(AdvertiseChannel channel)
        {
            if (_session == null) throw new InvalidOperationException("Session not started.");
            if (ReplayEnabled)
            {
                WarnReplaySuppressed(nameof(RegisterRecordingOnlyChannel), channel?.Id);
                return;
            }

            _session.RegisterRecordingOnlyChannel(channel);
        }

        /// <summary>Whether an MCAP recorder currently accepts this hidden channel.</summary>
        public bool HasRecordingDemand(uint channelId)
            => !ReplayEnabled
               && _session != null
               && _session.HasRecordingDemand(channelId);

        /// <summary>Unregister a channel by its numeric ID.</summary>
        public void UnregisterChannel(uint channelId)
        {
            if (_session == null) throw new InvalidOperationException("Session not started.");
            _session.UnregisterChannel(channelId);
        }

        /// <summary>Publish raw bytes to a channel. Timestamp is taken from the clock.</summary>
        public void Publish(uint channelId, byte[] payload)
        {
            if (_session == null) throw new InvalidOperationException("Session not started.");
            if (ReplayEnabled)
            {
                WarnReplaySuppressed(nameof(Publish), channelId);
                return;
            }

            _session.Publish(channelId, payload);
        }

        /// <summary>Publish raw bytes with an explicit nanosecond timestamp.</summary>
        public void Publish(uint channelId, byte[] payload, ulong logTimeNs)
        {
            if (_session == null) throw new InvalidOperationException("Session not started.");
            if (ReplayEnabled)
            {
                WarnReplaySuppressed(nameof(Publish), channelId);
                return;
            }

            _session.Publish(channelId, payload, logTimeNs);
        }

        /// <summary>Publish raw bytes only to a previously hidden MCAP channel.</summary>
        public bool PublishRecordingOnly(uint channelId, byte[] payload, ulong logTimeNs)
        {
            if (_session == null || ReplayEnabled || !_session.HasRecordingDemand(channelId))
                return false;
            _session.Publish(channelId, payload, logTimeNs);
            return true;
        }

        /// <summary>Register a schema channel on the session with the given encoding (default "json").</summary>
        public void RegisterSchemaChannel(
            uint channelId,
            string topic,
            string schemaName,
            string encoding = "json",
            string schemaEncoding = null)
        {
            if (_session == null) throw new InvalidOperationException("Session not started.");
            if (ReplayEnabled)
            {
                WarnReplaySuppressed(nameof(RegisterSchemaChannel), channelId);
                return;
            }

            _session.RegisterSchemaChannel(channelId, topic, schemaName, encoding, schemaEncoding);
        }

        /// <summary>Serialize and publish a JSON message. Timestamp is taken from the clock.</summary>
        public void PublishJson(uint channelId, object message)
        {
            if (_session == null) throw new InvalidOperationException("Session not started.");
            if (ReplayEnabled)
            {
                WarnReplaySuppressed(nameof(PublishJson), channelId);
                return;
            }

            _session.PublishJson(channelId, message);
        }

        /// <summary>Serialize and publish a JSON message with an explicit nanosecond timestamp.</summary>
        public void PublishJson(uint channelId, object message, ulong logTimeNs)
        {
            if (_session == null) throw new InvalidOperationException("Session not started.");
            if (ReplayEnabled)
            {
                WarnReplaySuppressed(nameof(PublishJson), channelId);
                return;
            }

            _session.PublishJson(channelId, message, logTimeNs);
        }

        /// <summary>
        /// Publish an official Foxglove diagnostics status message to connected clients.
        /// </summary>
        /// <param name="level">Status severity encoded with official numeric values.</param>
        /// <param name="message">Human-readable diagnostic message.</param>
        /// <param name="id">Optional stable status identifier for later removal.</param>
        public void PublishStatus(FoxgloveStatusLevel level, string message, string id = null)
        {
            if (_session == null) throw new InvalidOperationException("Session not started.");
            _session.PublishStatus(level, message, id);
        }

        /// <summary>
        /// Remove one or more official Foxglove diagnostics status messages.
        /// </summary>
        /// <param name="ids">Status identifiers to remove.</param>
        public void RemoveStatus(params string[] ids)
        {
            if (_session == null) throw new InvalidOperationException("Session not started.");
            _session.RemoveStatus(ids);
        }

        /// <summary>
        /// Drain pending service calls on the calling thread.
        /// Must be called on the Unity main thread if handlers touch Unity objects.
        /// </summary>
        public void DrainServiceCalls() => _session?.DrainServiceCalls();

        // ── Assets ──

        /// <summary>Register a local file system root for fetchAsset under the given URI prefix.</summary>
        public void RegisterAssetRoot(string uriPrefix, string localRoot, long maxBytes = 16 * 1024 * 1024)
        {
            ThrowIfSessionCleanupPending();
            _assets.RegisterRoot(uriPrefix, localRoot, maxBytes);
        }

        /// <summary>Asset registry for fetchAsset capability.</summary>
        public FoxgloveAssetRegistry Assets => _assets;

        // ── Recording (delegated) ──

        /// <summary>Whether recording is enabled.</summary>
        public bool RecordingEnabled => _recording.IsEnabled;

        /// <summary>Enable MCAP recording for the next session start.</summary>
        public void EnableRecording(string filePath, int chunkSizeBytes = McapRecorder.DefaultChunkSizeBytes, string compression = "", string coordinateMode = "")
        {
            ThrowIfSessionCleanupPending();
            _recording.Enable(filePath, chunkSizeBytes, compression, coordinateMode);
        }

        /// <summary>Enable MCAP recording with explicit output and input coordinate conventions.</summary>
        public void EnableRecording(
            string filePath,
            int chunkSizeBytes,
            string compression,
            string outputCoordinateMode,
            string inputCoordinateMode)
        {
            ThrowIfSessionCleanupPending();
            _recording.Enable(
                filePath,
                chunkSizeBytes,
                compression,
                outputCoordinateMode,
                inputCoordinateMode);
        }

        /// <summary>Enable MCAP recording with advanced writer options for the next session start.</summary>
        public void EnableRecording(string filePath, McapWriterOptions options, string coordinateMode = "")
        {
            ThrowIfSessionCleanupPending();
            _recording.Enable(filePath, options, coordinateMode);
        }

        /// <summary>Enable MCAP recording with paired coordinate conventions.</summary>
        public void EnableRecording(
            string filePath,
            McapWriterOptions options,
            string outputCoordinateMode,
            string inputCoordinateMode)
        {
            ThrowIfSessionCleanupPending();
            _recording.Enable(filePath, options, outputCoordinateMode, inputCoordinateMode);
        }

        /// <summary>Set the coordinate mode on the recording controller.</summary>
        public void SetRecordingCoordinateMode(string mode)
        {
            ThrowIfSessionCleanupPending();
            _recording.SetCoordinateMode(mode);
        }
        /// <summary>Set paired coordinate conventions on the recording controller.</summary>
        public void SetRecordingCoordinateModes(string outputMode, string inputMode)
        {
            ThrowIfSessionCleanupPending();
            _recording.SetCoordinateModes(outputMode, inputMode);
        }
        /// <summary>Disable recording.</summary>
        public void DisableRecording() => _recording.Disable();

        // ── Playback Control ──

        /// <summary>Enable the playback clock range from start to end nanoseconds.</summary>
        public void EnablePlaybackControl(ulong startNs, ulong endNs)
        {
            ThrowIfSessionCleanupPending();
            _playbackClock.EnableRange(startNs, endNs);
        }
        /// <summary>Whether playback control is enabled.</summary>
        public bool PlaybackEnabled => _tickCoordinator.IsPlaybackEnabled(_playbackClock);
        /// <summary>Get the playback start time in nanoseconds.</summary>
        public ulong GetPlaybackStartNs() => _tickCoordinator.GetPlaybackStartNs(_playbackClock);
        /// <summary>Get the playback end time in nanoseconds.</summary>
        public ulong GetPlaybackEndNs() => _tickCoordinator.GetPlaybackEndNs(_playbackClock);

        /// <summary>Apply a playback command to the clock.</summary>
        public void ApplyPlaybackCommand(byte cmd, float speed, bool hasSeek, ulong seekNs)
        {
            ThrowIfSessionCleanupPending();
            _tickCoordinator.ApplyPlaybackCommand(cmd, speed, hasSeek, seekNs, _playbackClock, _logger);
        }

        /// <summary>Get a snapshot of the playback clock state for a response.</summary>
        public PlaybackClock.PlaybackStateSnapshot GetPlaybackState(bool didSeek, string requestId)
            => _tickCoordinator.GetPlaybackState(didSeek, requestId, _playbackClock);

        /// <summary>Get the current replay cursor state for the optional loopback cursor endpoint.</summary>
        public ReplayCursorState GetExternalReplayCursorState()
            => ReplayCursorState.FromPlayback(
                ReplayEnabled,
                PlaybackEnabled,
                GetPlaybackState(false, "unity-cursor-state"),
                GetPlaybackStartNs(),
                GetPlaybackEndNs());

        /// <summary>Apply a decoded playback control request on the runtime owner thread.</summary>
        public PlaybackClock.PlaybackStateSnapshot ApplyPlaybackControl(
            byte cmd, float speed, bool hasSeek, ulong seekNs, string requestId)
        {
            ThrowIfSessionCleanupPending();
            return _tickCoordinator.ApplyPlaybackControl(
                cmd, speed, hasSeek, seekNs, requestId,
                _replay, _playbackClock, _wallClock, _logger);
        }

        // ── Replay (delegated) ──

        /// <summary>Whether replay is enabled.</summary>
        public bool ReplayEnabled => _replay.IsEnabled;
        /// <summary>Whether the last replay enable attempt observed a confirmed FoxRun schema mismatch.</summary>
        public bool ReplayStartHadSchemaMismatch => _replay.LastEnableHadSchemaMismatch;
        /// <summary>Whether the last replay enable attempt was blocked by a confirmed FoxRun schema mismatch.</summary>
        public bool ReplayStartBlockedBySchemaMismatch => _replay.LastEnableBlockedBySchemaMismatch;
        /// <summary>Message from the last failed replay enable attempt, or an empty string.</summary>
        public string ReplayStartFailureMessage => _replay.LastEnableFailureMessage;

        /// <summary>Enable MCAP replay; fails if recording is active.</summary>
        public void EnableReplay(string filePath)
        {
            ThrowIfSessionCleanupPending();
            _replay.Enable(filePath);
        }
        /// <summary>Enable MCAP replay using the selected schema identity policy.</summary>
        public void EnableReplay(string filePath, SchemaIdentityMode identityMode)
        {
            ThrowIfSessionCleanupPending();
            _replay.Enable(filePath, identityMode);
        }

        /// <summary>Enable MCAP replay with explicit output and input coordinate conventions.</summary>
        public void EnableReplay(
            string filePath,
            SchemaIdentityMode identityMode,
            string outputCoordinateMode,
            string inputCoordinateMode)
        {
            ThrowIfSessionCleanupPending();
            _replay.Enable(filePath, outputCoordinateMode, inputCoordinateMode, identityMode);
        }
        /// <summary>Disable replay and dispose the engine.</summary>
        public void DisableReplay()
            => _tickCoordinator.DisableReplay(_replay);
        /// <summary>Seek replay to the given nanosecond timestamp.</summary>
        public void ReplaySeek(ulong timeNs)
            => _tickCoordinator.ReplaySeek(timeNs, _replay, _wallClock);
        /// <summary>Start or resume replay playback.</summary>
        public void ReplayPlay()
            => _tickCoordinator.ReplayPlay(_replay, _playbackClock);
        /// <summary>Pause replay playback.</summary>
        public void ReplayPause()
            => _tickCoordinator.ReplayPause(_replay, _playbackClock);

        /// <summary>Enable or disable the optional external replay cursor queue.</summary>
        public void SetExternalReplayCursorEnabled(bool enabled)
        {
            ThrowIfSessionCleanupPending();
            _externalReplayCursorController.Enabled = enabled;
            if (!enabled)
                _externalReplayCursorController.Clear();
        }

        /// <summary>
        /// Queue one external replay cursor request for main-thread drain on the next runtime tick.
        /// </summary>
        public ExternalReplayCursorEnqueueResult TryEnqueueExternalReplayCursor(
            ReplayCursorRequest request,
            out string message)
        {
            ThrowIfSessionCleanupPending();
            return _externalReplayCursorController.TryEnqueue(
                request,
                ReplayEnabled,
                GetPlaybackStartNs(),
                GetPlaybackEndNs(),
                out message);
        }

        /// <summary>Request a replay panel-history backfill for newly subscribed clients.</summary>
        public void RequestReplaySubscriberBackfill()
        {
            ThrowIfSessionCleanupPending();
            _tickCoordinator.RequestReplaySubscriberBackfill(_replay, _playbackClock, _wallClock);
        }

        /// <summary>Internal: get the list of replay channels for test/runtime introspection.</summary>
        internal IReadOnlyList<McapChannel> GetReplayChannels() => _replay.GetChannels();

        /// <summary>Return the behavior class loaded for a replay channel id.</summary>
        public ReplayChannelBehavior GetReplayChannelBehavior(ushort channelId) => _replay.GetChannelBehavior(channelId);

        // ── Tick ──

        /// <summary>
        /// Called every frame from Unity. Drains service calls, ticks the
        /// replay engine when active, or broadcasts wall-clock time.
        /// </summary>
        public void Tick()
            => _tickCoordinator.Tick(_session, _playbackClock, _replay, _wallClock, _externalReplayCursorController);

        // ── Transport Health ──

        /// <summary>
        /// Get a read-only transport health snapshot.
        /// Returns <see cref="TransportStatsSnapshot.Unsupported"/> for transports
        /// that do not implement <see cref="IFoxgloveTransportStatsProvider"/>.
        /// </summary>
        public TransportStatsSnapshot GetTransportStatsSnapshot()
        {
            if (_transport is IFoxgloveTransportStatsProvider provider)
                return provider.GetStatsSnapshot();
            return Unsupported;
        }

        /// <summary>
        /// Stops the server, clears parameters and services, disposes
        /// recording, replay, and transport.
        /// </summary>
        public void Dispose()
        {
            if (Volatile.Read(ref _disposed) != 0)
                return;
            Volatile.Write(ref _disposeRequested, 1);
            if (Interlocked.Exchange(ref _disposing, 1) != 0)
                return;

            ExceptionDispatchInfo firstFailure = null;
            try
            {
                if (!_stopCleanup.IsComplete || _sessionPendingCleanup != null)
                {
                    try
                    {
                        Stop();
                    }
                    catch (Exception exception)
                    {
                        firstFailure ??= ExceptionDispatchInfo.Capture(exception);
                    }
                }

                TryCleanup(
                    () => _parameters.ClearDuringCleanup(),
                    ref _parametersCleared,
                    ref firstFailure);
                TryCleanup(
                    () => _services.Clear(),
                    ref _servicesCleared,
                    ref firstFailure);
                // The remaining owned helpers are pure managed state and do not implement IDisposable.
                TryCleanup(
                    _recording.Dispose,
                    ref _recordingDisposed,
                    ref firstFailure);
                TryCleanup(
                    _replay.Dispose,
                    ref _replayDisposed,
                    ref firstFailure);
                // Transport shutdown is independent of session callback removal.
                // Closing it here guarantees that a permanently failing custom
                // event accessor cannot keep the listener socket alive.
                TryCleanup(
                    _transport.Dispose,
                    ref _transportDisposed,
                    ref firstFailure);

                if (_transportDisposed && _sessionPendingCleanup != null)
                {
                    // Retry only the failed per-session substeps once after the
                    // transport is closed. If a custom accessor still refuses to
                    // detach, the disposed transport is the terminal ownership
                    // boundary and the retired session can be abandoned.
                    ExceptionDispatchInfo sessionRetryFailure = null;
                    RunStopCleanup(ref sessionRetryFailure);
                    firstFailure ??= sessionRetryFailure;
                    if (_sessionPendingCleanup != null)
                    {
                        try
                        {
                            _logger.LogWarning(
                                "Abandoning retired session callbacks after transport disposal completed.");
                        }
                        catch
                        {
                            // Diagnostics cannot prevent terminal resource release.
                        }
                        _sessionPendingCleanup = null;
                        _stopCleanup.MarkComplete(RuntimeStopCleanupStep.Session);
                        _stopCleanupComplete = _stopCleanup.IsResourceCleanupComplete;
                    }
                }

                if (_stopCleanupComplete
                    && _sessionPendingCleanup == null
                    && _parametersCleared
                    && _servicesCleared
                    && _recordingDisposed
                    && _replayDisposed
                    && _transportDisposed)
                {
                    Volatile.Write(ref _disposed, 1);
                }

                firstFailure?.Throw();
            }
            finally
            {
                Volatile.Write(ref _disposing, 0);
            }
        }

        private static void TryCleanup(
            Action cleanup,
            ref bool completed,
            ref ExceptionDispatchInfo firstFailure)
        {
            if (completed)
                return;

            try
            {
                cleanup();
                completed = true;
            }
            catch (Exception exception)
            {
                firstFailure ??= ExceptionDispatchInfo.Capture(exception);
            }
        }

        private static void TryCleanup(
            Action cleanup,
            ref ExceptionDispatchInfo firstFailure)
        {
            try
            {
                cleanup?.Invoke();
            }
            catch (Exception exception)
            {
                firstFailure ??= ExceptionDispatchInfo.Capture(exception);
            }
        }

        private void ThrowIfSessionCleanupPending()
        {
            if (_sessionPendingCleanup != null)
                throw new InvalidOperationException(
                    "Runtime configuration is unavailable while the previous session cleanup is pending.");
        }

        /// <summary>
        /// Public schema access is a guarded view rather than the raw injected
        /// registry.  Callers may retain the view across a failed Stop, but a
        /// registration cannot mutate the next-session definition set until the
        /// retired session has finished cleanup.
        /// </summary>
        private sealed class GuardedSchemaRegistry : IEncodingAwareSchemaRegistry
        {
            private readonly ISchemaRegistry _inner;
            private readonly Func<bool> _mutationAllowed;

            internal GuardedSchemaRegistry(ISchemaRegistry inner, Func<bool> mutationAllowed)
            {
                _inner = inner ?? throw new ArgumentNullException(nameof(inner));
                _mutationAllowed = mutationAllowed;
            }

            public bool TryGetSchema(string name, out SchemaEntry entry)
                => _inner.TryGetSchema(name, out entry);

            public bool TryGetSchema(string name, string encoding, out SchemaEntry entry)
            {
                if (_inner is IEncodingAwareSchemaRegistry encodingAware)
                    return encodingAware.TryGetSchema(name, encoding, out entry);

                if (!_inner.TryGetSchema(name, out entry))
                    return false;

                return string.Equals(entry.Encoding, encoding, StringComparison.OrdinalIgnoreCase);
            }

            public void Register(SchemaEntry entry)
            {
                if (_mutationAllowed != null && !_mutationAllowed())
                    throw new InvalidOperationException(
                        "Schema registry mutations are unavailable while session cleanup is pending.");
                _inner.Register(entry);
            }
        }

    }

    /// <summary>Shared executable generation predicate for main-thread client events.</summary>
    internal static class ClientEventGenerationGate
    {
        internal static bool IsCurrent(ulong eventGeneration, ulong currentGeneration)
            => eventGeneration == currentGeneration;
    }
}
