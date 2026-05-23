// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core
// Purpose: Top-level SDK entry point that owns transport, session, clock,
// parameter store, service registry, asset registry, recording controller,
// and replay controller. Delegates public API to these managed components.

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Transport;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Schemas.Ros2Msg;
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
    public class FoxgloveRuntime : IDisposable, IRuntimeContext
    {
        /// <summary>Active session; null before Start or after Stop.</summary>
        private FoxgloveSession _session;
        private readonly IFoxgloveTransport _transport;
        private readonly IFoxgloveClock _wallClock;
        private readonly PlaybackClock _playbackClock;
        private readonly ISchemaRegistry _schemaRegistry;
        private readonly IFoxgloveLogger _logger;
        private bool _protobufSchemasRegistered;
        private bool _ros2MsgSchemasRegistered;

        // Runtime-owned definitions survive Stop/Start cycles so
        // parameters and services are re-advertised on restart.
        /// <summary>Runtime-owned parameter store; survives Stop/Start cycles.</summary>
        private readonly FoxgloveParameterStore _parameters = new();
        /// <summary>Runtime-owned service registry; survives Stop/Start cycles.</summary>
        private readonly FoxgloveServiceRegistry _services = new();
        /// <summary>Runtime-owned asset registry for fetchAsset capability.</summary>
        private readonly FoxgloveAssetRegistry _assets = new();

        /// <summary>Recording lifecycle controller.</summary>
        private readonly RecordingController _recording;
        /// <summary>Replay lifecycle controller.</summary>
        private readonly ReplayController _replay;
        private readonly ReplaySnapshotStateMachine _replaySnapshots;
        /// <summary>Delegate bridging replay OnReplayMessage to the runtime's own event.</summary>
        private Action<string, byte[]> _replayForwarder;
        /// <summary>Delegate bridging context-rich replay messages to the runtime's own event.</summary>
        private Action<ReplayMessageContext> _replayContextForwarder;
        /// <summary>Delegate bridging replay batch boundaries to the runtime's own event.</summary>
        private Action<ReplayBatchContext> _replayBatchForwarder;
        private readonly object _playbackControlLock = new();

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
            if (_transport is IOriginGuardedFoxgloveTransport originGuard)
                originGuard.AddAllowedOrigin(origin);
        }

        /// <summary>Clear the transport's browser origin allowlist, blocking all browser clients.</summary>
        public void ClearAllowedOrigins()
        {
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
            FoxgloveSchemaDefinitions.RegisterCoreSchemas(_schemaRegistry);
            TryRegisterProtobufSchemas();
            TryRegisterRos2MsgSchemas();
            _recording = new RecordingController(_logger);
            _replay = new ReplayController(_logger);
            _replaySnapshots = new ReplaySnapshotStateMachine();
        }

        /// <summary>Active session; null before Start or after Stop.</summary>
        public FoxgloveSession Session => _session;
        /// <summary>Whether the session is currently running.</summary>
        public bool IsRunning => _session?.IsRunning ?? false;
        /// <summary>Whether a registered channel has live subscriber or MCAP recording demand.</summary>
        public bool HasChannelDemand(uint channelId) => _session?.HasChannelDemand(channelId) ?? false;
        /// <summary>Schema registry used by this runtime.</summary>
        public ISchemaRegistry Schemas => _schemaRegistry;
        /// <summary>Runtime-owned parameter store.</summary>
        public FoxgloveParameterStore Parameters => _parameters;

        /// <summary>Register a named parameter. Can be called before Start; stored for later advertisement.</summary>
        public void RegisterParameter(string name, JToken value, string type, bool writable)
            => _parameters.Register(name, value, type, writable);

        /// <summary>
        /// Update a writable runtime-owned parameter and notify Foxglove clients
        /// subscribed to parameter updates.
        /// </summary>
        public bool TrySetParameter(string name, JToken value)
        {
            if (!_parameters.TrySetFromClient(name, value))
                return false;
            _session?.BroadcastParameterValues(new[] { name });
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
            var id = handler != null
                ? _services.Register(descriptor, handler)
                : _services.Register(descriptor);
            // Re-advertise immediately so connected clients pick up the new service
            if (_session != null)
            {
                _session.AdvertiseRegisteredService(id);
            }
            return id;
        }

        /// <summary>
        /// Unregister a service and notify connected clients when the runtime
        /// is currently serving a session.
        /// </summary>
        public bool UnregisterService(uint serviceId)
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
        public void Start(string name, string host = "127.0.0.1", int port = 8765, bool enableCdrClientPublish = true)
        {
            if (_session != null)
                throw new InvalidOperationException("Session already started. Call Stop() first.");

            var session = new FoxgloveSession(name, _transport, _playbackClock, _schemaRegistry, _logger, _parameters, _services);
            Action<string, byte[]> replayForwarder = null;
            Action<ReplayMessageContext> replayContextForwarder = null;
            Action<ReplayBatchContext> replayBatchForwarder = null;
            try
            {
                session.SetRuntimeContext(this);
                if (_protobufSchemasRegistered)
                    session.EnableProtobuf();
                if (enableCdrClientPublish && _ros2MsgSchemasRegistered)
                    session.EnableCdr();
                _recording.AttachToSession(_playbackClock, _parameters, session);
                session.Start(host, port);

                _session = session;
                _replay.RegisterChannels(session);

                replayForwarder = (topic, data) => OnReplayMessage?.Invoke(topic, data);
                replayContextForwarder = context => OnReplayMessageContext?.Invoke(context);
                replayBatchForwarder = context => OnReplayBatchCompleted?.Invoke(context);
                _replay.OnReplayMessage += replayForwarder;
                _replay.OnReplayMessageContext += replayContextForwarder;
                _replay.OnReplayBatchCompleted += replayBatchForwarder;
                _replayForwarder = replayForwarder;
                _replayContextForwarder = replayContextForwarder;
                _replayBatchForwarder = replayBatchForwarder;
            }
            catch
            {
                if (replayForwarder != null)
                    _replay.OnReplayMessage -= replayForwarder;
                if (replayContextForwarder != null)
                    _replay.OnReplayMessageContext -= replayContextForwarder;
                if (replayBatchForwarder != null)
                    _replay.OnReplayBatchCompleted -= replayBatchForwarder;
                _replayForwarder = null;
                _replayContextForwarder = null;
                _replayBatchForwarder = null;
                _recording.DetachFromSession();
                session.Dispose();
                _session = null;
                throw;
            }
        }

        /// <summary>Fires when the replay engine forwards a message (e.g. for UI update).</summary>
        public event Action<string, byte[]> OnReplayMessage;

        /// <summary>Fires when replay data is forwarded with channel, schema, and log-time context.</summary>
        public event Action<ReplayMessageContext> OnReplayMessageContext;

        /// <summary>Fires after a replay batch has been forwarded to scene listeners.</summary>
        public event Action<ReplayBatchContext> OnReplayBatchCompleted;

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
            _replaySnapshots.Clear();
            _replay.CancelPanelHistory();
            if (_replayForwarder != null)
            {
                _replay.OnReplayMessage -= _replayForwarder;
                _replayForwarder = null;
            }
            if (_replayContextForwarder != null)
            {
                _replay.OnReplayMessageContext -= _replayContextForwarder;
                _replayContextForwarder = null;
            }
            if (_replayBatchForwarder != null)
            {
                _replay.OnReplayBatchCompleted -= _replayBatchForwarder;
                _replayBatchForwarder = null;
            }
            var session = _session;
            _session = null;
            session?.SetRecorder(null);
            session?.Dispose();
            _recording.DetachFromSession();
        }

        // ── Channel API ──

        /// <summary>Register an advertise channel on the session.</summary>
        public void RegisterChannel(AdvertiseChannel channel)
        {
            if (_session == null) throw new InvalidOperationException("Session not started.");
            if (ReplayEnabled) return;
            _session.RegisterChannel(channel);
        }

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
            if (ReplayEnabled) return;
            _session.Publish(channelId, payload);
        }

        /// <summary>Publish raw bytes with an explicit nanosecond timestamp.</summary>
        public void Publish(uint channelId, byte[] payload, ulong logTimeNs)
        {
            if (_session == null) throw new InvalidOperationException("Session not started.");
            if (ReplayEnabled) return;
            _session.Publish(channelId, payload, logTimeNs);
        }

        /// <summary>Publish a validated ROS 2 CDR payload. Timestamp is taken from the clock.</summary>
        public void PublishRos2Cdr(uint channelId, byte[] payload)
        {
            if (_session == null) throw new InvalidOperationException("Session not started.");
            if (ReplayEnabled) return;
            _session.PublishRos2Cdr(channelId, payload);
        }

        /// <summary>Publish a validated ROS 2 CDR payload with an explicit nanosecond timestamp.</summary>
        public void PublishRos2Cdr(uint channelId, byte[] payload, ulong logTimeNs)
        {
            if (_session == null) throw new InvalidOperationException("Session not started.");
            if (ReplayEnabled) return;
            _session.PublishRos2Cdr(channelId, payload, logTimeNs);
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
            if (ReplayEnabled) return;
            _session.RegisterSchemaChannel(channelId, topic, schemaName, encoding, schemaEncoding);
        }

        /// <summary>Register a ROS 2 .msg schema channel with CDR message encoding.</summary>
        public void RegisterRos2MsgSchemaChannel(uint channelId, string topic, string schemaName)
        {
            RegisterSchemaChannel(channelId, topic, schemaName, "cdr", "ros2msg");
        }

        /// <summary>Serialize and publish a JSON message. Timestamp is taken from the clock.</summary>
        public void PublishJson(uint channelId, object message)
        {
            if (_session == null) throw new InvalidOperationException("Session not started.");
            if (ReplayEnabled) return;
            _session.PublishJson(channelId, message);
        }

        /// <summary>Serialize and publish a JSON message with an explicit nanosecond timestamp.</summary>
        public void PublishJson(uint channelId, object message, ulong logTimeNs)
        {
            if (_session == null) throw new InvalidOperationException("Session not started.");
            if (ReplayEnabled) return;
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
            => _assets.RegisterRoot(uriPrefix, localRoot, maxBytes);

        /// <summary>Asset registry for fetchAsset capability.</summary>
        public FoxgloveAssetRegistry Assets => _assets;

        // ── Recording (delegated) ──

        /// <summary>Whether recording is enabled.</summary>
        public bool RecordingEnabled => _recording.IsEnabled;

        /// <summary>Enable MCAP recording for the next session start.</summary>
        public void EnableRecording(string filePath, int chunkSizeBytes = McapRecorder.DefaultChunkSizeBytes, string compression = "", string coordinateMode = "")
            => _recording.Enable(filePath, chunkSizeBytes, compression, coordinateMode);

        /// <summary>Enable MCAP recording with advanced writer options for the next session start.</summary>
        public void EnableRecording(string filePath, McapWriterOptions options, string coordinateMode = "")
            => _recording.Enable(filePath, options, coordinateMode);

        /// <summary>Set the coordinate mode on the recording controller.</summary>
        public void SetRecordingCoordinateMode(string mode) => _recording.SetCoordinateMode(mode);
        /// <summary>Disable recording.</summary>
        public void DisableRecording() => _recording.Disable();

        // ── Playback Control ──

        /// <summary>Enable the playback clock range from start to end nanoseconds.</summary>
        public void EnablePlaybackControl(ulong startNs, ulong endNs) => _playbackClock.EnableRange(startNs, endNs);
        /// <summary>Whether playback control is enabled.</summary>
        public bool PlaybackEnabled => _playbackClock.PlaybackEnabled;
        /// <summary>Get the playback start time in nanoseconds.</summary>
        public ulong GetPlaybackStartNs() => _playbackClock.StartNs;
        /// <summary>Get the playback end time in nanoseconds.</summary>
        public ulong GetPlaybackEndNs() => _playbackClock.EndNs;

        /// <summary>Apply a playback command to the clock.</summary>
        public void ApplyPlaybackCommand(byte cmd, float speed, bool hasSeek, ulong seekNs)
        {
            lock (_playbackControlLock)
                _playbackClock.Apply(cmd, speed, hasSeek, seekNs);
        }

        /// <summary>Get a snapshot of the playback clock state for a response.</summary>
        public PlaybackClock.PlaybackStateSnapshot GetPlaybackState(bool didSeek, string requestId)
        {
            lock (_playbackControlLock)
                return _playbackClock.ToState(didSeek, requestId);
        }

        /// <summary>Apply a decoded playback control request on the runtime owner thread.</summary>
        public PlaybackClock.PlaybackStateSnapshot ApplyPlaybackControl(
            byte cmd, float speed, bool hasSeek, ulong seekNs, string requestId)
        {
            lock (_playbackControlLock)
            {
                _playbackClock.Apply(cmd, speed, hasSeek, seekNs);

                if (hasSeek)
                {
                    _replay.Seek(seekNs);
                    QueueReplaySceneSnapshot(seekNs);
                }

                if (cmd == 0)
                {
                    ClearPendingReplaySnapshot();
                    _replay.ResetPanelHistoryProgress();
                    _replay.Play();
                }
                else if (cmd == 1)
                {
                    _replay.Pause();
                    ClearPendingReplaySnapshot();
                }

                if (hasSeek && cmd == 1)
                    QueueReplaySnapshot(seekNs);

                return _playbackClock.ToState(hasSeek, requestId);
            }
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
            => _replay.Enable(filePath, _playbackClock, _recording.IsEnabled, _recording.CoordinateMode);
        /// <summary>Enable MCAP replay using the selected schema identity policy.</summary>
        public void EnableReplay(string filePath, SchemaIdentityMode identityMode)
            => _replay.Enable(filePath, _playbackClock, _recording.IsEnabled, _recording.CoordinateMode, identityMode);
        /// <summary>Disable replay and dispose the engine.</summary>
        public void DisableReplay()
        {
            ClearPendingReplaySnapshot();
            ClearPendingReplaySceneSnapshot();
            _replay.Disable();
        }
        /// <summary>Seek replay to the given nanosecond timestamp.</summary>
        public void ReplaySeek(ulong timeNs)
        {
            lock (_playbackControlLock)
            {
                _replay.Seek(timeNs);
                QueueReplaySceneSnapshot(timeNs);
                QueueReplaySnapshot(timeNs);
            }
        }
        /// <summary>Start or resume replay playback.</summary>
        public void ReplayPlay()
        {
            lock (_playbackControlLock)
            {
                ClearPendingReplaySnapshot();
                ClearPendingReplaySceneSnapshot();
                _replay.ResetPanelHistoryProgress();
                _playbackClock.Play();
                _replay.Play();
            }
        }
        /// <summary>Pause replay playback.</summary>
        public void ReplayPause()
        {
            lock (_playbackControlLock)
            {
                _playbackClock.Pause();
                _replay.Pause();
                ClearPendingReplaySnapshot();
            }
        }

        private void QueueReplaySnapshot(ulong timeNs)
        {
            _replay.CancelPanelHistory();
            _replaySnapshots.RequestPanelSnapshot(
                timeNs,
                _wallClock.NowNs + ReplayController.ScrubHistoryDebounceNs);
        }

        private bool TryConsumeReplaySnapshot(out ulong timeNs)
            => _replaySnapshots.TryConsumePanelSnapshot(_wallClock.NowNs, out timeNs);

        private void QueueReplaySceneSnapshot(ulong timeNs)
            => _replaySnapshots.RequestSceneSnapshot(timeNs);

        private bool TryConsumeReplaySceneSnapshot(out ulong timeNs)
            => _replaySnapshots.TryConsumeSceneSnapshot(out timeNs);

        private void ClearPendingReplaySnapshot()
            => _replaySnapshots.ClearPanelSnapshot();

        private void ClearPendingReplaySceneSnapshot()
            => _replaySnapshots.ClearSceneSnapshot();

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
        {
            if (_session == null) return;
            _session.DrainPlaybackControls();
            _session.DrainServiceCalls();
            lock (_playbackControlLock)
            {
                _playbackClock.Tick();

                if (_replay.IsEnabled)
                {
                    if (TryConsumeReplaySceneSnapshot(out var sceneSnapshotTimeNs))
                        _replay.ApplySnapshotToScene(sceneSnapshotTimeNs);
                    if (TryConsumeReplaySnapshot(out var snapshotTimeNs))
                        _replay.PublishSnapshot(_session, snapshotTimeNs);
                    else
                        _replay.DrainPanelHistory(_session);
                    _replay.Tick(_session, _playbackClock.NowNs);
                }
                else
                    _session.BroadcastTime();
            }
        }

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
            Stop();
            _parameters.Clear();
            _services.Clear();
            _recording.Dispose();
            _replay.Dispose();
            _transport.Dispose();
        }

        /// <summary>
        /// Try to load protobuf schema registration from the optional Proto assembly.
        /// If the assembly is present, registers all 46 official Foxglove protobuf schemas.
        /// This is a no-op if the proto assembly is not available.
        /// </summary>
        private void TryRegisterProtobufSchemas()
        {
            try
            {
                var type = Type.GetType(
                    "Foxglove.Schemas.ProtobufSchemasSetup, Unity.FoxgloveSDK.Proto");
                if (type == null) return;

                var method = type.GetMethod("RegisterSchemas",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (method == null) return;

                method.Invoke(null, new object[] { _schemaRegistry });
                _protobufSchemasRegistered = true;
            }
            catch (Exception ex)
            {
                // Protobuf support is optional. Keep startup non-fatal, but emit
                // one diagnostic so real schema-registration failures are visible.
                _logger.LogWarning($"Optional protobuf schema registration failed; continuing without protobuf support: {ex.Message}");
            }
        }

        /// <summary>
        /// Register bundled ROS 2 .msg schemas. If registration succeeds,
        /// sessions advertise CDR support for explicit ros2msg channels.
        /// </summary>
        private void TryRegisterRos2MsgSchemas()
        {
            try
            {
                Ros2MsgSchemasSetup.RegisterSchemas(_schemaRegistry);
                _ros2MsgSchemasRegistered = true;
            }
            catch (Exception ex)
            {
                // ROS 2 .msg schema support is optional. Keep startup non-fatal,
                // but emit one diagnostic so real registration failures are visible.
                _logger.LogWarning($"Optional ROS 2 .msg schema registration failed; continuing without CDR support: {ex.Message}");
            }
        }
    }
}
