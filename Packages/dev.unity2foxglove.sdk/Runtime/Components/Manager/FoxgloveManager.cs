// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components
// Purpose: MonoBehaviour entry point that manages FoxgloveRuntime lifecycle
// within Unity's game loop. Exposes Inspector-configurable settings for
// WebSocket server, coordinate mode, asset roots, playback control, MCAP
// recording, and MCAP replay.

using System.Collections.Generic;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Ros2Bridge;
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
    [DisallowMultipleComponent]
    public partial class FoxgloveManager : MonoBehaviour
    {
        /// <summary>
        /// First channel identifier assigned by manager-owned manual publishing APIs.
        /// </summary>
        private const int FirstAutoChannelId = 1;
        private const int MaxRecordingChunkSizeKB = int.MaxValue / 1024;
        public const string SharedTokenEnvironmentVariable = "FOXGLOVE_SHARED_TOKEN";
        public const string CertificatePasswordEnvironmentVariable = "FOXGLOVE_CERTIFICATE_PASSWORD";
        public const string RemoteMcapFileServerTokenEnvironmentVariable = "FOXGLOVE_REMOTE_MCAP_TOKEN";
        public const string ReplayCursorBridgeTokenEnvironmentVariable = "FOXGLOVE_REPLAY_CURSOR_TOKEN";

        [Header("General")]
        [SerializeField] private string _serverName = "Unity Foxglove SDK";
        [SerializeField] private string _host = "127.0.0.1";
        [Range(1, 65535)]
        [SerializeField] private int _port = 8765;
        [SerializeField] private bool _startOnEnable = true;
        [SerializeField] private bool _runInBackground = true;
        [SerializeField] private FoxgloveTransportMode _transportMode = FoxgloveTransportMode.WebSocket;
        [SerializeField, HideInInspector] private FoxgloveTransportMode _transportModeBeforeOutputDisabled = FoxgloveTransportMode.WebSocket;
        [Tooltip("When unchecked, the WebSocket server is disabled (transport = None).")]
        [SerializeField] private bool _foxgloveOutputEnabled = true;
        [Tooltip("Global policy flag for R2FU native DDS output. R2FU components query this at runtime. Default off; check to enable R2FU output.")]
        [SerializeField] private bool _ros2NativeEnabled;

        [Header("Diagnostics")]
        [Tooltip("Enable optional Unity Profiler markers for SDK diagnostics. Leave disabled for normal runs.")]
        [SerializeField] private bool _profilingEnabled;

        [Header("Publish Rate")]
        [Tooltip("Default publish rate used by publishers that choose the manager default. Use <= 0 to publish every eligible frame.")]
        [SerializeField] private float _defaultPublishRateHz = 10f;
        [Header("Publisher Encoding")]
        [Tooltip("Global default encoding for publishers that support it.")]
        [SerializeField] private GlobalEncoding _defaultPublisherEncoding = GlobalEncoding.Protobuf;
        [Tooltip("When enabled, individual publishers can override the global default.")]
        [SerializeField] private bool _allowPublisherOverride = true;

        [Tooltip("Enable the optional localhost ROS2 Bridge mirror output. Normal Foxglove WebSocket output is unchanged.")]
        [SerializeField] private bool _ros2BridgeEnabled;
        [SerializeField] private string _ros2BridgeHost = "127.0.0.1";
        [SerializeField, Min(1)] private int _ros2BridgePort = 8767;
        [SerializeField] private bool _ros2BridgeAutoConnect = true;
        [SerializeField] private bool _defaultRos2BridgeOutputEnabled;
        [SerializeField] private bool _allowPublisherRos2BridgeOverride = true;
        [Tooltip("Optional ROS2 Bridge namespace prefix, for example /robot1. WebSocket topics are unchanged.")]
        [SerializeField] private string _ros2BridgeNamespace = "";
        private string _cachedRos2BridgeNamespace;
        private bool _ros2BridgeNamespaceCacheValid;
        [SerializeField] private Ros2BridgeQosPreset _ros2BridgeQosPreset = Ros2BridgeQosPreset.ReliableDefault;
        [SerializeField] private Ros2BridgeReliability _ros2BridgeCustomReliability = Ros2BridgeReliability.Reliable;
        [SerializeField] private Ros2BridgeDurability _ros2BridgeCustomDurability = Ros2BridgeDurability.Volatile;
        [SerializeField, Min(1)] private int _ros2BridgeCustomDepth = 10;
        [SerializeField, Min(1)] private int _ros2BridgeQueueCapacity = 1024;
        [SerializeField, Min(1)] private int _ros2BridgeReconnectIntervalMs = 1000;
        [SerializeField, Min(1)] private int _ros2BridgeSendTimeoutMs = 1000;

        [Header("Coordinate System")]
        [SerializeField] private CoordinateMode _coordinateMode = CoordinateMode.LeftHand;

        [SerializeField] private AssetRootDefinition[] _assetRoots = { };

        [SerializeField] private bool _enablePlaybackControl;
        [Range(-3600f, 3600f)]
        [SerializeField] private float _playbackStartOffsetSeconds = 0;
        [Min(0f)]
        [SerializeField] private float _playbackDurationSeconds = 60;

        [Header("MCAP Recording")]
        [SerializeField] private bool _enableRecording;
        [SerializeField] private string _recordingPrefix = "foxglove";
        [Tooltip("Leave empty to save in <project>/Recordings/ . Shown path is the resolved default at runtime.")]
        [SerializeField] private string _recordingDirectory = "";
        [Range(1, MaxRecordingChunkSizeKB)]
        [SerializeField] private int _recordingChunkSizeKB = 1024;
        [SerializeField] private McapCompressionMode _recordingCompression = McapCompressionMode.None;

        [Header("MCAP Replay")]
        [SerializeField] private bool _enableReplay;
        [SerializeField] private string _replayFilePath = "";
        [SerializeField] private bool _replayAutoPlay;
        [SerializeField] private bool _disableLivePublishers;
        [Tooltip("Serve the selected replay MCAP as a loopback URL so Foxglove Desktop owns the replay timeline.")]
        [SerializeField] private bool _enableRemoteMcapFileServer;
        [SerializeField] private string _remoteMcapFileServerHost = "127.0.0.1";
        [SerializeField, Min(1)] private int _remoteMcapFileServerPort = 8891;
        [SerializeField] private string _remoteMcapFileServerSourceId = "local-mcap";
        [Tooltip("Optional Remote MCAP bearer token. Prefer FOXGLOVE_REMOTE_MCAP_TOKEN for credentials that must not be serialized into scenes.")]
        [SerializeField, HideInInspector] private string _remoteMcapFileServerToken = "";
        [Tooltip("Optional loopback endpoint that accepts Foxglove extension timeline cursor updates and applies them to Unity replay on the next runtime tick.")]
        [SerializeField] private bool _enableReplayCursorBridge = false;
        [SerializeField] private string _replayCursorBridgeHost = "127.0.0.1";
        [SerializeField, Min(1)] private int _replayCursorBridgePort = 8892;
        [Tooltip("Optional replay cursor bearer token. Prefer FOXGLOVE_REPLAY_CURSOR_TOKEN for credentials that must not be serialized into scenes.")]
        [SerializeField, HideInInspector] private string _replayCursorBridgeToken = "";

        [SerializeField] private SchemaIdentityModeSource _identityModeSource = SchemaIdentityModeSource.ProjectSettings;
        [SerializeField] private SchemaIdentityMode _identityModeOverride = SchemaIdentityMode.Off;
        [SerializeField, HideInInspector] private SchemaIdentityMode _projectSettingsIdentityMode = SchemaIdentityMode.Off;
        [SerializeField, HideInInspector] private string _schemaEvidenceRoot = "Assets/Generated";

        [Header("Security")]
        [Tooltip("Allow hosted Foxglove Web at https://app.foxglove.dev. This is independent of project, user, layout, and query string.")]
        [SerializeField] private bool _allowHostedFoxgloveWeb = true;
        /// <summary>Additional Inspector-configured browser origins for CSWSH protection.</summary>
        [Tooltip("Additional browser origins for custom/private WebSocket clients. Full page URLs are accepted and normalized. Foxglove Desktop and non-browser clients do not send Origin and are always allowed.")]
        [SerializeField] private System.Collections.Generic.List<string> _allowedBrowserOrigins = new();
        [Tooltip("Optional WSS certificate path. Avoid committing machine-local or private certificate paths in shared scenes.")]
        [SerializeField] private string _certificatePfxPath = "";
        [Tooltip("Optional PFX password. Prefer FOXGLOVE_CERTIFICATE_PASSWORD for credentials that must not be serialized into scenes.")]
        [SerializeField, HideInInspector] private string _certificatePassword = "";
        [SerializeField] private bool _rootCaDistributorEnabled;
        [SerializeField] private string _rootCaDistributorHost = "127.0.0.1";
        [Range(1, 65535)]
        [SerializeField] private int _rootCaDistributorPort = 8766;
        [SerializeField] private string _rootCaFilePath = "";
        [Tooltip("Optional WebSocket bearer token. Prefer FOXGLOVE_SHARED_TOKEN for credentials that must not be serialized into scenes.")]
        [SerializeField, HideInInspector] private string _sharedToken = "";

        private Core.FoxgloveRuntime _runtime;
        private Ros2BridgeRuntime _ros2BridgeRuntime;
        private UnityReplayCursorEndpoint _replayCursorEndpoint;
        private bool _replayCursorEndpointLoggedFirstCursor;
        private bool _replayCursorEndpointLoggedUnavailable;
        private FoxgloveCertificateDistributor _certificateDistributor;
        private readonly System.Collections.Generic.List<MonoBehaviour> _disabledPublishers = new();
        private readonly ConnectionRuntimeState _connectionState = new ConnectionRuntimeState(FirstAutoChannelId);
        private readonly RecordingRuntimeState _recordingState = new RecordingRuntimeState();
        private readonly ReplayRuntimeState _replayState = new ReplayRuntimeState();
        private readonly StatisticsRuntimeState _statisticsState = new StatisticsRuntimeState();
        private readonly WarningDebounceState _warningDebounceState = new WarningDebounceState();
        private readonly FoxgloveSharedSensorClock _sharedSensorClock = new FoxgloveSharedSensorClock();

        /// <summary>Current nanosecond timestamp for publish calls.</summary>
        public ulong NowNs => _runtime?.NowNs ?? Schemas.FoxgloveTimeUtil.NowUnixTimeNs();

        /// <summary>
        /// Convert a Unity physics timestamp to a shared, manager-level monotonic
        /// nanosecond timestamp for sensors so LiDAR / IMU timestamps stay in
        /// one timeline.
        /// </summary>
        /// <param name="physicsTimeSeconds">Unity physics timeline time in seconds.</param>
        /// <returns>Nanoseconds, anchored once per play session.</returns>
        public ulong GetSharedSensorClockUnixTime(double physicsTimeSeconds)
            => _sharedSensorClock.GetUnixTime(physicsTimeSeconds, NowNs);

        /// <summary>Fires when a Foxglove client connects on the main thread.</summary>
        public event System.Action<uint> OnClientConnected;

        /// <summary>Fires when a Foxglove client disconnects on the main thread.</summary>
        public event System.Action<uint> OnClientDisconnected;

        /// <summary>
        /// Fires when a client-published message arrives on the main thread.
        /// </summary>
        public event System.Action<uint, uint, string, byte[]> OnClientMessage;

        /// <summary>
        /// Fires when a client-published message arrives with its advertised wire encoding.
        /// </summary>
        public event System.Action<uint, uint, string, string, byte[]> OnClientMessageWithEncoding;

        /// <summary>Fires when a replay message is forwarded on the main thread.</summary>
        public event System.Action<string, byte[]> OnReplayMessage;

        /// <summary>Fires when replay data is forwarded with channel, schema, and log-time context.</summary>
        public event System.Action<ReplayMessageContext> OnReplayMessageContext;

        /// <summary>Fires after a replay batch has been forwarded to scene listeners.</summary>
        public event System.Action<ReplayBatchContext> OnReplayBatchCompleted;

        private readonly System.Collections.Generic.Dictionary<(string topic, string schemaName, string encoding, string schemaEncoding), uint> _channelCache
            = new System.Collections.Generic.Dictionary<(string, string, string, string), uint>();

        private System.Action<string, byte[]> _replayForwarder;
        private System.Action<ReplayMessageContext> _replayContextForwarder;
        private System.Action<ReplayBatchContext> _replayBatchForwarder;
        private System.Action<uint, uint, string, string, byte[]> _clientMessageForwarder;

        /// <summary>Current coordinate mode, read from Inspector or code.</summary>
        public CoordinateMode ActiveCoordinateMode => _coordinateMode;

        /// <summary>True when the Inspector's optional Unity Profiler marker hook is enabled.</summary>
        public bool ProfilingEnabled => _profilingEnabled;

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

        /// <summary>Effective schema identity policy for recording and replay startup.</summary>
        public SchemaIdentityMode EffectiveSchemaIdentityMode =>
            _identityModeSource == SchemaIdentityModeSource.Override
                ? _identityModeOverride
                : _projectSettingsIdentityMode;

        /// <summary>Project-relative or absolute root containing current schema evidence files.</summary>
        public string SchemaEvidenceRoot => _schemaEvidenceRoot;

        /// <summary>True if the WebSocket server is currently running.</summary>
        public bool IsRunning => _runtime?.Session?.IsRunning ?? false;

        /// <summary>Return the behavior class loaded for a replay channel id.</summary>
        public ReplayChannelBehavior GetReplayChannelBehavior(ushort channelId)
            => _runtime?.GetReplayChannelBehavior(channelId) ?? ReplayChannelBehavior.NotLoaded;

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

        /// <summary>Global default publish rate for publishers that use manager rate policy.</summary>
        public float DefaultPublishRateHz => _defaultPublishRateHz;

        /// <summary>Whether individual publishers can override the global encoding.</summary>
        public bool AllowPublisherOverride => _allowPublisherOverride;

        /// <summary>Whether the optional ROS2 Bridge mirror output is enabled.</summary>
        public bool Ros2BridgeEnabled => _ros2BridgeEnabled;

        /// <summary>Global policy flag for R2FU native DDS output. R2FU components check this at runtime.</summary>
        public bool Ros2NativeEnabled => _ros2NativeEnabled;

        /// <summary>Manager-level default for publisher bridge output when the bridge master switch is enabled.</summary>
        public bool DefaultRos2BridgeOutputEnabled => _defaultRos2BridgeOutputEnabled;

        /// <summary>Whether individual publishers can override the manager ROS2 Bridge output default.</summary>
        public bool AllowPublisherRos2BridgeOverride => _allowPublisherRos2BridgeOverride;

        /// <summary>Optional manager-level namespace applied only to ROS2 Bridge output topics.</summary>
        public string Ros2BridgeNamespace
        {
            get
            {
                if (!_ros2BridgeNamespaceCacheValid)
                {
                    _cachedRos2BridgeNamespace = Ros2BridgeTopicProfile.TryNormalizeRos2BridgeNamespace(
                        _ros2BridgeNamespace, out var normalized, out _)
                        ? normalized
                        : string.Empty;
                    _ros2BridgeNamespaceCacheValid = true;
                }

                return _cachedRos2BridgeNamespace;
            }
        }

        private void InvalidateRos2BridgeNamespaceCache()
        {
            _ros2BridgeNamespaceCacheValid = false;
        }

        /// <summary>Manager-level ROS2 Bridge QoS preset.</summary>
        public Ros2BridgeQosPreset Ros2BridgeQosPreset => _ros2BridgeQosPreset;

        /// <summary>Resolve the active ROS2 Bridge QoS profile.</summary>
        public Ros2BridgeQosProfile ResolveRos2BridgeQos()
            => Ros2BridgeQosProfile.Resolve(
                _ros2BridgeQosPreset,
                _ros2BridgeCustomReliability,
                _ros2BridgeCustomDurability,
                _ros2BridgeCustomDepth);

        /// <summary>Resolve an effective ROS2 Bridge topic without mutating the publisher's WebSocket topic.</summary>
        public bool TryResolveRos2BridgeTopic(string publisherTopic, string overrideTopic, out string effectiveTopic, out string error)
            => Ros2BridgeTopicProfile.TryResolveRos2BridgeTopic(_ros2BridgeNamespace, publisherTopic, overrideTopic, out effectiveTopic, out error);

        /// <summary>Resolve an effective ROS2 Bridge topic, or an empty string when the profile is invalid.</summary>
        public string ResolveRos2BridgeTopic(string publisherTopic, string overrideTopic)
            => TryResolveRos2BridgeTopic(publisherTopic, overrideTopic, out var effectiveTopic, out _)
                ? effectiveTopic
                : string.Empty;

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

            ConfigureProfiler();
            EnsureRuntimeCreated();
            CreateRos2BridgeRuntime();

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
        /// Applies the Inspector profiler toggle before runtime work starts.
        /// </summary>
        private void ConfigureProfiler()
        {
            if (_profilingEnabled)
            {
                FoxgloveProfiler.SetGlobal(this, UnityProfilerAdapter.Instance);
                return;
            }

            FoxgloveProfiler.ResetGlobal(this);
        }

        /// <summary>
        /// Creates the runtime if Unity lifecycle callbacks reach StartServer before Awake initialization.
        /// </summary>
        private void EnsureRuntimeCreated()
        {
            if (_runtime != null)
            {
                return;
            }

            var logger = new UnityLogger();
            // Runtime services (recording, replay, ROS2 Bridge policy) should be available
            // even when Foxglove output is temporarily disabled.
            var transport = CreateTransport(logger);
            _runtime = new Core.FoxgloveRuntime(transport, new SystemClock(), new DefaultSchemaRegistry(), logger);
        }

        private void OnValidate()
        {
            InvalidateRos2BridgeNamespaceCache();
            InvalidateResolvedReplayFilePathCache();
            _port = ManagerConfigValidator.ClampTcpPort(_port);
            _rootCaDistributorPort = ManagerConfigValidator.ClampTcpPort(_rootCaDistributorPort);
            _recordingChunkSizeKB = Mathf.Clamp(_recordingChunkSizeKB, 1, MaxRecordingChunkSizeKB);
            _ros2BridgePort = ManagerConfigValidator.ClampTcpPort(_ros2BridgePort);
            _remoteMcapFileServerPort = ManagerConfigValidator.ClampTcpPort(_remoteMcapFileServerPort);
            _replayCursorBridgePort = ManagerConfigValidator.ClampTcpPort(_replayCursorBridgePort);
            if (_enableRemoteMcapFileServer)
            {
                _replayAutoPlay = false;
            }

            _ros2BridgeCustomDepth = ManagerConfigValidator.ClampAtLeastOne(_ros2BridgeCustomDepth);
            _ros2BridgeQueueCapacity = ManagerConfigValidator.ClampAtLeastOne(_ros2BridgeQueueCapacity);
            _ros2BridgeReconnectIntervalMs = ManagerConfigValidator.ClampAtLeastOne(_ros2BridgeReconnectIntervalMs);
            _ros2BridgeSendTimeoutMs = ManagerConfigValidator.ClampAtLeastOne(_ros2BridgeSendTimeoutMs);
            if (!_foxgloveOutputEnabled)
            {
                if (_transportMode != FoxgloveTransportMode.None)
                {
                    _transportModeBeforeOutputDisabled = _transportMode;
                }

                _transportMode = FoxgloveTransportMode.None;
            }
            else if (_transportMode == FoxgloveTransportMode.None)
            {
                _transportMode = _transportModeBeforeOutputDisabled == FoxgloveTransportMode.None
                    ? FoxgloveTransportMode.WebSocket
                    : _transportModeBeforeOutputDisabled;
            }
            else
            {
                _transportModeBeforeOutputDisabled = _transportMode;
            }
        }

        /// <summary>
        /// Starts the server automatically when configured to start on enable.
        /// </summary>
        private void OnEnable()
        {
            BeginFoxRunSubscriptionSessionIfNeeded();

            if (_startOnEnable)
            {
                StartServer();
            }

            StartRos2BridgeIfNeeded();
            InitializeOutputModeWatchers();
        }

        /// <summary>
        /// Ticks the runtime and drains transport-thread events onto the Unity main thread.
        /// </summary>
        private void Update()
        {
            SyncFoxRunSubscriptionSession();

            var frameStallStageStart = BeginFrameStallStageTiming();
            _runtime?.Tick();
            RecordFrameStallStageTiming(ref frameStallStageStart, FrameStallStage.RuntimeTick);
            DrainClientEventQueue(_clientLifecycleEvents);
            RecordFrameStallStageTiming(ref frameStallStageStart, FrameStallStage.ClientLifecycleDrain);
            DrainClientEventQueue(_clientMessageEvents);
            RecordFrameStallStageTiming(ref frameStallStageStart, FrameStallStage.ClientMessageDrain);
            FlushPublishCadenceDiagnosticsIfNeeded();
            RecordFrameStallStageTiming(ref frameStallStageStart, FrameStallStage.PublishCadenceDiagnostics);
            ApplyLiveOutputModeWatchers();
            RecordFrameStallStageTiming(ref frameStallStageStart, FrameStallStage.LiveOutputModeWatchers);
            RefreshRemoteMcapFileServerIfNeeded();
            RecordFrameStallStageTiming(ref frameStallStageStart, FrameStallStage.RemoteMcapRefresh);
            RefreshReplayCursorEndpointIfNeeded();
            RecordFrameStallStageTiming(ref frameStallStageStart, FrameStallStage.ReplayCursorEndpointRefresh);
            RecordFrameStallDiagnosticsIfNeeded();
        }

        /// <summary>
        /// Stops the server when the component is disabled and restores publishers
        /// that replay mode disabled for this manager session.
        /// </summary>
        private void OnDisable()
        {
            EndFoxRunSubscriptionSession();
            _ros2BridgeRuntime?.Stop();
            StopServer(restoreLivePublishers: true);
            _connectionState.OutputModeWatchInitialized = false;
            FoxgloveProfiler.ResetGlobal(this);
        }

        /// <summary>
        /// Stops and disposes runtime-owned resources when the component is destroyed.
        /// </summary>
        private void OnDestroy()
        {
            EndFoxRunSubscriptionSession();
            StopServer(restoreLivePublishers: true);
            _ros2BridgeRuntime?.Dispose();
            _ros2BridgeRuntime = null;
            _replayCursorEndpoint?.Dispose();
            _replayCursorEndpoint = null;
            _certificateDistributor?.Dispose();
            _certificateDistributor = null;
            _runtime?.Dispose();
            _runtime = null;
            FoxgloveProfiler.ResetGlobal(this);
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

        private void InvalidateResolvedReplayFilePathCache()
        {
            _replayState.InvalidateResolvedReplayFilePathCache();
        }

        private void CreateRos2BridgeRuntime()
        {
            try
            {
                _ros2BridgeRuntime?.Dispose();
                _ros2BridgeRuntime = new Ros2BridgeRuntime(
                    string.IsNullOrWhiteSpace(_ros2BridgeHost) ? "127.0.0.1" : _ros2BridgeHost,
                    ManagerConfigValidator.ClampTcpPort(_ros2BridgePort),
                    ManagerConfigValidator.ClampAtLeastOne(_ros2BridgeQueueCapacity),
                    ManagerConfigValidator.ClampAtLeastOne(_ros2BridgeReconnectIntervalMs),
                    ManagerConfigValidator.ClampAtLeastOne(_ros2BridgeSendTimeoutMs));
                _connectionState.Ros2BridgeSetupError = string.Empty;
            }
            catch (System.Exception ex)
            {
                _ros2BridgeRuntime = null;
                _connectionState.Ros2BridgeSetupError = ex.Message;
                Debug.LogWarning(StatusTextBuilder.CreateRos2BridgeDisabledWarning(ex.Message));
            }
        }

        private void StartRos2BridgeIfNeeded()
        {
            if (!_ros2BridgeEnabled)
                return;

            if (_ros2BridgeRuntime == null)
                CreateRos2BridgeRuntime();

            _ros2BridgeRuntime?.Start(enabled: true, autoConnect: _ros2BridgeAutoConnect);
        }

        /// <summary>
        /// Captures the first observed runtime output-mode state.
        /// </summary>
        private void InitializeOutputModeWatchers()
        {
            _connectionState.LastFoxgloveOutputEnabled = _foxgloveOutputEnabled;
            _connectionState.LastRos2BridgeEnabled = _ros2BridgeEnabled;
            _connectionState.OutputModeWatchInitialized = true;
            InvalidateRos2BridgeNamespaceCache();
        }

        /// <summary>
        /// Applies live Output Mode toggles (WebSocket server + ROS2 bridge) without recreating the runtime.
        /// </summary>
        private void ApplyLiveOutputModeWatchers()
        {
            if (!_connectionState.OutputModeWatchInitialized)
            {
                InitializeOutputModeWatchers();
                return;
            }

            if (_connectionState.LastFoxgloveOutputEnabled != _foxgloveOutputEnabled)
            {
                if (_foxgloveOutputEnabled)
                {
                    StartServer();
                }
                else
                {
                    StopServer(restoreLivePublishers: true);
                }
            }

            if (_connectionState.LastRos2BridgeEnabled != _ros2BridgeEnabled)
            {
                if (_ros2BridgeEnabled)
                {
                    StartRos2BridgeIfNeeded();
                }
                else
                {
                    _ros2BridgeRuntime?.Stop();
                }
            }

            _connectionState.LastFoxgloveOutputEnabled = _foxgloveOutputEnabled;
            _connectionState.LastRos2BridgeEnabled = _ros2BridgeEnabled;
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
        private const double DefaultMaxMegabytes = 16d;

        /// <summary>
        /// Converts megabytes to kilobytes.
        /// </summary>
        private const double KilobytesPerMegabyte = 1024d;

        /// <summary>
        /// Converts kilobytes to bytes.
        /// </summary>
        private const double BytesPerKilobyte = 1024d;

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
        public long MaxBytesOrDefault
        {
            get
            {
                var megabytes = maxMB > 0 ? maxMB : DefaultMaxMegabytes;
                var bytes = megabytes * KilobytesPerMegabyte * BytesPerKilobyte;
                if (bytes >= long.MaxValue)
                    return long.MaxValue;

                return bytes <= 0 ? 0 : (long)bytes;
            }
        }
    }

}
