// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Manager
// Purpose: Custom Inspector for FoxgloveManager workflow settings and
// path helpers.

using System.IO;
using System.Reflection;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Ros2Bridge;
using Unity.FoxgloveSDK.Transport;
using UnityEngine;
using UnityEditor;

namespace Unity.FoxgloveSDK.Editor
{
    /// <summary>
    /// Custom Inspector for <c>FoxgloveManager</c> that groups the growing
    /// runtime, recording, replay, security, and transport settings into
    /// readable sections while preserving the original serialized fields.
    /// </summary>
    [CustomEditor(typeof(Components.FoxgloveManager))]
    public partial class FoxgloveManagerEditor : UnityEditor.Editor
    {
        private bool _connectionSecurityExpanded;
        private bool _dataTransportExpanded;
        private bool _dataTransportPublishExpanded;
        private bool _dataTransportSubscribeExpanded;
        private bool _dataTransportNativeRuntimeExpanded;
        private bool _dataTransportRos2BridgeExpanded;
        private bool _mcapExpanded;
        private bool _foxServicesExpanded;
        private bool _schemaEvidenceAdvancedExpanded;
        private bool _remoteFileAccessExpanded = true;
        private bool _diagnosticsExpanded;
        private const string LocalRootCaDistributorHost = "127.0.0.1";
        private const int LocalRootCaDistributorPort = 8766;
        private const string LocalRootCaPageUrl = "http://127.0.0.1:8766/";
        private const string CertificateBackendEditorPrefKey = "Unity2Foxglove.LocalDevCertificate.Backend";
        private const string OpenSslPathEditorPrefKey = "Unity2Foxglove.LocalDevCertificate.OpenSslPath";
        private const string InspectorFoldoutSessionPrefix = "Unity2Foxglove.FoxgloveManagerEditor.Foldout.";
        private static readonly string[] TransportModeLabels =
        {
            "Web Socket",
            "Secure Web Socket"
        };
        private static FoxgloveCertificateDistributor _editorRootCaDistributor;
        private static string _lastRootCaDistributorPath;
        private static string _lastRootCaDistributorHost;
        private static int _lastRootCaDistributorPort;
        private string _cachedRootCaFingerprintPath;
        private string _cachedRootCaFingerprint;
        private SerializedProperty _scriptProperty;
        private SerializedProperty _hostProperty;
        private SerializedProperty _portProperty;
        private SerializedProperty _transportModeProperty;
        private SerializedProperty _sharedTokenProperty;
        private SerializedProperty _startOnEnableProperty;
        private SerializedProperty _enableRecordingProperty;
        private SerializedProperty _enableReplayProperty;
        private SerializedProperty _foxgloveOutputEnabledProperty;
        private SerializedProperty _ros2NativeEnabledProperty;
        private SerializedProperty _ros2BridgeEnabledProperty;
        private SerializedProperty _enableFoxRunInboundProperty;
        private SerializedProperty _defaultFoxRunPublishEncodingProperty;
        private SerializedProperty _defaultFoxRunSubscriptionEncodingProperty;
        private SerializedProperty _defaultFoxRunSubscriptionProviderProperty;
        private SerializedProperty _defaultFoxRunRos2QosProperty;
        private SerializedProperty _foxRunRos2NativeCopyBudgetBytesProperty;
        private SerializedProperty _allowRemoteFoxRunInboundWithSharedTokenProperty;
        private SerializedProperty _certificatePfxPathProperty;
        private SerializedProperty _certificatePasswordProperty;
        private SerializedProperty _rootCaFilePathProperty;
        private SerializedProperty _rootCaDistributorEnabledProperty;
        private SerializedProperty _rootCaDistributorHostProperty;
        private SerializedProperty _rootCaDistributorPortProperty;
        private SerializedProperty _enableRemoteMcapFileServerProperty;
        private SerializedProperty _remoteMcapFileServerHostProperty;
        private SerializedProperty _remoteMcapFileServerPortProperty;
        private SerializedProperty _remoteMcapFileServerSourceIdProperty;
        private SerializedProperty _remoteMcapFileServerTokenProperty;
        private SerializedProperty _recordingDirectoryProperty;
        private SerializedProperty _replayFilePathProperty;
        private SerializedProperty _replayAutoPlayProperty;
        private SerializedProperty _identityModeSourceProperty;
        private SerializedProperty _identityModeOverrideProperty;
        private SerializedProperty _projectSettingsIdentityModeProperty;
        private SerializedProperty _schemaEvidenceRootProperty;
        private TransportStatsSnapshot _transportStatsThisRepaint = TransportStatsSnapshot.Unsupported;
        private int _transportStatsFrame = -1;
        private Components.FoxgloveServiceHub _cachedServiceHub;
        private int _cachedServiceSnapshotFrame = -1;
        private System.Collections.Generic.IReadOnlyList<Components.FoxgloveRegisteredServiceSnapshot> _cachedServiceSnapshots =
            System.Array.Empty<Components.FoxgloveRegisteredServiceSnapshot>();
        private string _cachedEndpointHost;
        private int _cachedEndpointPort;
        private bool _cachedEndpointSecure;
        private string _cachedEndpointToken;
        private string _cachedEndpoint;
        private string _cachedFoxgloveWebUrl;
        private string _cachedRedactedFoxgloveWebUrl;
        private string _cachedSecureUrl;
        private string _cachedRemoteHost;
        private int _cachedRemotePort;
        private string _cachedRemoteSourceId;
        private string _cachedRemoteBaseUrl;
        private string _cachedRemoteDirectFileUrl;
        private Ros2BridgeQosProfile _ros2BridgeQosThisRepaint = Ros2BridgeQosProfile.ReliableDefault;
        private Ros2BridgeStatsSnapshot _ros2BridgeStatsThisRepaint = Ros2BridgeStatsSnapshot.Disabled;
        private int _ros2BridgeStatsFrame = -1;

        static FoxgloveManagerEditor()
        {
            AssemblyReloadEvents.beforeAssemblyReload += StopEditorRootCaDistributor;
            AssemblyReloadEvents.beforeAssemblyReload += ResetOptionalR2fuRuntimeSelectorCache;
            EditorApplication.quitting += StopEditorRootCaDistributor;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private void OnEnable()
        {
            CacheSerializedProperties();
            LoadInspectorFoldoutState();
            InvalidateUrlCaches();
        }

        private void OnDisable()
        {
            _ros2BridgeHealthDrawer.Dispose();
            _mcapReplayPreflight.Dispose();
            _transportStatsThisRepaint = TransportStatsSnapshot.Unsupported;
            _transportStatsFrame = -1;
            _cachedServiceHub = null;
            _cachedServiceSnapshotFrame = -1;
            _cachedServiceSnapshots = System.Array.Empty<Components.FoxgloveRegisteredServiceSnapshot>();
            _ros2BridgeStatsThisRepaint = Ros2BridgeStatsSnapshot.Disabled;
            _ros2BridgeStatsFrame = -1;
            ClearTransportClientLabelCache();
        }

        private void CacheSerializedProperties()
        {
            _scriptProperty = serializedObject.FindProperty("m_Script");
            _hostProperty = serializedObject.FindProperty("_host");
            _portProperty = serializedObject.FindProperty("_port");
            _transportModeProperty = serializedObject.FindProperty("_transportMode");
            _sharedTokenProperty = serializedObject.FindProperty("_sharedToken");
            _startOnEnableProperty = serializedObject.FindProperty("_startOnEnable");
            _enableRecordingProperty = serializedObject.FindProperty("_enableRecording");
            _enableReplayProperty = serializedObject.FindProperty("_enableReplay");
            _foxgloveOutputEnabledProperty = serializedObject.FindProperty("_foxgloveOutputEnabled");
            _ros2NativeEnabledProperty = serializedObject.FindProperty("_ros2NativeEnabled");
            _ros2BridgeEnabledProperty = serializedObject.FindProperty("_ros2BridgeEnabled");
            _enableFoxRunInboundProperty = serializedObject.FindProperty("_enableFoxRunInbound");
            _defaultFoxRunPublishEncodingProperty = serializedObject.FindProperty("_defaultFoxRunPublishEncoding");
            _defaultFoxRunSubscriptionEncodingProperty = serializedObject.FindProperty("_defaultFoxRunSubscriptionEncoding");
            _defaultFoxRunSubscriptionProviderProperty = serializedObject.FindProperty("_defaultFoxRunSubscriptionProvider");
            _defaultFoxRunRos2QosProperty = serializedObject.FindProperty("_defaultFoxRunRos2Qos");
            _foxRunRos2NativeCopyBudgetBytesProperty = serializedObject.FindProperty("_foxRunRos2NativeCopyBudgetBytes");
            _allowRemoteFoxRunInboundWithSharedTokenProperty = serializedObject.FindProperty("_allowRemoteFoxRunInboundWithSharedToken");
            _certificatePfxPathProperty = serializedObject.FindProperty("_certificatePfxPath");
            _certificatePasswordProperty = serializedObject.FindProperty("_certificatePassword");
            _rootCaFilePathProperty = serializedObject.FindProperty("_rootCaFilePath");
            _rootCaDistributorEnabledProperty = serializedObject.FindProperty("_rootCaDistributorEnabled");
            _rootCaDistributorHostProperty = serializedObject.FindProperty("_rootCaDistributorHost");
            _rootCaDistributorPortProperty = serializedObject.FindProperty("_rootCaDistributorPort");
            _enableRemoteMcapFileServerProperty = serializedObject.FindProperty("_enableRemoteMcapFileServer");
            _remoteMcapFileServerHostProperty = serializedObject.FindProperty("_remoteMcapFileServerHost");
            _remoteMcapFileServerPortProperty = serializedObject.FindProperty("_remoteMcapFileServerPort");
            _remoteMcapFileServerSourceIdProperty = serializedObject.FindProperty("_remoteMcapFileServerSourceId");
            _remoteMcapFileServerTokenProperty = serializedObject.FindProperty("_remoteMcapFileServerToken");
            _recordingDirectoryProperty = serializedObject.FindProperty("_recordingDirectory");
            _replayFilePathProperty = serializedObject.FindProperty("_replayFilePath");
            _replayAutoPlayProperty = serializedObject.FindProperty("_replayAutoPlay");
            _identityModeSourceProperty = serializedObject.FindProperty("_identityModeSource");
            _identityModeOverrideProperty = serializedObject.FindProperty("_identityModeOverride");
            _projectSettingsIdentityModeProperty = serializedObject.FindProperty("_projectSettingsIdentityMode");
            _schemaEvidenceRootProperty = serializedObject.FindProperty("_schemaEvidenceRoot");
        }

        /// <summary>
        /// Draws a curated Inspector for Manager settings and runtime status.
        /// </summary>
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            Unity2FoxgloveSchemaEvidenceSettings.SyncSerializedManager(serializedObject);
            RefreshTransportStatsForRepaint();

            DrawScriptProperty();
            DrawCompactStatus();
            EnsureSecureSettingsVisible();

            DrawSection("Connection & Security", "ConnectionSecurity", ref _connectionSecurityExpanded, DrawConnectionSecuritySection);
            DrawSection("Data Transport", "DataTransport", ref _dataTransportExpanded, DrawDataTransportSection);
            DrawRecordingReplayWarning();
            DrawSection("MCAP Record & Replay", "Mcap", ref _mcapExpanded, DrawMcapSection);
            DrawSection("FoxServices", "FoxServices", ref _foxServicesExpanded, DrawFoxServicesSection);
            DrawSection("Diagnostics", "Diagnostics", ref _diagnosticsExpanded, DrawDiagnosticsSection);

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawScriptProperty()
        {
            var script = FindCachedProperty("m_Script");
            if (script == null) return;

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(script);
            }
        }

        private void DrawCompactStatus()
        {
            var manager = (Components.FoxgloveManager)target;
            var host = GetString("_host", "127.0.0.1");
            var port = GetInt("_port", 8765);
            var isSecure = IsSecureMode();
            var token = GetString("_sharedToken", "");
            RefreshWebUrlCache(host, port, isSecure, token);

            EditorGUILayout.Space();
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Status Summary", EditorStyles.boldLabel);
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.TextField("Endpoint", _cachedEndpoint);
                    EditorGUILayout.TextField("Foxglove Web URL", _cachedRedactedFoxgloveWebUrl);
                    EditorGUILayout.Toggle("Start On Enable", GetBool("_startOnEnable"));
                    EditorGUILayout.Toggle("Recording Enabled", GetBool("_enableRecording"));
                    EditorGUILayout.Toggle("Replay Enabled", GetBool("_enableReplay"));

                    if (Application.isPlaying && manager != null)
                    {
                        EditorGUILayout.Toggle("Running", manager.IsRunning);
                        var stats = GetTransportStatsForRepaint();
                        if (stats.Supported)
                        {
                            EditorGUILayout.IntField("Active Clients", stats.ActiveClientCount);
                            EditorGUILayout.LongField("Queued Frames", stats.TotalQueuedFrames);
                            EditorGUILayout.LongField("Dropped Data Frames", stats.TotalDroppedDataFrames);
                        }
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    var copyWebUrlLabel = string.IsNullOrEmpty(token)
                        ? new GUIContent("Copy Web URL")
                        : new GUIContent("Copy Web URL (with token)", "Copies the full Foxglove Web URL, including the shared token query parameter.");
                    if (GUILayout.Button(copyWebUrlLabel))
                        EditorGUIUtility.systemCopyBuffer = _cachedFoxgloveWebUrl;

                    if (GUILayout.Button("Open Web"))
                        Application.OpenURL(_cachedFoxgloveWebUrl);
                }
            }
        }

        private void EnsureSecureSettingsVisible()
        {
            if (IsSecureMode() && string.IsNullOrWhiteSpace(GetString("_certificatePfxPath", "")))
            {
                _connectionSecurityExpanded = true;
                SessionState.SetBool(InspectorFoldoutKey("ConnectionSecurity"), true);
            }
        }

        private static void DrawSection(string title, string sessionStateName, ref bool expanded, System.Action drawContents)
        {
            if (!FoxgloveManagerInspectorLayout.WorkflowSection(title, InspectorFoldoutKey(sessionStateName), ref expanded))
                return;

            EditorGUI.indentLevel++;
            drawContents();
            EditorGUI.indentLevel--;
        }

        private void LoadInspectorFoldoutState()
        {
            if (SessionState.GetInt(InspectorFoldoutKey("DataTransportFoldoutMigrationVersion"), 0) < 1)
            {
                var publishDataExpanded = SessionState.GetBool(InspectorFoldoutKey("PublishData"), false);
                var subscribeDataExpanded = SessionState.GetBool(InspectorFoldoutKey("SubscribeData"), false);
                var r2fuRuntimeExpanded = SessionState.GetBool(InspectorFoldoutKey("R2fuRuntime"), false);
                var ros2BridgeExpanded = SessionState.GetBool(InspectorFoldoutKey("Ros2Bridge"), false);

                SessionState.SetBool(
                    InspectorFoldoutKey("DataTransport"),
                    publishDataExpanded || subscribeDataExpanded || r2fuRuntimeExpanded || ros2BridgeExpanded);
                SessionState.SetBool(InspectorFoldoutKey("DataTransportPublish"), publishDataExpanded);
                SessionState.SetBool(InspectorFoldoutKey("DataTransportSubscribe"), subscribeDataExpanded);
                SessionState.SetBool(InspectorFoldoutKey("DataTransportNativeRuntime"), r2fuRuntimeExpanded);
                SessionState.SetBool(InspectorFoldoutKey("DataTransportRos2Bridge"), ros2BridgeExpanded);
                SessionState.SetInt(InspectorFoldoutKey("DataTransportFoldoutMigrationVersion"), 1);
            }

            _connectionSecurityExpanded = SessionState.GetBool(InspectorFoldoutKey("ConnectionSecurity"), false);
            _dataTransportExpanded = SessionState.GetBool(InspectorFoldoutKey("DataTransport"), false);
            _dataTransportPublishExpanded = SessionState.GetBool(InspectorFoldoutKey("DataTransportPublish"), false);
            _dataTransportSubscribeExpanded = SessionState.GetBool(InspectorFoldoutKey("DataTransportSubscribe"), false);
            _dataTransportNativeRuntimeExpanded = SessionState.GetBool(InspectorFoldoutKey("DataTransportNativeRuntime"), false);
            _dataTransportRos2BridgeExpanded = SessionState.GetBool(InspectorFoldoutKey("DataTransportRos2Bridge"), false);
            _mcapExpanded = SessionState.GetBool(InspectorFoldoutKey("Mcap"), false);
            _foxServicesExpanded = SessionState.GetBool(InspectorFoldoutKey("FoxServices"), false);
            _schemaEvidenceAdvancedExpanded = SessionState.GetBool(InspectorFoldoutKey("SchemaEvidenceAdvanced"), false);
            _remoteFileAccessExpanded = SessionState.GetBool(InspectorFoldoutKey("RemoteFileAccess"), true);
            _diagnosticsExpanded = SessionState.GetBool(InspectorFoldoutKey("Diagnostics"), false);
        }

        private static string InspectorFoldoutKey(string name)
            => InspectorFoldoutSessionPrefix + name;

        private void DrawConnectionSecuritySection()
        {
            FoxgloveManagerInspectorLayout.Subheader("Server");
            DrawProperty("_serverName");
            using (new EditorGUI.DisabledScope(!GetBool("_foxgloveOutputEnabled")))
                DrawTransportModeProperty();
            DrawProperty("_host");
            DrawProperty("_port");
            DrawProperty("_startOnEnable");
            DrawProperty("_runInBackground");

            FoxgloveManagerInspectorLayout.Subheader("Web Access");
            DrawProperty("_allowHostedFoxgloveWeb");
            DrawProperty("_allowedBrowserOrigins");

            var isSecure = IsSecureMode();
            FoxgloveManagerInspectorLayout.Subheader("Security / WSS");
            DrawSecureWebSocketFields(isSecure);

            FoxgloveManagerInspectorLayout.Subheader("Certificate Tools");
            DrawSecureWebSocketSection(isSecure);
        }

        private void DrawTransportModeProperty()
        {
            // TransportModeLabels exposes only "Web Socket" and "Secure Web Socket";
            // the internal None sentinel stays hidden from the Inspector.
            var prop = FindCachedProperty("_transportMode");
            if (prop == null)
            {
                DrawMissingProperty("_transportMode");
                return;
            }

            var secureIndex = EnumIndex(prop, nameof(FoxgloveTransportMode.SecureWebSocket), (int)FoxgloveTransportMode.SecureWebSocket);
            var webSocketIndex = EnumIndex(prop, nameof(FoxgloveTransportMode.WebSocket), (int)FoxgloveTransportMode.WebSocket);
            var noneIndex = EnumIndex(prop, nameof(FoxgloveTransportMode.None), (int)FoxgloveTransportMode.None);
            if (GetBool("_foxgloveOutputEnabled") && prop.enumValueIndex == noneIndex)
            {
                EditorGUILayout.HelpBox(
                    "Transport mode is serialized as None while Foxglove WebSocket output is enabled. Select Web Socket or Secure Web Socket.",
                    MessageType.Warning);
            }

            var current = prop.enumValueIndex == secureIndex
                ? FoxgloveTransportMode.SecureWebSocket
                : FoxgloveTransportMode.WebSocket;
            var selected = EditorGUILayout.Popup(
                "Transport Mode",
                current == FoxgloveTransportMode.SecureWebSocket ? 1 : 0,
                TransportModeLabels);

            prop.enumValueIndex = selected == 1 ? secureIndex : webSocketIndex;
        }

        private SerializedProperty FindCachedProperty(string propertyName)
        {
            switch (propertyName)
            {
                case "m_Script": return _scriptProperty;
                case "_host": return _hostProperty;
                case "_port": return _portProperty;
                case "_transportMode": return _transportModeProperty;
                case "_sharedToken": return _sharedTokenProperty;
                case "_startOnEnable": return _startOnEnableProperty;
                case "_enableRecording": return _enableRecordingProperty;
                case "_enableReplay": return _enableReplayProperty;
                case "_foxgloveOutputEnabled": return _foxgloveOutputEnabledProperty;
                case "_ros2NativeEnabled": return _ros2NativeEnabledProperty;
                case "_ros2BridgeEnabled": return _ros2BridgeEnabledProperty;
                case "_enableFoxRunInbound": return _enableFoxRunInboundProperty;
                case "_defaultFoxRunPublishEncoding": return _defaultFoxRunPublishEncodingProperty;
                case "_defaultFoxRunSubscriptionEncoding": return _defaultFoxRunSubscriptionEncodingProperty;
                case "_defaultFoxRunSubscriptionProvider": return _defaultFoxRunSubscriptionProviderProperty;
                case "_defaultFoxRunRos2Qos": return _defaultFoxRunRos2QosProperty;
                case "_foxRunRos2NativeCopyBudgetBytes": return _foxRunRos2NativeCopyBudgetBytesProperty;
                case "_allowRemoteFoxRunInboundWithSharedToken": return _allowRemoteFoxRunInboundWithSharedTokenProperty;
                case "_certificatePfxPath": return _certificatePfxPathProperty;
                case "_certificatePassword": return _certificatePasswordProperty;
                case "_rootCaFilePath": return _rootCaFilePathProperty;
                case "_rootCaDistributorEnabled": return _rootCaDistributorEnabledProperty;
                case "_rootCaDistributorHost": return _rootCaDistributorHostProperty;
                case "_rootCaDistributorPort": return _rootCaDistributorPortProperty;
                case "_enableRemoteMcapFileServer": return _enableRemoteMcapFileServerProperty;
                case "_remoteMcapFileServerHost": return _remoteMcapFileServerHostProperty;
                case "_remoteMcapFileServerPort": return _remoteMcapFileServerPortProperty;
                case "_remoteMcapFileServerSourceId": return _remoteMcapFileServerSourceIdProperty;
                case "_remoteMcapFileServerToken": return _remoteMcapFileServerTokenProperty;
                case "_recordingDirectory": return _recordingDirectoryProperty;
                case "_replayFilePath": return _replayFilePathProperty;
                case "_replayAutoPlay": return _replayAutoPlayProperty;
                case "_identityModeSource": return _identityModeSourceProperty;
                case "_identityModeOverride": return _identityModeOverrideProperty;
                case "_projectSettingsIdentityMode": return _projectSettingsIdentityModeProperty;
                case "_schemaEvidenceRoot": return _schemaEvidenceRootProperty;
                default: return serializedObject.FindProperty(propertyName);
            }
        }

        private void RefreshWebUrlCache(string host, int port, bool isSecure, string token)
        {
            host = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host;
            token = token ?? string.Empty;

            if (string.Equals(_cachedEndpointHost, host, System.StringComparison.Ordinal)
                && _cachedEndpointPort == port
                && _cachedEndpointSecure == isSecure
                && string.Equals(_cachedEndpointToken, token, System.StringComparison.Ordinal))
            {
                return;
            }

            _cachedEndpointHost = host;
            _cachedEndpointPort = port;
            _cachedEndpointSecure = isSecure;
            _cachedEndpointToken = token;
            _cachedEndpoint = FoxgloveAppUrl.BuildWebSocketEndpoint(host, port, isSecure, token, redactToken: true);
            _cachedFoxgloveWebUrl = FoxgloveAppUrl.BuildHostedWebSocketUrl(host, port, isSecure, token: token);
            _cachedRedactedFoxgloveWebUrl = FoxgloveAppUrl.BuildHostedWebSocketUrl(host, port, isSecure, token: token, redactToken: true);
            _cachedSecureUrl = $"wss://{host}:{port}" + (string.IsNullOrEmpty(token) ? "" : "?token=REDACTED");
        }

        private void RefreshRemoteMcapUrlCache(string host, int port, string sourceId)
        {
            host = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host.Trim();
            sourceId = string.IsNullOrWhiteSpace(sourceId) ? "local-mcap" : sourceId.Trim();

            if (string.Equals(_cachedRemoteHost, host, System.StringComparison.Ordinal)
                && _cachedRemotePort == port
                && string.Equals(_cachedRemoteSourceId, sourceId, System.StringComparison.Ordinal))
            {
                return;
            }

            _cachedRemoteHost = host;
            _cachedRemotePort = port;
            _cachedRemoteSourceId = sourceId;
            _cachedRemoteBaseUrl = "http://" + host + ":" + port.ToString(System.Globalization.CultureInfo.InvariantCulture);
            _cachedRemoteDirectFileUrl = _cachedRemoteBaseUrl
                + "/v1/files/"
                + System.Uri.EscapeDataString(sourceId)
                + ".mcap";
        }

        private void InvalidateUrlCaches()
        {
            _cachedEndpointHost = null;
            _cachedRemoteHost = null;
        }

        private void GenerateLocalDevCertificate()
        {
            if (!EditorUtility.DisplayDialog(
                    "Generate Local Dev Certificate",
                    "Generate a self-signed local-development certificate under UserSettings, then fill the WSS fields. This does not import the root CA into your OS trust store.",
                    "Generate",
                    "Cancel"))
            {
                return;
            }

            Undo.RecordObject(target, "Generate Local Dev WSS Certificate");
            serializedObject.ApplyModifiedProperties();
            var host = GetString("_host", "127.0.0.1");
            try
            {
                var result = FoxgloveLocalDevCertificateGenerator.Generate(host, BuildCertificateGeneratorOptions());

                serializedObject.Update();
                SetString("_certificatePfxPath", MakeRelative(result.PfxPath));
                SetString("_certificatePassword", result.CertificatePassword);
                SetBool("_rootCaDistributorEnabled", true);
                SetString("_rootCaDistributorHost", LocalRootCaDistributorHost);
                SetInt("_rootCaDistributorPort", LocalRootCaDistributorPort);
                SetString("_rootCaFilePath", MakeRelative(result.RootCaPath));
                serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(target);

                var fingerprint = FoxgloveCertificateDistributor.ComputeSha256Fingerprint(result.RootCaPath);
                _cachedRootCaFingerprintPath = result.RootCaPath;
                _cachedRootCaFingerprint = fingerprint;
                var pageStarted = StartEditorRootCaDistributor(
                    result.RootCaPath,
                    LocalRootCaDistributorHost,
                    LocalRootCaDistributorPort,
                    out var pageError);
                Debug.Log(
                    "[Foxglove] Generated local development WSS certificate. "
                    + $"Root CA SHA-256={fingerprint}. Import the root CA manually after fingerprint verification.");

                if (pageStarted)
                {
                    Debug.Log($"[Foxglove] Local Root CA page is available at {LocalRootCaPageUrl}");
                    Application.OpenURL(LocalRootCaPageUrl);
                }
                else
                {
                    Debug.LogWarning(
                        $"[Foxglove] Generated the local development certificate, but could not start "
                        + $"the Root CA page at {LocalRootCaPageUrl}: {pageError}");
                }
            }
            catch (FoxgloveLocalDevCertificateException ex)
            {
                var message = BuildCertificateFailureMessage(ex);
                EditorUtility.DisplayDialog("Generate Local Dev Certificate", message, "OK");
                Debug.LogError($"[Foxglove] Failed to generate local development WSS certificate: {message}");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[Foxglove] Failed to generate local development WSS certificate: {ex.Message}");
            }
        }

        private void DrawCertificateUtilityButtons(string fingerprint, string secureUrl)
        {
            var pfxPath = ResolveProjectPath(GetString("_certificatePfxPath", ""));
            var rootPath = ResolveProjectPath(GetString("_rootCaFilePath", ""));
            var hasCertificateFiles = File.Exists(pfxPath) || File.Exists(rootPath);

            using (new EditorGUI.DisabledScope(!hasCertificateFiles))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Reveal Certificate Folder"))
                    {
                        var revealPath = File.Exists(rootPath) ? rootPath : pfxPath;
                        EditorUtility.RevealInFinder(revealPath);
                    }

                    if (GUILayout.Button("Copy Root CA SHA-256"))
                    {
                        EditorGUIUtility.systemCopyBuffer = fingerprint ?? string.Empty;
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Copy Redacted WSS URL"))
                    {
                        EditorGUIUtility.systemCopyBuffer = secureUrl ?? string.Empty;
                    }
                }
            }
        }

        private static bool StartEditorRootCaDistributor(string rootCaPath, string host, int port, out string error)
        {
            StopEditorRootCaDistributor();
            error = string.Empty;

            try
            {
                _editorRootCaDistributor = new FoxgloveCertificateDistributor(
                    rootCaPath,
                    logger: new Components.UnityLogger());
                _editorRootCaDistributor.Start(host, port);
                _lastRootCaDistributorPath = rootCaPath;
                _lastRootCaDistributorHost = host;
                _lastRootCaDistributorPort = port;
                return true;
            }
            catch (System.Exception ex)
            {
                error = ex.Message;
                StopEditorRootCaDistributor();
                return false;
            }
        }

        private static void StopEditorRootCaDistributor()
        {
            try
            {
                _editorRootCaDistributor?.Dispose();
            }
            catch
            {
                // Best effort cleanup during editor reload/play-mode transitions.
            }
            finally
            {
                _editorRootCaDistributor = null;
            }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.EnteredPlayMode)
                StopEditorRootCaDistributor();
            else if (state == PlayModeStateChange.EnteredEditMode)
                RestartEditorRootCaDistributorIfPossible();
        }

        private string GetCachedRootCaFingerprint(string resolvedPath)
        {
            if (string.Equals(_cachedRootCaFingerprintPath, resolvedPath, System.StringComparison.Ordinal))
                return _cachedRootCaFingerprint;

            _cachedRootCaFingerprintPath = resolvedPath;
            _cachedRootCaFingerprint = FoxgloveCertificateDistributor.ComputeSha256Fingerprint(resolvedPath);
            return _cachedRootCaFingerprint;
        }

        private static void RestartEditorRootCaDistributorIfPossible()
        {
            if (string.IsNullOrEmpty(_lastRootCaDistributorPath)
                || string.IsNullOrEmpty(_lastRootCaDistributorHost)
                || _lastRootCaDistributorPort <= 0
                || !File.Exists(_lastRootCaDistributorPath))
            {
                return;
            }

            if (!StartEditorRootCaDistributor(
                    _lastRootCaDistributorPath,
                    _lastRootCaDistributorHost,
                    _lastRootCaDistributorPort,
                    out var error))
            {
                Debug.LogWarning("[Foxglove] Could not restart the local Root CA page after Play Mode: " + error);
            }
        }

    }

    /// <summary>
    /// Property drawer for <c>AssetRootDefinition</c> that renders a foldout with
    /// URI prefix, local root (with Browse button), and max size fields.
    /// </summary>
    [CustomPropertyDrawer(typeof(Components.AssetRootDefinition))]
    public class AssetRootDefinitionDrawer : PropertyDrawer
    {
        /// <summary>
        /// Draws a foldout containing <c>uriPrefix</c>, <c>localRoot</c> (with Browse),
        /// and <c>maxMB</c> properties.
        /// </summary>
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            var lineH = EditorGUIUtility.singleLineHeight;
            var spacing = EditorGUIUtility.standardVerticalSpacing;

            property.isExpanded = EditorGUI.Foldout(
                new Rect(position.x, position.y, position.width, lineH),
                property.isExpanded, label, true);

            if (property.isExpanded)
            {
                EditorGUI.indentLevel++;
                var y = position.y + lineH + spacing;

                var uriProp = property.FindPropertyRelative("uriPrefix");
                var localRootProp = property.FindPropertyRelative("localRoot");
                var maxMBProp = property.FindPropertyRelative("maxMB");

                EditorGUI.PropertyField(new Rect(position.x, y, position.width, lineH), uriProp);
                y += lineH + spacing;

                var browseW = 30f;
                var gap = 4f;
                var fieldRect = new Rect(position.x, y, position.width - browseW - gap, lineH);
                var btnRect = new Rect(position.x + position.width - browseW, y, browseW, lineH);
                EditorGUI.PropertyField(fieldRect, localRootProp);
                if (GUI.Button(btnRect, "..."))
                {
                    var defaultDir = FoxgloveManagerEditor.GetSmartDefault(localRootProp.stringValue, false);
                    var selected = EditorUtility.OpenFolderPanel("Select Asset Root", defaultDir, "");
                    if (!string.IsNullOrEmpty(selected))
                        localRootProp.stringValue = FoxgloveManagerEditor.MakeRelative(selected);
                }
                y += lineH + spacing;

                EditorGUI.PropertyField(new Rect(position.x, y, position.width, lineH), maxMBProp);
                EditorGUI.indentLevel--;
            }

            EditorGUI.EndProperty();
        }

        /// <summary>
        /// Returns the height of the property drawer: a single line when collapsed,
        /// or the height of the expanded foldout with 3 child fields otherwise.
        /// </summary>
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (!property.isExpanded)
                return EditorGUIUtility.singleLineHeight;

            var lineH = EditorGUIUtility.singleLineHeight;
            var spacing = EditorGUIUtility.standardVerticalSpacing;
            return lineH + (lineH + spacing) * 3;
        }
    }
}
