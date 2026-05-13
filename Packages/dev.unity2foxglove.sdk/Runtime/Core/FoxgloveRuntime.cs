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
        /// <summary>Delegate bridging replay OnReplayMessage to the runtime's own event.</summary>
        private Action<string, byte[]> _replayForwarder;
        private readonly object _replaySnapshotLock = new();
        private readonly object _replaySceneSnapshotLock = new();
        private readonly object _playbackControlLock = new();
        private bool _replaySnapshotPending;
        private ulong _replaySnapshotTimeNs;
        private ulong _replaySnapshotReadyWallNs;
        private bool _replaySceneSnapshotPending;
        private ulong _replaySceneSnapshotTimeNs;

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
            _recording = new RecordingController(_logger);
            _replay = new ReplayController(_logger);
        }

        /// <summary>Active session; null before Start or after Stop.</summary>
        public FoxgloveSession Session => _session;
        /// <summary>Whether the session is currently running.</summary>
        public bool IsRunning => _session?.IsRunning ?? false;
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
        /// Start the WebSocket server. Creates a new FoxgloveSession,
        /// attaches recording/replay controllers, and wires replay
        /// message forwarding. Protobuf encoding is enabled automatically
        /// when the proto assembly is available.
        /// </summary>
        public void Start(string name, string host = "127.0.0.1", int port = 8765)
        {
            if (_session != null)
                throw new InvalidOperationException("Session already started. Call Stop() first.");

            var session = new FoxgloveSession(name, _transport, _playbackClock, _schemaRegistry, _logger, _parameters, _services);
            Action<string, byte[]> replayForwarder = null;
            try
            {
                session.SetRuntimeContext(this);
                if (_protobufSchemasRegistered)
                    session.EnableProtobuf();
                _recording.AttachToSession(_playbackClock, _parameters, session);
                session.Start(host, port);

                _session = session;
                _replay.RegisterChannels(session);

                replayForwarder = (topic, data) => OnReplayMessage?.Invoke(topic, data);
                _replay.OnReplayMessage += replayForwarder;
                _replayForwarder = replayForwarder;
            }
            catch
            {
                if (replayForwarder != null)
                    _replay.OnReplayMessage -= replayForwarder;
                _replayForwarder = null;
                _recording.DetachFromSession();
                session.Dispose();
                _session = null;
                throw;
            }
        }

        /// <summary>Fires when the replay engine forwards a message (e.g. for UI update).</summary>
        public event Action<string, byte[]> OnReplayMessage;

        /// <summary>Test-only hook to fire replay without loading an MCAP file.</summary>
        internal void FireReplayForTests(string topic, byte[] data)
            => _replay.FireForTests(topic, data);

        /// <summary>
        /// Stop the server, detach recording/replay, and dispose the session.
        /// </summary>
        public void Stop()
        {
            ClearPendingReplaySnapshot();
            ClearPendingReplaySceneSnapshot();
            _replay.CancelPanelHistory();
            if (_replayForwarder != null)
            {
                _replay.OnReplayMessage -= _replayForwarder;
                _replayForwarder = null;
            }
            _recording.DetachFromSession();
            _session?.Dispose();
            _session = null;
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

        /// <summary>Register a schema channel on the session with the given encoding (default "json").</summary>
        public void RegisterSchemaChannel(uint channelId, string topic, string schemaName, string encoding = "json")
        {
            if (_session == null) throw new InvalidOperationException("Session not started.");
            if (ReplayEnabled) return;
            _session.RegisterSchemaChannel(channelId, topic, schemaName, encoding);
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

        /// <summary>Enable MCAP replay; fails if recording is active.</summary>
        public void EnableReplay(string filePath)
            => _replay.Enable(filePath, _playbackClock, _recording.IsEnabled, _recording.CoordinateMode);
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
            lock (_replaySnapshotLock)
            {
                _replay.CancelPanelHistory();
                _replaySnapshotTimeNs = timeNs;
                _replaySnapshotReadyWallNs = _wallClock.NowNs + ReplayController.ScrubHistoryDebounceNs;
                _replaySnapshotPending = true;
            }
        }

        private bool TryConsumeReplaySnapshot(out ulong timeNs)
        {
            lock (_replaySnapshotLock)
            {
                timeNs = _replaySnapshotTimeNs;
                if (!_replaySnapshotPending)
                    return false;
                if (_wallClock.NowNs < _replaySnapshotReadyWallNs)
                    return false;
                _replaySnapshotPending = false;
                return true;
            }
        }

        private void QueueReplaySceneSnapshot(ulong timeNs)
        {
            lock (_replaySceneSnapshotLock)
            {
                _replaySceneSnapshotTimeNs = timeNs;
                _replaySceneSnapshotPending = true;
            }
        }

        private bool TryConsumeReplaySceneSnapshot(out ulong timeNs)
        {
            lock (_replaySceneSnapshotLock)
            {
                timeNs = _replaySceneSnapshotTimeNs;
                if (!_replaySceneSnapshotPending)
                    return false;
                _replaySceneSnapshotPending = false;
                return true;
            }
        }

        private void ClearPendingReplaySnapshot()
        {
            lock (_replaySnapshotLock)
            {
                _replaySnapshotPending = false;
                _replaySnapshotTimeNs = 0;
                _replaySnapshotReadyWallNs = 0;
            }
        }

        private void ClearPendingReplaySceneSnapshot()
        {
            lock (_replaySceneSnapshotLock)
            {
                _replaySceneSnapshotPending = false;
                _replaySceneSnapshotTimeNs = 0;
            }
        }

        /// <summary>Internal: get the list of replay channels for test/runtime introspection.</summary>
        internal IReadOnlyList<McapChannel> GetReplayChannels() => _replay.GetChannels();

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
    }
}
