using UnityEngine;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// MonoBehaviour entry point. Manages FoxgloveRuntime lifecycle
    /// within Unity's game loop.
    /// </summary>
    public enum CoordinateMode
    {
        /// <summary>Unity left-handed: X right, Y up, Z forward. No conversion.</summary>
        UnityRaw,
        /// <summary>Foxglove right-handed: X forward, Y left, Z up.</summary>
        FoxgloveStandard
    }

    public enum McapCompressionMode
    {
        /// <summary>No compression.</summary>
        None,
        /// <summary>LZ4 block compression.</summary>
        Lz4,
        /// <summary>Zstandard compression.</summary>
        Zstd
    }

    public class FoxgloveManager : MonoBehaviour
    {
        [Header("General")]
        [SerializeField] private string _serverName = "Unity Foxglove SDK";
        [SerializeField] private string _host = "127.0.0.1";
        [SerializeField] private int _port = 8765;
        [SerializeField] private bool _startOnEnable = true;
        [SerializeField] private bool _runInBackground = true;

        [Header("Coordinate System")]
        [SerializeField] private CoordinateMode _coordinateMode = CoordinateMode.UnityRaw;

        // Phase 9: Asset roots
        [SerializeField] private AssetRootDefinition[] _assetRoots = { };

        // Phase 9: Playback control
        [SerializeField] private bool _enablePlaybackControl;
        [SerializeField] private float _playbackStartOffsetSeconds = 0;
        [SerializeField] private float _playbackDurationSeconds = 60;

        // Phase 10: MCAP Recording
        [Header("MCAP Recording")]
        [SerializeField] private bool _enableRecording;
        [SerializeField] private string _recordingPrefix = "foxglove";
        [Tooltip("Leave empty to save in <project>/Recordings/ . Shown path is the resolved default at runtime.")]
        [SerializeField] private string _recordingDirectory = "";
        [SerializeField] private int _recordingChunkSizeKB = 1024;
        [SerializeField] private McapCompressionMode _recordingCompression = McapCompressionMode.None;

        // Phase 11: MCAP Replay
        [Header("MCAP Replay")]
        [SerializeField] private bool _enableReplay;
        [SerializeField] private string _replayFilePath = "";
        [SerializeField] private bool _replayAutoPlay;
        [SerializeField] private bool _disableLivePublishers = true;
        private bool _livePublishersDisabled;

        private Core.FoxgloveRuntime _runtime;
        private int _nextChannelId = 1;
        private bool _warnedNotRunning;
        private readonly System.Collections.Generic.List<MonoBehaviour> _disabledPublishers = new();
        private readonly System.Collections.Concurrent.ConcurrentQueue<ClientEvent> _clientEvents = new();
        public ulong NowNs => _runtime?.NowNs ?? Schemas.FoxgloveTimeUtil.NowUnixTimeNs();
        public event System.Action<uint> OnClientConnected;
        public event System.Action<uint> OnClientDisconnected;
        public event System.Action<uint, uint, string, byte[]> OnClientMessage;
        public event System.Action<string, byte[]> OnReplayMessage;
        private readonly System.Collections.Generic.Dictionary<(string topic, string schemaName), uint> _channelCache
            = new System.Collections.Generic.Dictionary<(string, string), uint>();

        public CoordinateMode ActiveCoordinateMode => _coordinateMode;

        /// <summary>Convert Unity position to Foxglove (for publishing).</summary>
        public Vector3 UnityToFoxglovePosition(Vector3 p)
        {
            if (_coordinateMode == CoordinateMode.FoxgloveStandard)
                return new Vector3(p.y, p.z, p.x);
            return p;
        }

        /// <summary>Convert Unity rotation to Foxglove (for publishing).</summary>
        public Quaternion UnityToFoxgloveRotation(Quaternion q)
        {
            if (_coordinateMode == CoordinateMode.FoxgloveStandard)
                return new Quaternion(q.y, q.z, q.x, q.w);
            return q;
        }

        /// <summary>Convert Foxglove position to Unity (for replay).</summary>
        public Vector3 FoxgloveToUnityPosition(Vector3 p)
        {
            if (_coordinateMode == CoordinateMode.FoxgloveStandard)
                return new Vector3(-p.y, p.z, p.x);
            return p;
        }

        /// <summary>Convert Foxglove rotation to Unity (for replay).</summary>
        public Quaternion FoxgloveToUnityRotation(Quaternion q)
        {
            if (_coordinateMode == CoordinateMode.FoxgloveStandard)
                return new Quaternion(q.y, -q.z, -q.x, q.w);
            return q;
        }

        public Core.FoxgloveRuntime Runtime => _runtime;
        public bool IsRunning => _runtime?.Session?.IsRunning ?? false;

        private void Awake()
        {
            if (_runInBackground)
                Application.runInBackground = true;

            _runtime = new Core.FoxgloveRuntime(new UnityLogger());

            // Phase 11: Recording / Replay mutual exclusion
            if (_enableRecording && _enableReplay)
            {
                Debug.LogWarning("[Foxglove] Recording and Replay cannot both be enabled. Disabling Replay.");
                _enableReplay = false;
            }

            // Phase 11: If replay enabled, disable live publishers
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

        public void StartServer()
        {
            if (IsRunning)
            {
                Debug.LogWarning("[Foxglove] Server already running.");
                return;
            }

            // Phase 9: Register asset roots
            if (_assetRoots != null) foreach (var ar in _assetRoots)
            {
                if (ar.uriPrefix != null && ar.localRoot != null
                    && !string.IsNullOrEmpty(ar.uriPrefix) && !string.IsNullOrEmpty(ar.localRoot))
                {
                    var absRoot = System.IO.Path.IsPathRooted(ar.localRoot)
                        ? ar.localRoot
                        : System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", ar.localRoot));
                    var maxBytes = (long)ar.MaxBytesOrDefault;
                    _runtime.RegisterAssetRoot(ar.uriPrefix, absRoot, maxBytes);
                }
            }

            // Phase 9: Enable playback control
            if (_enablePlaybackControl)
            {
                var nowMs = (long)(System.DateTime.UtcNow - new System.DateTime(1970, 1, 1)).TotalMilliseconds;
                var startNs = (ulong)((nowMs + (long)(_playbackStartOffsetSeconds * 1000)) * 1_000_000L);
                var endNs = startNs + (ulong)(_playbackDurationSeconds * 1_000_000_000L);
                _runtime.EnablePlaybackControl(startNs, endNs);
            }

            // Phase 10: Enable recording
            if (_enableRecording)
            {
                var dir = string.IsNullOrEmpty(_recordingDirectory) ? Application.dataPath + "/../Recordings" : _recordingDirectory;
                System.IO.Directory.CreateDirectory(dir);
                var path = System.IO.Path.Combine(dir, $"{_recordingPrefix}_{System.DateTime.Now:yyyyMMdd_HHmmss}.mcap");
                var comp = _recordingCompression switch
                {
                    McapCompressionMode.Lz4 => "lz4",
                    McapCompressionMode.Zstd => "zstd",
                    _ => ""
                };
                var coord = _coordinateMode == CoordinateMode.FoxgloveStandard ? "FoxgloveStandard" : "UnityRaw";
                _runtime.EnableRecording(path, _recordingChunkSizeKB * 1024, comp, coord);
            }

            // Phase 11: Enable replay
            if (_enableReplay && !string.IsNullOrEmpty(_replayFilePath))
            {
                if (_disableLivePublishers && !_livePublishersDisabled)
                    DisableLivePublishers();
                var coord = _coordinateMode == CoordinateMode.FoxgloveStandard ? "FoxgloveStandard" : "UnityRaw";
                _runtime.SetRecordingCoordinateMode(coord);
                _runtime.EnableReplay(_replayFilePath);
            }

            _runtime.Start(_serverName, _host, _port);
            _runtime.OnReplayMessage += (topic, data) => OnReplayMessage?.Invoke(topic, data);
            _warnedNotRunning = false;

            var transport = _runtime.Session?.Transport;
            if (transport != null)
            {
                transport.OnClientConnected += EnqueueConnect;
                transport.OnClientDisconnected += EnqueueDisconnect;
                _runtime.Session.OnClientMessage += (cid, chId, topic, payload) =>
                    _clientEvents.Enqueue(new ClientEvent { ClientId = cid, ChannelId = chId, Topic = topic, Payload = payload, IsConnect = false, IsMessage = true });
            }

            Debug.Log($"[Foxglove] Server started on ws://{_host}:{_port}");
        }

        public void StopServer()
        {
            if (!IsRunning) return;
            _runtime.Stop();
            _channelCache.Clear();
            _nextChannelId = 1;
            RestoreLivePublishers();
        }

        /// <summary>
        /// Get or register a schema channel. Idempotent: same (topic, schemaName) returns same id.
        /// </summary>
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
        /// Serialize a message to JSON and publish. Safe no-op if runtime is not started.
        /// </summary>
        public void RegisterParameter(string name, Newtonsoft.Json.Linq.JToken value, string type, bool writable)
        {
            _runtime?.RegisterParameter(name, value, type, writable);
        }

        public uint RegisterService(Unity.FoxgloveSDK.Protocol.ServiceDescriptor descriptor)
        {
            return _runtime?.RegisterService(descriptor) ?? 0;
        }

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

            var channelId = GetOrRegisterSchemaChannel(topic, schemaName);
            _runtime.PublishJson(channelId, message, logTimeNs);
        }
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

    [System.Serializable]
    public struct AssetRootDefinition
    {
        [Tooltip("URI prefix, e.g. asset://demo/")]
        public string uriPrefix;
        [Tooltip("Local folder path (relative to project root or absolute)")]
        public string localRoot;
        [Tooltip("Maximum file size in MB (16 MB recommended)")]
        public float maxMB;
        public float MaxBytesOrDefault => (maxMB > 0 ? maxMB : 16) * 1024 * 1024;
    }

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
