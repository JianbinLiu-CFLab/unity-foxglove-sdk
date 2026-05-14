// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components
// Purpose: MonoBehaviour entry point that manages FoxgloveRuntime lifecycle
// within Unity's game loop. Exposes Inspector-configurable settings for
// WebSocket server, coordinate mode, asset roots, playback control, MCAP
// recording, and MCAP replay.

using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Transport;
using UnityEngine;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Unity-to-Foxglove coordinate conversion mode.
    /// </summary>
    public enum CoordinateMode
    {
        /// <summary>Unity native left-handed coordinates with X right, Y up, and Z forward.</summary>
        LeftHand,

        /// <summary>ROS/Foxglove right-handed coordinates with X forward, Y left, and Z up.</summary>
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
    /// Central Unity MonoBehaviour that owns the Foxglove runtime and bridges transport events into the Unity main thread.
    /// </summary>
    public partial class FoxgloveManager : MonoBehaviour
    {
        /// <summary>
        /// First channel identifier assigned by manager-owned manual publishing APIs.
        /// </summary>
        private const int FirstAutoChannelId = 1;

        [Header("General")]
        [SerializeField] private string _serverName = "Unity Foxglove SDK";
        [SerializeField] private string _host = "127.0.0.1";
        [SerializeField] private int _port = 8765;
        [SerializeField] private bool _startOnEnable = true;
        [SerializeField] private bool _runInBackground = true;
        [SerializeField] private FoxgloveTransportMode _transportMode = FoxgloveTransportMode.WebSocket;

        [Header("Publisher Encoding")]
        [Tooltip("Global default encoding for publishers that support it.")]
        [SerializeField] private GlobalEncoding _defaultPublisherEncoding = GlobalEncoding.Protobuf;
        [Tooltip("When enabled, individual publishers can override the global default.")]
        [SerializeField] private bool _allowPublisherOverride = true;

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
        [Tooltip("Allow hosted Foxglove Web at https://app.foxglove.dev. This is independent of project, user, layout, and query string.")]
        [SerializeField] private bool _allowHostedFoxgloveWeb = true;
        /// <summary>Additional Inspector-configured browser origins for CSWSH protection.</summary>
        [Tooltip("Additional browser origins for custom/private WebSocket clients. Full page URLs are accepted and normalized. Foxglove Desktop and non-browser clients do not send Origin and are always allowed.")]
        [SerializeField] private System.Collections.Generic.List<string> _allowedBrowserOrigins = new() { "https://app.foxglove.dev" };
        [SerializeField] private string _certificatePfxPath = "";
        [SerializeField] private string _certificatePassword = "";
        [SerializeField] private bool _rootCaDistributorEnabled;
        [SerializeField] private string _rootCaDistributorHost = "127.0.0.1";
        [SerializeField] private int _rootCaDistributorPort = 8766;
        [SerializeField] private string _rootCaFilePath = "";
        [SerializeField] private string _sharedToken = "";

        private Core.FoxgloveRuntime _runtime;
        private FoxgloveCertificateDistributor _certificateDistributor;
        private int _nextChannelId = FirstAutoChannelId;
        private bool _warnedNotRunning;
        private readonly System.Collections.Generic.List<MonoBehaviour> _disabledPublishers = new();
        private readonly System.Collections.Concurrent.ConcurrentQueue<ClientEvent> _clientEvents = new();

        /// <summary>Current nanosecond timestamp for publish calls.</summary>
        public ulong NowNs => _runtime?.NowNs ?? Schemas.FoxgloveTimeUtil.NowUnixTimeNs();

        /// <summary>Fires when a Foxglove client connects on the main thread.</summary>
        public event System.Action<uint> OnClientConnected;

        /// <summary>Fires when a Foxglove client disconnects on the main thread.</summary>
        public event System.Action<uint> OnClientDisconnected;

        /// <summary>
        /// Fires when a client-published message arrives on the main thread.
        /// </summary>
        public event System.Action<uint, uint, string, byte[]> OnClientMessage;

        /// <summary>Fires when a replay message is forwarded on the main thread.</summary>
        public event System.Action<string, byte[]> OnReplayMessage;

        private readonly System.Collections.Generic.Dictionary<(string topic, string schemaName, string encoding), uint> _channelCache
            = new System.Collections.Generic.Dictionary<(string, string, string), uint>();

        private System.Action<string, byte[]> _replayForwarder;
        private System.Action<uint, uint, string, byte[]> _clientMessageForwarder;

        /// <summary>Current coordinate mode, read from Inspector or code.</summary>
        public CoordinateMode ActiveCoordinateMode => _coordinateMode;

        /// <summary>
        /// Converts a Unity position to Foxglove coordinates.
        /// </summary>
        /// <param name="p">Position in Unity coordinates.</param>
        /// <returns>The converted position, or the original value in left-handed mode.</returns>
        public Vector3 UnityToFoxglovePosition(Vector3 p)
            => _coordinateMode == CoordinateMode.RightHand ? CoordinateConverter.UnityToFoxglovePosition(p) : p;

        /// <summary>
        /// Converts a Unity rotation to Foxglove coordinates.
        /// </summary>
        /// <param name="q">Rotation in Unity coordinates.</param>
        /// <returns>The converted rotation, or the original value in left-handed mode.</returns>
        public Quaternion UnityToFoxgloveRotation(Quaternion q)
            => _coordinateMode == CoordinateMode.RightHand ? CoordinateConverter.UnityToFoxgloveRotation(q) : q;

        /// <summary>
        /// Converts a Foxglove position to Unity coordinates.
        /// </summary>
        /// <param name="p">Position in Foxglove coordinates.</param>
        /// <returns>The converted position, or the original value in left-handed mode.</returns>
        public Vector3 FoxgloveToUnityPosition(Vector3 p)
            => _coordinateMode == CoordinateMode.RightHand ? CoordinateConverter.FoxgloveToUnityPosition(p) : p;

        /// <summary>
        /// Converts a Foxglove rotation to Unity coordinates.
        /// </summary>
        /// <param name="q">Rotation in Foxglove coordinates.</param>
        /// <returns>The converted rotation, or the original value in left-handed mode.</returns>
        public Quaternion FoxgloveToUnityRotation(Quaternion q)
            => _coordinateMode == CoordinateMode.RightHand ? CoordinateConverter.FoxgloveToUnityRotation(q) : q;

        /// <summary>The backing Foxglove runtime, or null after disposal.</summary>
        public Core.FoxgloveRuntime Runtime => _runtime;

        /// <summary>True if the WebSocket server is currently running.</summary>
        public bool IsRunning => _runtime?.Session?.IsRunning ?? false;

        /// <summary>
        /// Gets a read-only snapshot of transport client and queue health.
        /// </summary>
        /// <returns>A transport stats snapshot, or an unsupported snapshot when stats are unavailable.</returns>
        public Transport.TransportStatsSnapshot GetTransportStatsSnapshot()
        {
            return _runtime?.GetTransportStatsSnapshot() ?? Transport.TransportStatsSnapshot.Unsupported;
        }

        /// <summary>Global default publisher encoding.</summary>
        public GlobalEncoding DefaultPublisherEncoding => _defaultPublisherEncoding;

        /// <summary>Whether individual publishers can override the global encoding.</summary>
        public bool AllowPublisherOverride => _allowPublisherOverride;

        /// <summary>
        /// True when replay is active and live publisher output should be suppressed to avoid duplicate topic advertisements.
        /// </summary>
        public bool SuppressLivePublishersForReplay =>
            _disableLivePublishers && (_runtime?.ReplayEnabled ?? false);

        /// <summary>
        /// Creates the runtime and resolves mutually exclusive recording and replay settings.
        /// </summary>
        private void Awake()
        {
            if (_runInBackground)
            {
                Application.runInBackground = true;
            }

            var logger = new UnityLogger();
            var transport = CreateTransport(logger);
            _runtime = new Core.FoxgloveRuntime(transport, new SystemClock(), new DefaultSchemaRegistry(), logger);

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

        /// <summary>
        /// Queues a transport connect event for main-thread delivery.
        /// </summary>
        /// <param name="id">Connected Foxglove client identifier.</param>
        private void EnqueueConnect(uint id) =>
            _clientEvents.Enqueue(new ClientEvent { ClientId = id, IsConnect = true });

        /// <summary>
        /// Queues a transport disconnect event for main-thread delivery.
        /// </summary>
        /// <param name="id">Disconnected Foxglove client identifier.</param>
        private void EnqueueDisconnect(uint id) =>
            _clientEvents.Enqueue(new ClientEvent { ClientId = id, IsConnect = false });

        /// <summary>
        /// Starts the server automatically when configured to start on enable.
        /// </summary>
        private void OnEnable()
        {
            if (_startOnEnable)
            {
                StartServer();
            }
        }

        /// <summary>
        /// Ticks the runtime and drains transport-thread events onto the Unity main thread.
        /// </summary>
        private void Update()
        {
            _runtime?.Tick();
            while (_clientEvents.TryDequeue(out var evt))
            {
                if (evt.IsMessage)
                {
                    OnClientMessage?.Invoke(evt.ClientId, evt.ChannelId, evt.Topic, evt.Payload);
                }
                else if (evt.IsConnect)
                {
                    OnClientConnected?.Invoke(evt.ClientId);
                }
                else
                {
                    OnClientDisconnected?.Invoke(evt.ClientId);
                }
            }
        }

        /// <summary>
        /// Stops the server when the component is disabled without restoring replay-disabled publishers.
        /// </summary>
        private void OnDisable()
        {
            StopServer(restoreLivePublishers: false);
        }

        /// <summary>
        /// Stops and disposes runtime-owned resources when the component is destroyed.
        /// </summary>
        private void OnDestroy()
        {
            StopServer(restoreLivePublishers: false);
            _certificateDistributor?.Dispose();
            _certificateDistributor = null;
            _runtime?.Dispose();
            _runtime = null;
        }

        /// <summary>
        /// Absolute path to the Unity project root that contains the Assets directory.
        /// </summary>
        private static string ProjectRoot => System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, ".."));

        /// <summary>
        /// Resolves a project-relative path against <see cref="ProjectRoot"/>.
        /// </summary>
        /// <param name="path">Absolute path, project-relative path, or an empty path.</param>
        /// <returns>The original absolute/empty path, or an absolute project-root path.</returns>
        private static string ResolveProjectPath(string path)
        {
            if (string.IsNullOrEmpty(path) || System.IO.Path.IsPathRooted(path))
            {
                return path;
            }

            return System.IO.Path.GetFullPath(System.IO.Path.Combine(ProjectRoot, path));
        }

        /// <summary>
        /// Registers a runtime parameter.
        /// </summary>
        /// <param name="name">Parameter path, for example "/cube/color".</param>
        /// <param name="value">Initial value as a JToken.</param>
        /// <param name="type">Foxglove type string, for example "number[]".</param>
        /// <param name="writable">Whether Foxglove clients can modify this parameter.</param>
        public void RegisterParameter(string name, Newtonsoft.Json.Linq.JToken value, string type, bool writable)
        {
            _runtime?.RegisterParameter(name, value, type, writable);
        }

        /// <summary>
        /// Registers a service.
        /// </summary>
        /// <param name="descriptor">Service descriptor with name, type, request schemas, and response schemas.</param>
        /// <returns>The service identifier, or 0 when the runtime is not available.</returns>
        public uint RegisterService(Unity.FoxgloveSDK.Protocol.ServiceDescriptor descriptor)
        {
            return _runtime?.RegisterService(descriptor) ?? 0;
        }

        /// <summary>
        /// Unregisters a service.
        /// </summary>
        /// <param name="serviceId">Service identifier returned by <see cref="RegisterService"/>.</param>
        /// <returns>True when the service was registered and removed.</returns>
        public bool UnregisterService(uint serviceId)
        {
            return _runtime?.UnregisterService(serviceId) == true;
        }
    }

    /// <summary>
    /// Defines an asset root mapping for fetchAsset requests.
    /// </summary>
    [System.Serializable]
    public struct AssetRootDefinition
    {
        /// <summary>
        /// Default maximum fetchable asset size in megabytes.
        /// </summary>
        private const float DefaultMaxMegabytes = 16f;

        /// <summary>
        /// Converts megabytes to kilobytes.
        /// </summary>
        private const float KilobytesPerMegabyte = 1024f;

        /// <summary>
        /// Converts kilobytes to bytes.
        /// </summary>
        private const float BytesPerKilobyte = 1024f;

        /// <summary>
        /// URI prefix associated with this asset root.
        /// </summary>
        [Tooltip("URI prefix, e.g. asset://demo/")]
        public string uriPrefix;

        /// <summary>
        /// Local folder path, relative to the project root or absolute.
        /// </summary>
        [Tooltip("Local folder path (relative to project root or absolute)")]
        public string localRoot;

        /// <summary>
        /// Maximum fetchable file size in megabytes.
        /// </summary>
        [Tooltip("Maximum file size in MB (16 MB recommended)")]
        public float maxMB;

        /// <summary>
        /// Maximum fetchable file size in bytes.
        /// </summary>
        public float MaxBytesOrDefault => (maxMB > 0 ? maxMB : DefaultMaxMegabytes) * KilobytesPerMegabyte * BytesPerKilobyte;
    }

    /// <summary>
    /// Transport event queued for main-thread delivery.
    /// </summary>
    internal struct ClientEvent
    {
        /// <summary>
        /// Foxglove client identifier associated with the event.
        /// </summary>
        public uint ClientId;

        /// <summary>
        /// Client-advertised channel identifier for message events.
        /// </summary>
        public uint ChannelId;

        /// <summary>
        /// Client-advertised topic name for message events.
        /// </summary>
        public string Topic;

        /// <summary>
        /// Client-published payload bytes for message events.
        /// </summary>
        public byte[] Payload;

        /// <summary>
        /// True when the event represents a client connection.
        /// </summary>
        public bool IsConnect;

        /// <summary>
        /// True when the event represents a client-published message.
        /// </summary>
        public bool IsMessage;
    }
}
