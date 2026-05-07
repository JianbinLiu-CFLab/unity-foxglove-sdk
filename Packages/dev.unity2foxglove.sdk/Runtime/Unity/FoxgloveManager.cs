// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Unity
// Purpose: MonoBehaviour entry point that manages FoxgloveRuntime lifecycle
// within Unity's game loop. Exposes Inspector-configurable settings for
// WebSocket server, coordinate mode, asset roots, playback control, MCAP
// recording, and MCAP replay.

using UnityEngine;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// MonoBehaviour entry point. Manages FoxgloveRuntime lifecycle
    /// within Unity's game loop.
    ///
    /// Attach this component to a GameObject in your scene. It auto-starts
    /// the WebSocket server on <c>OnEnable</c> (unless <c>_startOnEnable</c>
    /// is disabled) and auto-stops on <c>OnDisable</c> / <c>OnDestroy</c>.
    ///
    /// During <c>Update</c>, it ticks the runtime and drains client connect /
    /// disconnect / message events onto the main thread so Unity API calls
    /// are safe.
    /// </summary>
    public enum CoordinateMode
    {
        /// <summary>Unity native left-handed (X right, Y up, Z forward). No conversion.</summary>
        LeftHand,
        /// <summary>ROS/Foxglove right-handed (X forward, Y left, Z up). Data is converted.</summary>
        RightHand
    }

    /// <summary>
    /// Compression algorithm for MCAP recording output.
    /// </summary>
    public enum McapCompressionMode
    {
        /// <summary>No compression.</summary>
        None,
        /// <summary>LZ4 block compression.</summary>
        Lz4,
        /// <summary>Zstandard compression.</summary>
        Zstd
    }

    /// <summary>
    /// Central Unity MonoBehaviour that owns the FoxgloveRuntime and
    /// bridges transport-layer events into the Unity main thread.
    ///
    /// <para>Lifecycle:</para>
    /// <list type="bullet">
    /// <item><c>Awake</c> — creates the runtime, disables conflicting features.</item>
    /// <item><c>OnEnable</c> — starts the WebSocket server.</item>
    /// <item><c>Update</c> — ticks the runtime, drains client events.</item>
    /// <item><c>OnDisable / OnDestroy</c> — stops server, disposes runtime.</item>
    /// </list>
    ///
    /// <para>Thread boundary:</para>
    /// Client connect, disconnect, and message handlers run on transport
    /// threads. They enqueue events via <c>ConcurrentQueue</c>, and the main
    /// thread dequeues them in <c>Update</c>. All Unity API access stays on
    /// the main thread.
    /// </summary>
    public class FoxgloveManager : MonoBehaviour
    {
        [Header("General")]
        [SerializeField] private string _serverName = "Unity Foxglove SDK";
        [SerializeField] private string _host = "127.0.0.1";
        [SerializeField] private int _port = 8765;
        [SerializeField] private bool _startOnEnable = true;
        [SerializeField] private bool _runInBackground = true;

        [Header("Coordinate System")]
        [SerializeField] private CoordinateMode _coordinateMode = CoordinateMode.LeftHand;

        [SerializeField] private AssetRootDefinition[] _assetRoots = { };

        [SerializeField] private bool _enablePlaybackControl;
        [SerializeField] private float _playbackStartOffsetSeconds = 0;
        [SerializeField] private float _playbackDurationSeconds = 60;

        [Header("MCAP Recording")]
        [SerializeField] private bool _enableRecording;
        [SerializeField] private string _recordingPrefix = "foxglove";
        [Tooltip("Leave empty to save in <project>/Recordings/ . Shown path is the resolved default at runtime.")]
        [SerializeField] private string _recordingDirectory = "";
        [SerializeField] private int _recordingChunkSizeKB = 1024;
        [SerializeField] private McapCompressionMode _recordingCompression = McapCompressionMode.None;

        [Header("MCAP Replay")]
        [SerializeField] private bool _enableReplay;
        [SerializeField] private string _replayFilePath = "";
        [SerializeField] private bool _replayAutoPlay;
        [SerializeField] private bool _disableLivePublishers = true;
        private bool _livePublishersDisabled;

        [Header("Security")]
        [Tooltip("Allowed browser origins for WebSocket connections. Empty list rejects all browser-origin clients. Foxglove Desktop and non-browser clients do not send Origin and are always allowed.")]
        [SerializeField] private List<string> _allowedBrowserOrigins = new();

        private Core.FoxgloveRuntime _runtime;
        private int _nextChannelId = 1;
        private bool _warnedNotRunning;
        private readonly System.Collections.Generic.List<MonoBehaviour> _disabledPublishers = new();
        private readonly System.Collections.Concurrent.ConcurrentQueue<ClientEvent> _clientEvents = new();

        /// <summary>Current nanosecond timestamp for publish calls.</summary>
        public ulong NowNs => _runtime?.NowNs ?? Schemas.FoxgloveTimeUtil.NowUnixTimeNs();

        /// <summary>Fires when a Foxglove client connects (on main thread).</summary>
        public event System.Action<uint> OnClientConnected;

        /// <summary>Fires when a Foxglove client disconnects (on main thread).</summary>
        public event System.Action<uint> OnClientDisconnected;

        /// <summary>
        /// Fires when a client-published message arrives (on main thread).
        /// Parameters: clientId, channelId, topic, payload bytes.
        /// </summary>
        public event System.Action<uint, uint, string, byte[]> OnClientMessage;

        /// <summary>Fires when a replay message is forwarded (on main thread).</summary>
        public event System.Action<string, byte[]> OnReplayMessage;

        private readonly System.Collections.Generic.Dictionary<(string topic, string schemaName), uint> _channelCache
            = new System.Collections.Generic.Dictionary<(string, string), uint>();

        private System.Action<string, byte[]> _replayForwarder;
        private System.Action<uint, uint, string, byte[]> _clientMessageForwarder;

        /// <summary>Current coordinate mode, read from Inspector or code.</summary>
        public CoordinateMode ActiveCoordinateMode => _coordinateMode;

        /// <summary>
        /// Convert a Unity position to Foxglove convention. No-op in LeftHand mode.
        /// </summary>
        public Vector3 UnityToFoxglovePosition(Vector3 p)
            => _coordinateMode == CoordinateMode.RightHand ? CoordinateConverter.UnityToFoxglovePosition(p) : p;

        /// <summary>
        /// Convert a Unity rotation to Foxglove convention. No-op in LeftHand mode.
        /// </summary>
        public Quaternion UnityToFoxgloveRotation(Quaternion q)
            => _coordinateMode == CoordinateMode.RightHand ? CoordinateConverter.UnityToFoxgloveRotation(q) : q;

        /// <summary>
        /// Convert a Foxglove position to Unity convention. No-op in LeftHand mode.
        /// </summary>
        public Vector3 FoxgloveToUnityPosition(Vector3 p)
            => _coordinateMode == CoordinateMode.RightHand ? CoordinateConverter.FoxgloveToUnityPosition(p) : p;

        /// <summary>
        /// Convert a Foxglove rotation to Unity convention. No-op in LeftHand mode.
        /// </summary>
        public Quaternion FoxgloveToUnityRotation(Quaternion q)
            => _coordinateMode == CoordinateMode.RightHand ? CoordinateConverter.FoxgloveToUnityRotation(q) : q;

        /// <summary>The backing FoxgloveRuntime. Null after Dispose.</summary>
        public Core.FoxgloveRuntime Runtime => _runtime;

        /// <summary>True if the WebSocket server is currently running.</summary>
        public bool IsRunning => _runtime?.Session?.IsRunning ?? false;

        private void Awake()
        {
            if (_runInBackground)
                Application.runInBackground = true;

            _runtime = new Core.FoxgloveRuntime(new UnityLogger());

            // Recording and replay cannot both be active. When both are enabled,
            // replay is disabled and recording is kept — replay relies on file data
            // already on disk, so live recording is the stricter constraint.
            if (_enableRecording && _enableReplay)
            {
                Debug.LogWarning("[Foxglove] Recording and Replay cannot both be enabled. Disabling Replay.");
                _enableReplay = false;
            }

            if (_enableReplay && _disableLivePublishers)
            {
                DisableLivePublishers();
            }
        }

        private void EnqueueConnect(uint id) =>
            _clientEvents.Enqueue(new ClientEvent { ClientId = id, IsConnect = true });

        private void EnqueueDisconnect(uint id) =>
            _clientEvents.Enqueue(new ClientEvent { ClientId = id, IsConnect = false });

        private void OnEnable()
        {
            if (_startOnEnable)
                StartServer();
        }

        // Drains transport-thread events onto the main thread so Unity API
        // usage in event handlers is safe (enabled only during Active state).
        private void Update()
        {
            _runtime?.Tick();
            while (_clientEvents.TryDequeue(out var evt))
            {
                if (evt.IsMessage)
                    OnClientMessage?.Invoke(evt.ClientId, evt.ChannelId, evt.Topic, evt.Payload);
                else if (evt.IsConnect)
                    OnClientConnected?.Invoke(evt.ClientId);
                else
                    OnClientDisconnected?.Invoke(evt.ClientId);
            }
        }

        private void OnDisable()
        {
            StopServer();
        }

        private void OnDestroy()
        {
            StopServer();
            _runtime?.Dispose();
            _runtime = null;
        }

        /// <summary>
        /// Start the WebSocket server. Idempotent — warns if already running.
        /// Registers asset roots, playback control, recording, and replay setup
        /// before starting the runtime.
        /// </summary>
        public void StartServer()
        {
            if (IsRunning)
            {
                Debug.LogWarning("[Foxglove] Server already running.");
                return;
            }

            RegisterAssetRoots();
            SetupPlaybackControl();
            SetupRecording();
            SetupReplay();
            SetupAllowedOrigins();

            _runtime.Start(_serverName, _host, _port);
            _replayForwarder = (topic, data) => OnReplayMessage?.Invoke(topic, data);
            _runtime.OnReplayMessage += _replayForwarder;
            _warnedNotRunning = false;

            var transport = _runtime.Session?.Transport;
            if (transport != null)
            {
                transport.OnClientConnected += EnqueueConnect;
                transport.OnClientDisconnected += EnqueueDisconnect;
                _clientMessageForwarder = (cid, chId, topic, payload) =>
                    _clientEvents.Enqueue(new ClientEvent { ClientId = cid, ChannelId = chId, Topic = topic, Payload = payload, IsConnect = false, IsMessage = true });
                _runtime.Session.OnClientMessage += _clientMessageForwarder;
            }

            Debug.Log($"[Foxglove] Server started on ws://{_host}:{_port}");
        }

        /// <summary>
        /// Register asset roots from the Inspector list. Resolves relative
        /// <c>localRoot</c> paths against the Unity project root.
        /// </summary>
        private void RegisterAssetRoots()
        {
            if (_assetRoots == null) return;
            foreach (var ar in _assetRoots)
            {
                if (ar.uriPrefix == null || ar.localRoot == null
                    || string.IsNullOrEmpty(ar.uriPrefix) || string.IsNullOrEmpty(ar.localRoot))
                    continue;
                var absRoot = System.IO.Path.IsPathRooted(ar.localRoot)
                    ? ar.localRoot
                    : System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", ar.localRoot));
                _runtime.RegisterAssetRoot(ar.uriPrefix, absRoot, (long)ar.MaxBytesOrDefault);
            }
        }

        /// <summary>
        /// Configure PlaybackControl on the runtime if <c>_enablePlaybackControl</c>
        /// is set. Defines a time window in <c>logTimeNs</c> based on current UTC
        /// time plus offset and duration.
        /// </summary>
        private void SetupPlaybackControl()
        {
            if (!_enablePlaybackControl) return;
            var nowMs = (long)(System.DateTime.UtcNow - new System.DateTime(1970, 1, 1)).TotalMilliseconds;
            var startNs = (ulong)((nowMs + (long)(_playbackStartOffsetSeconds * 1000)) * 1_000_000L);
            var endNs = startNs + (ulong)(_playbackDurationSeconds * 1_000_000_000L);
            _runtime.EnablePlaybackControl(startNs, endNs);
        }

        /// <summary>
        /// Configure MCAP recording on the runtime if <c>_enableRecording</c>
        /// is set. Creates the output directory and generates a timestamped
        /// filename. Compression and coordinate mode are forwarded from Inspector.
        /// </summary>
        private void SetupRecording()
        {
            if (!_enableRecording) return;
            var dir = string.IsNullOrEmpty(_recordingDirectory) ? Application.dataPath + "/../Recordings" : _recordingDirectory;
            System.IO.Directory.CreateDirectory(dir);
            var path = System.IO.Path.Combine(dir, $"{_recordingPrefix}_{System.DateTime.Now:yyyyMMdd_HHmmss}.mcap");
            var comp = _recordingCompression switch
            {
                McapCompressionMode.Lz4 => "lz4",
                McapCompressionMode.Zstd => "zstd",
                _ => ""
            };
            var coord = _coordinateMode == CoordinateMode.RightHand ? "RightHand" : "LeftHand";
            _runtime.EnableRecording(path, _recordingChunkSizeKB * 1024, comp, coord);
        }

        /// <summary>
        /// Configure MCAP replay on the runtime if <c>_enableReplay</c> is set
        /// and a valid file path is provided. Disables live publishers if
        /// <c>_disableLivePublishers</c> is enabled.
        /// </summary>
        private void SetupReplay()
        {
            if (!_enableReplay || string.IsNullOrEmpty(_replayFilePath)) return;
            if (_disableLivePublishers && !_livePublishersDisabled)
                DisableLivePublishers();
            var coord = _coordinateMode == CoordinateMode.RightHand ? "RightHand" : "LeftHand";
            _runtime.SetRecordingCoordinateMode(coord);
            _runtime.EnableReplay(_replayFilePath);
        }

        /// <summary>Sync Inspector-configured browser origin allowlist to the transport before starting.</summary>
        private void SetupAllowedOrigins()
        {
            _runtime.ClearAllowedOrigins();
            if (_allowedBrowserOrigins != null)
            {
                foreach (var origin in _allowedBrowserOrigins)
                    _runtime.AddAllowedOrigin(origin);
            }
        }

        /// <summary>
        /// Stop the WebSocket server and clean up transport subscriptions.
        /// Channel cache is cleared and live publishers are restored.
        /// </summary>
        public void StopServer()
        {
            if (!IsRunning) return;

            var transport = _runtime.Session?.Transport;
            // Unsubscribe transport events and client message forwarder before
            // stopping the runtime, so stop/cleanup callbacks don't fire handlers.
            if (transport != null)
            {
                transport.OnClientConnected -= EnqueueConnect;
                transport.OnClientDisconnected -= EnqueueDisconnect;
            }
            if (_runtime.Session != null && _clientMessageForwarder != null)
            {
                _runtime.Session.OnClientMessage -= _clientMessageForwarder;
                _clientMessageForwarder = null;
            }
            // Replay forwarder is also cleaned up inside _runtime.Stop(),
            // but we remove it here first so no replay messages slip through
            // during shutdown.
            if (_replayForwarder != null)
            {
                _runtime.OnReplayMessage -= _replayForwarder;
                _replayForwarder = null;
            }
            _runtime.Stop();
            _channelCache.Clear();
            _nextChannelId = 1;
            RestoreLivePublishers();
        }

        /// <summary>
        /// Get or register a schema-bound channel. Idempotent: same
        /// (topic, schemaName) pair always returns the same channel id.
        /// </summary>
        /// <param name="topic">Topic name (e.g. "/tf").</param>
        /// <param name="schemaName">Schema name (e.g. "foxglove.FrameTransform").</param>
        public uint GetOrRegisterSchemaChannel(string topic, string schemaName)
        {
            var key = (topic, schemaName);
            if (_channelCache.TryGetValue(key, out var id))
                return id;

            id = (uint)_nextChannelId++;
            _channelCache[key] = id;
            _runtime.RegisterSchemaChannel(id, topic, schemaName);
            return id;
        }

        /// <summary>
        /// Register a runtime parameter. Safe no-op when the runtime is null.
        /// </summary>
        /// <param name="name">Parameter path (e.g. "/cube/color").</param>
        /// <param name="value">Initial value as a JToken.</param>
        /// <param name="type">Foxglove type string (e.g. "number[]").</param>
        /// <param name="writable">Whether Foxglove clients can modify this parameter.</param>
        public void RegisterParameter(string name, Newtonsoft.Json.Linq.JToken value, string type, bool writable)
        {
            _runtime?.RegisterParameter(name, value, type, writable);
        }

        /// <summary>
        /// Register a service. Returns 0 if the runtime is null.
        /// </summary>
        /// <param name="descriptor">Service descriptor with name, type, request/response schemas.</param>
        public uint RegisterService(Unity.FoxgloveSDK.Protocol.ServiceDescriptor descriptor)
        {
            return _runtime?.RegisterService(descriptor) ?? 0;
        }

        /// <summary>
        /// Serialize a message to JSON and publish. Safe no-op if the server
        /// is not running — the first such call per session emits a warning.
        /// </summary>
        /// <param name="topic">Topic to publish to.</param>
        /// <param name="schemaName">Schema name, or null/empty for schemaless JSON.</param>
        /// <param name="message">Object to serialize via Newtonsoft.Json.</param>
        /// <param name="logTimeNs">Nanosecond log timestamp.</param>
        public void PublishJson(string topic, string schemaName, object message, ulong logTimeNs)
        {
            if (!IsRunning)
            {
                if (!_warnedNotRunning)
                {
                    Debug.LogWarning("[Foxglove] PublishJson called but server is not running.");
                    _warnedNotRunning = true;
                }
                return;
            }

            var channelId = string.IsNullOrEmpty(schemaName)
                ? GetOrRegisterChannel(topic, "json")
                : GetOrRegisterSchemaChannel(topic, schemaName);
            _runtime.PublishJson(channelId, message, logTimeNs);
        }

        private uint GetOrRegisterChannel(string topic, string encoding)
        {
            var key = (topic, encoding);
            if (_channelCache.TryGetValue(key, out var id))
                return id;

            id = (uint)_nextChannelId++;
            _channelCache[key] = id;
            _runtime.RegisterChannel(new Protocol.AdvertiseChannel
            {
                Id = id,
                Topic = topic,
                Encoding = encoding,
                SchemaName = "",
                Schema = ""
            });
            return id;
        }

        // Disables all FoxglovePublisherBase components in the scene so that
        // replay-driven data and live publisher data don't collide.
        private void DisableLivePublishers()
        {
            if (_livePublishersDisabled) return;
            var pubs = FindObjectsByType<FoxglovePublisherBase>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            _disabledPublishers.Clear();
            foreach (var pub in pubs)
            {
                if (pub.enabled)
                {
                    pub.enabled = false;
                    _disabledPublishers.Add(pub);
                }
            }
            _livePublishersDisabled = true;
            Debug.Log($"[Foxglove] Disabled {_disabledPublishers.Count} live publisher(s)");
        }

        private void RestoreLivePublishers()
        {
            if (!_livePublishersDisabled) return;
            foreach (var pub in _disabledPublishers)
            {
                if (pub != null)
                    pub.enabled = true;
            }
            _disabledPublishers.Clear();
            _livePublishersDisabled = false;
            Debug.Log("[Foxglove] Restored live publishers");
        }
    }

    /// <summary>
    /// Defines an asset root mapping: associates a URI prefix with a local
    /// folder for <c>fetchAsset</c> requests.
    /// </summary>
    [System.Serializable]
    public struct AssetRootDefinition
    {
        [Tooltip("URI prefix, e.g. asset://demo/")]
        public string uriPrefix;
        [Tooltip("Local folder path (relative to project root or absolute)")]
        public string localRoot;
        [Tooltip("Maximum file size in MB (16 MB recommended)")]
        public float maxMB;
        /// <summary>Maximum file size in bytes. Defaults to 16 MB.</summary>
        public float MaxBytesOrDefault => (maxMB > 0 ? maxMB : 16) * 1024 * 1024;
    }

    /// <summary>
    /// Internal struct for enqueuing transport events on the main thread.
    /// </summary>
    internal struct ClientEvent
    {
        public uint ClientId;
        public uint ChannelId;
        public string Topic;
        public byte[] Payload;
        public bool IsConnect;
        public bool IsMessage;
    }
}
