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
        private bool _publishDataExpanded;
        private bool _ros2BridgeExpanded;
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

            DrawSection("Connection & Security", ref _connectionSecurityExpanded, DrawConnectionSecuritySection);
            DrawSection("Publish Data", ref _publishDataExpanded, DrawPublishDataSection);
            DrawRecordingReplayWarning();
            DrawSection("MCAP Record & Replay", ref _mcapExpanded, DrawMcapSection);
            DrawSection("FoxServices", ref _foxServicesExpanded, DrawFoxServicesSection);
            var ros2BridgeProp = FindCachedProperty("_ros2BridgeEnabled");
            if (ros2BridgeProp != null && ros2BridgeProp.boolValue)
                DrawSection("ROS2 Bridge", ref _ros2BridgeExpanded, DrawRos2BridgeSection);
            DrawSection("Diagnostics", ref _diagnosticsExpanded, DrawDiagnosticsSection);

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
                _connectionSecurityExpanded = true;
        }

        private static void DrawSection(string title, ref bool expanded, System.Action drawContents)
        {
            if (!FoxgloveManagerInspectorLayout.WorkflowSection(title, ref expanded))
                return;

            EditorGUI.indentLevel++;
            drawContents();
            EditorGUI.indentLevel--;
        }

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

            FoxgloveManagerInspectorLayout.Subheader("FoxRun Inbound");
            DrawProperty("_enableFoxRunInbound");
            using (new EditorGUI.DisabledScope(!GetBool("_enableFoxRunInbound")))
            {
                DrawProperty("_allowRemoteFoxRunInboundWithSharedToken");
                DrawProperty("_foxRunInboundMaxPayloadBytes");
                DrawProperty("_foxRunInboundMaxMessagesPerSecondPerTopic");
            }
            if (GetBool("_enableFoxRunInbound")
                && !Components.FoxgloveManager.IsLoopbackHost(GetString("_host", "127.0.0.1"))
                && (!GetBool("_allowRemoteFoxRunInboundWithSharedToken")
                    || string.IsNullOrWhiteSpace(GetString("_sharedToken", ""))))
            {
                EditorGUILayout.HelpBox(
                    "FoxRun inbound is fail-closed for non-loopback hosts. Enable remote inbound explicitly and configure a shared token.",
                    MessageType.Warning);
            }

            var isSecure = IsSecureMode();
            FoxgloveManagerInspectorLayout.Subheader("Security / WSS");
            DrawSecureWebSocketFields(isSecure);

            FoxgloveManagerInspectorLayout.Subheader("Certificate Tools");
            DrawSecureWebSocketSection(isSecure);
        }

        private void DrawFoxServicesSection()
        {
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "Generated [FoxService] services register when Play Mode starts. Use Foxglove's Call Service panel to invoke them.",
                    MessageType.Info);
                return;
            }

            if (!Components.FoxgloveServiceHub.TryGetActive(out var hub) || hub == null)
            {
                EditorGUILayout.HelpBox("FoxServiceHub is not active yet.", MessageType.Info);
                return;
            }

            var snapshots = GetServiceSnapshotsForRepaint(hub);
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.IntField("Registered Services", snapshots.Count);

            if (snapshots.Count == 0)
            {
                EditorGUILayout.HelpBox("No generated [FoxService] services are currently registered.", MessageType.Info);
                return;
            }

            if (GUILayout.Button("Copy Service List"))
                EditorGUIUtility.systemCopyBuffer = BuildServiceListText(snapshots);

            foreach (var snapshot in snapshots)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.SelectableLabel(snapshot.Name, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                        if (GUILayout.Button("Copy", GUILayout.Width(54)))
                            EditorGUIUtility.systemCopyBuffer = snapshot.Name;
                    }

                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.TextField("Source", snapshot.Source);
                        EditorGUILayout.TextField("Request", snapshot.RequestSchemaName);
                        EditorGUILayout.TextField("Response", snapshot.ResponseSchemaName);
                        EditorGUILayout.LongField("Service Id", snapshot.ServiceId);
                    }
                }
            }
        }

        private static string BuildServiceListText(System.Collections.Generic.IReadOnlyList<Components.FoxgloveRegisteredServiceSnapshot> snapshots)
        {
            var lines = new string[snapshots.Count];
            for (var i = 0; i < snapshots.Count; i++)
            {
                var snapshot = snapshots[i];
                lines[i] = snapshot.Name
                           + " | Source: " + snapshot.Source
                           + " | Request: " + snapshot.RequestSchemaName
                           + " | Response: " + snapshot.ResponseSchemaName
                           + " | Service Id: " + snapshot.ServiceId;
            }
            return string.Join("\n", lines);
        }

        private void DrawSecureWebSocketSection(bool isSecure)
        {
            DrawCertificateGeneratorBackendControls();

            EditorGUILayout.Space();
            if (GUILayout.Button("Generate Local Dev Certificate"))
                GenerateLocalDevCertificate();

            var host = GetString("_host", "127.0.0.1");
            var port = GetInt("_port", 8765);
            var token = GetString("_sharedToken", "");
            RefreshWebUrlCache(host, port, isSecure: true, token);
            var secureUrl = _cachedSecureUrl;
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField("Secure URL", secureUrl);
            }

            if (!isSecure)
                EditorGUILayout.HelpBox("Select SecureWebSocket transport mode to enable WSS settings.", MessageType.Info);

            var distributorHost = GetString("_rootCaDistributorHost", "127.0.0.1");
            if (distributorHost != "127.0.0.1" && distributorHost != "localhost")
            {
                EditorGUILayout.HelpBox(
                    "Root CA distributor is not bound to loopback. Only use this on trusted networks.",
                    MessageType.Warning);
            }

            var rootPath = GetString("_rootCaFilePath", "");
            var fingerprint = GetCachedRootCaFingerprint(ResolveProjectPath(rootPath));
            if (!string.IsNullOrEmpty(fingerprint))
            {
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.TextField("Root CA SHA-256", fingerprint);
                EditorGUILayout.HelpBox(
                    "Import the generated root CA manually only after comparing this SHA-256 fingerprint through a trusted channel.",
                    MessageType.Info);
            }

            if (GetBool("_rootCaDistributorEnabled"))
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.TextField(
                        "Root CA URL",
                        $"http://{distributorHost}:{GetInt("_rootCaDistributorPort", 8766)}/rootCA.crt");
                }
            }

            DrawCertificateUtilityButtons(fingerprint, secureUrl);
        }

        private void DrawSecureWebSocketFields(bool isSecure)
        {
            using (new EditorGUI.DisabledScope(!isSecure))
            {
                var pfx = FindCachedProperty("_certificatePfxPath");
                if (pfx != null)
                    DrawPathBrowse(pfx, "Select WSS PFX Certificate", "pfx", true, GetSmartDefault(pfx.stringValue, true));
                else
                    DrawMissingProperty("_certificatePfxPath");

                DrawPasswordProperty("_certificatePassword", "Certificate Password");
                DrawPasswordProperty("_sharedToken", "Shared Token");
                EditorGUILayout.HelpBox(
                    "Certificate passwords and shared tokens entered here are serialized with the scene or prefab. Prefer FOXGLOVE_CERTIFICATE_PASSWORD and FOXGLOVE_SHARED_TOKEN for credentials that must not be committed.",
                    MessageType.Warning);
                DrawProperty("_rootCaDistributorEnabled");
                DrawProperty("_rootCaDistributorHost");
                DrawProperty("_rootCaDistributorPort");

                var rootCa = FindCachedProperty("_rootCaFilePath");
                if (rootCa != null)
                    DrawPathBrowse(rootCa, "Select Root CA File", "crt", true, GetSmartDefault(rootCa.stringValue, true));
                else
                    DrawMissingProperty("_rootCaFilePath");
            }
        }

        private static void DrawCertificateGeneratorBackendControls()
        {
            var backend = GetCertificateBackendPreference();
            var selected = (FoxgloveLocalDevCertificateBackend)EditorGUILayout.EnumPopup(
                "Certificate Generator",
                backend);
            if (selected != backend)
            {
                EditorPrefs.SetString(CertificateBackendEditorPrefKey, selected.ToString());
                backend = selected;
            }

            if (backend != FoxgloveLocalDevCertificateBackend.OpenSsl)
                return;

            var configuredPath = EditorPrefs.GetString(OpenSslPathEditorPrefKey, string.Empty);
            var resolved = OpenSslResolver.Resolve(configuredPath);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField("OpenSSL", string.IsNullOrEmpty(resolved) ? "Not found" : resolved);
            }

            if (string.IsNullOrEmpty(resolved))
            {
                EditorGUILayout.HelpBox(
                    "OpenSSL is optional. Install it, add it to PATH, set UNITY2FOXGLOVE_OPENSSL, or choose an executable before using the OpenSSL backend.",
                    MessageType.Warning);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Choose OpenSSL"))
                {
                    var selectedPath = EditorUtility.OpenFilePanel(
                        "Choose OpenSSL executable",
                        GetOpenSslPickerDirectory(configuredPath),
                        Application.platform == RuntimePlatform.WindowsEditor ? "exe" : "");
                    if (!string.IsNullOrEmpty(selectedPath))
                        EditorPrefs.SetString(OpenSslPathEditorPrefKey, selectedPath);
                }

                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(configuredPath)))
                {
                    if (GUILayout.Button("Clear OpenSSL"))
                        EditorPrefs.DeleteKey(OpenSslPathEditorPrefKey);
                }
            }
        }

        private void DrawProperty(string propertyName)
        {
            var prop = FindCachedProperty(propertyName);
            if (prop == null)
            {
                DrawMissingProperty(propertyName);
                return;
            }

            EditorGUILayout.PropertyField(prop, true);
        }

        private void DrawProperty(string propertyName, string label)
        {
            var prop = FindCachedProperty(propertyName);
            if (prop == null)
            {
                DrawMissingProperty(propertyName);
                return;
            }

            EditorGUILayout.PropertyField(prop, new GUIContent(label), true);
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
            var current = prop.enumValueIndex == secureIndex
                ? FoxgloveTransportMode.SecureWebSocket
                : FoxgloveTransportMode.WebSocket;
            var selected = EditorGUILayout.Popup(
                "Transport Mode",
                current == FoxgloveTransportMode.SecureWebSocket ? 1 : 0,
                TransportModeLabels);

            prop.enumValueIndex = selected == 1 ? secureIndex : webSocketIndex;
        }

        private void DrawFloatProperty(string propertyName, string label, string tooltip)
        {
            var prop = FindCachedProperty(propertyName);
            if (prop == null)
            {
                DrawMissingProperty(propertyName);
                return;
            }

            prop.floatValue = EditorGUILayout.FloatField(new GUIContent(label, tooltip), prop.floatValue);
        }

        private void DrawGlobalEncodingProperty(string propertyName, string label)
        {
            var prop = FindCachedProperty(propertyName);
            if (prop == null)
            {
                DrawMissingProperty(propertyName);
                return;
            }

            PublisherEncodingEditorLabels.DrawGlobalEncoding(prop, label);
        }

        private static void DrawMissingProperty(string propertyName)
        {
            EditorGUILayout.HelpBox($"Serialized property '{propertyName}' was not found.", MessageType.Warning);
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

        private string GetString(string propertyName, string fallback)
        {
            var prop = FindCachedProperty(propertyName);
            return prop != null ? prop.stringValue : fallback;
        }

        private int GetInt(string propertyName, int fallback)
        {
            var prop = FindCachedProperty(propertyName);
            return prop != null ? prop.intValue : fallback;
        }

        private bool GetBool(string propertyName)
        {
            var prop = FindCachedProperty(propertyName);
            return prop != null && prop.boolValue;
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

        private void RefreshTransportStatsForRepaint()
        {
            var manager = target as Components.FoxgloveManager;
            _transportStatsFrame = Time.frameCount;
            _transportStatsThisRepaint = Application.isPlaying && manager != null
                ? manager.GetTransportStatsSnapshot()
                : TransportStatsSnapshot.Unsupported;
        }

        private TransportStatsSnapshot GetTransportStatsForRepaint()
        {
            if (_transportStatsFrame != Time.frameCount)
                RefreshTransportStatsForRepaint();
            return _transportStatsThisRepaint ?? TransportStatsSnapshot.Unsupported;
        }

        private System.Collections.Generic.IReadOnlyList<Components.FoxgloveRegisteredServiceSnapshot> GetServiceSnapshotsForRepaint(
            Components.FoxgloveServiceHub hub)
        {
            if (hub == null)
                return System.Array.Empty<Components.FoxgloveRegisteredServiceSnapshot>();

            var frame = Time.frameCount;
            if (_cachedServiceHub == hub && _cachedServiceSnapshotFrame == frame)
                return _cachedServiceSnapshots;

            _cachedServiceHub = hub;
            _cachedServiceSnapshotFrame = frame;
            _cachedServiceSnapshots = hub.GetRegisteredServiceSnapshots();
            return _cachedServiceSnapshots;
        }

        private void SetString(string propertyName, string value)
        {
            var prop = FindCachedProperty(propertyName);
            if (prop != null)
                prop.stringValue = value ?? string.Empty;
        }

        private void SetBool(string propertyName, bool value)
        {
            var prop = FindCachedProperty(propertyName);
            if (prop != null)
                prop.boolValue = value;
        }

        private void SetInt(string propertyName, int value)
        {
            var prop = FindCachedProperty(propertyName);
            if (prop != null)
                prop.intValue = value;
        }

        private bool IsSecureMode()
        {
            var prop = FindCachedProperty("_transportMode");
            return prop != null && EnumPropertyIs(prop, nameof(FoxgloveTransportMode.SecureWebSocket), (int)FoxgloveTransportMode.SecureWebSocket);
        }

        private static bool EnumPropertyIs(SerializedProperty prop, string enumName, int fallbackIndex)
            => prop != null && prop.enumValueIndex == EnumIndex(prop, enumName, fallbackIndex);

        private static void SetEnumProperty(SerializedProperty prop, string enumName, int fallbackIndex)
        {
            if (prop != null)
                prop.enumValueIndex = EnumIndex(prop, enumName, fallbackIndex);
        }

        private static int EnumIndex(SerializedProperty prop, string enumName, int fallbackIndex)
        {
            var names = prop?.enumNames;
            if (names == null)
                return fallbackIndex;

            for (var i = 0; i < names.Length; i++)
            {
                if (string.Equals(names[i], enumName, System.StringComparison.Ordinal))
                    return i;
            }

            return fallbackIndex;
        }

        private static SchemaIdentityMode SchemaIdentityModeFromProperty(SerializedProperty prop)
        {
            var names = prop?.enumNames;
            if (names == null || prop.enumValueIndex < 0 || prop.enumValueIndex >= names.Length)
                return SchemaIdentityMode.Off;

            return System.Enum.TryParse(names[prop.enumValueIndex], out SchemaIdentityMode mode)
                ? mode
                : SchemaIdentityMode.Off;
        }

        private void DrawPasswordProperty(string propertyName, string label)
        {
            var prop = FindCachedProperty(propertyName);
            if (prop == null)
            {
                DrawMissingProperty(propertyName);
                return;
            }

            prop.stringValue = EditorGUILayout.PasswordField(label, prop.stringValue);
        }

        private static FoxgloveLocalDevCertificateBackend GetCertificateBackendPreference()
        {
            var value = EditorPrefs.GetString(
                CertificateBackendEditorPrefKey,
                FoxgloveLocalDevCertificateBackend.BuiltIn.ToString());
            return value == FoxgloveLocalDevCertificateBackend.OpenSsl.ToString()
                ? FoxgloveLocalDevCertificateBackend.OpenSsl
                : FoxgloveLocalDevCertificateBackend.BuiltIn;
        }

        private static FoxgloveLocalDevCertificateOptions BuildCertificateGeneratorOptions()
        {
            var backend = GetCertificateBackendPreference();
            if (backend == FoxgloveLocalDevCertificateBackend.OpenSsl)
                return FoxgloveLocalDevCertificateOptions.OpenSsl(
                    EditorPrefs.GetString(OpenSslPathEditorPrefKey, string.Empty));

            return FoxgloveLocalDevCertificateOptions.BuiltIn;
        }

        private static string GetOpenSslPickerDirectory(string configuredPath)
        {
            if (!string.IsNullOrEmpty(configuredPath))
            {
                if (Directory.Exists(configuredPath))
                    return configuredPath;

                var directory = Path.GetDirectoryName(configuredPath);
                if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
                    return directory;
            }

            return GetDefaultDir();
        }

        private static string BuildCertificateFailureMessage(FoxgloveLocalDevCertificateException ex)
        {
            switch (ex.Kind)
            {
                case FoxgloveLocalDevCertificateFailureKind.BuiltInUnavailable:
                    return "Built-in certificate generation failed in this Unity Editor. The default SDK path does not require OpenSSL; OpenSSL is only a manual fallback. "
                        + "Details: " + ex.Message
                        + "\n\nFallback: select the OpenSSL certificate generator, then install OpenSSL or choose an OpenSSL executable.";
                case FoxgloveLocalDevCertificateFailureKind.OpenSslNotFound:
                    return "OpenSSL was not found. Install OpenSSL, install Git for Windows, add openssl.exe to PATH, set UNITY2FOXGLOVE_OPENSSL to an OpenSSL executable or bin directory, or click Choose OpenSSL in the Inspector.";
                case FoxgloveLocalDevCertificateFailureKind.OpenSslFailed:
                    return ex.Message;
                default:
                    return string.IsNullOrEmpty(ex.Message)
                        ? "Local development certificate generation failed."
                        : ex.Message;
            }
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

        /// <summary>
        /// Renders a path label on one row and the value plus browse button
        /// on the next row.
        /// <para>On selection, converts the absolute path to a project-relative path and
        /// applies it to the serialized property.</para>
        /// </summary>
        internal static void DrawStackedPathBrowse(
            SerializedProperty prop,
            string label,
            string title,
            string extension,
            bool isFile,
            string defaultDir)
        {
            NormalizeProjectRelativePath(prop);

            EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                prop.stringValue = EditorGUILayout.TextField(prop.stringValue);
                if (GUILayout.Button("...", GUILayout.Width(30)))
                {
                    var capturedProp = prop.Copy();
                    var d = defaultDir;
                    EditorApplication.delayCall += () =>
                    {
                        if (capturedProp.serializedObject == null || capturedProp.serializedObject.targetObject == null)
                            return;

                        string selected;
                        if (isFile)
                            selected = EditorUtility.OpenFilePanel(title, d, extension);
                        else
                            selected = EditorUtility.OpenFolderPanel(title, d, "");

                        if (!string.IsNullOrEmpty(selected))
                        {
                            capturedProp.serializedObject.Update();
                            capturedProp.stringValue = MakeRelative(selected);
                            capturedProp.serializedObject.ApplyModifiedProperties();
                        }
                    };
                }
            }
        }

        /// <summary>
        /// Renders a property field with a "..." button that opens a file or folder picker.
        /// <para>On selection, converts the absolute path to a project-relative path and
        /// applies it to the serialized property.</para>
        /// </summary>
        internal static void DrawPathBrowse(SerializedProperty prop, string title, string extension, bool isFile, string defaultDir)
        {
            NormalizeProjectRelativePath(prop);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(prop);
            if (GUILayout.Button("...", GUILayout.Width(30)))
            {
                var capturedProp = prop.Copy();
                var d = defaultDir;
                EditorApplication.delayCall += () =>
                {
                    if (capturedProp.serializedObject == null || capturedProp.serializedObject.targetObject == null)
                        return;

                    string selected;
                    if (isFile)
                        selected = EditorUtility.OpenFilePanel(title, d, extension);
                    else
                        selected = EditorUtility.OpenFolderPanel(title, d, "");

                    if (!string.IsNullOrEmpty(selected))
                    {
                        capturedProp.serializedObject.Update();
                        capturedProp.stringValue = MakeRelative(selected);
                        capturedProp.serializedObject.ApplyModifiedProperties();
                    }
                };
            }
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// Returns the project root directory (one level above <c>Assets</c>).
        /// </summary>
        internal static string GetDefaultDir()
        {
            return Path.GetDirectoryName(Application.dataPath) ?? Application.dataPath;
        }

        /// <summary>
        /// Resolves the best starting directory for the file/folder picker.
        /// Prefers an existing value, then the project-level
        /// <c>Recordings/</c> directory, then the project root.
        /// </summary>
        internal static string GetSmartDefault(string currentValue, bool isFile)
        {
            if (!string.IsNullOrEmpty(currentValue))
            {
                var abs = Path.IsPathRooted(currentValue)
                    ? currentValue
                    : Path.GetFullPath(Path.Combine(GetDefaultDir(), currentValue));
                var dir = isFile ? Path.GetDirectoryName(abs) : abs;
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    return dir;
            }

            // Recording output and replay input both normally live under Recordings/.
            var recordingsDir = Path.Combine(GetDefaultDir(), "Recordings");
            if (Directory.Exists(recordingsDir))
                return recordingsDir;

            return GetDefaultDir();
        }

        /// <summary>
        /// Converts an absolute path to a project-relative path if it resides
        /// under the project root. Returns the absolute path unchanged otherwise.
        /// </summary>
        internal static string MakeRelative(string absolute)
        {
            var projectRoot = Path.GetDirectoryName(Application.dataPath);
            if (string.IsNullOrEmpty(projectRoot)) return absolute;
            var normRoot = projectRoot.Replace('\\', '/');
            var normAbs = absolute.Replace('\\', '/');
            if (normAbs.StartsWith(normRoot + "/"))
                return normAbs.Substring(normRoot.Length + 1);
            return normAbs;
        }

        internal static string ResolveProjectPath(string path)
        {
            if (string.IsNullOrEmpty(path) || Path.IsPathRooted(path))
                return path;
            return Path.GetFullPath(Path.Combine(GetDefaultDir(), path));
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

        private static void NormalizeProjectRelativePath(SerializedProperty prop)
        {
            if (prop == null || prop.propertyType != SerializedPropertyType.String)
                return;

            var value = prop.stringValue;
            if (string.IsNullOrEmpty(value) || !Path.IsPathRooted(value))
                return;

            var relative = MakeRelative(value);
            if (relative != value)
                prop.stringValue = relative;
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
