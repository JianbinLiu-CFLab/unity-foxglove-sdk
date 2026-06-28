// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Manager
// Purpose: Custom Inspector for FoxgloveManager workflow settings and
// path helpers.

using System.IO;
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

        static FoxgloveManagerEditor()
        {
            AssemblyReloadEvents.beforeAssemblyReload += StopEditorRootCaDistributor;
            EditorApplication.quitting += StopEditorRootCaDistributor;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        /// <summary>
        /// Draws a curated Inspector for Manager settings and runtime status.
        /// </summary>
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            Unity2FoxgloveSchemaEvidenceSettings.SyncSerializedManager(serializedObject);

            DrawScriptProperty();
            DrawCompactStatus();
            EnsureSecureSettingsVisible();

            DrawSection("Connection & Security", ref _connectionSecurityExpanded, DrawConnectionSecuritySection);
            DrawSection("Publish Data", ref _publishDataExpanded, DrawPublishDataSection);
            DrawRecordingReplayWarning();
            DrawSection("MCAP Record & Replay", ref _mcapExpanded, DrawMcapSection);
            DrawSection("FoxServices", ref _foxServicesExpanded, DrawFoxServicesSection);
            var ros2BridgeProp = serializedObject.FindProperty("_ros2BridgeEnabled");
            if (ros2BridgeProp != null && ros2BridgeProp.boolValue)
                DrawSection("ROS2 Bridge", ref _ros2BridgeExpanded, DrawRos2BridgeSection);
            DrawSection("Diagnostics", ref _diagnosticsExpanded, DrawDiagnosticsSection);

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawScriptProperty()
        {
            var script = serializedObject.FindProperty("m_Script");
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
            var endpoint = FoxgloveAppUrl.BuildWebSocketEndpoint(host, port, isSecure, token, redactToken: true);
            var foxgloveWebUrl = FoxgloveAppUrl.BuildHostedWebSocketUrl(host, port, isSecure, token: token);
            var redactedFoxgloveWebUrl = FoxgloveAppUrl.BuildHostedWebSocketUrl(host, port, isSecure, token: token, redactToken: true);

            EditorGUILayout.Space();
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Status Summary", EditorStyles.boldLabel);
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.TextField("Endpoint", endpoint);
                    EditorGUILayout.TextField("Foxglove Web URL", redactedFoxgloveWebUrl);
                    EditorGUILayout.Toggle("Start On Enable", GetBool("_startOnEnable"));
                    EditorGUILayout.Toggle("Recording Enabled", GetBool("_enableRecording"));
                    EditorGUILayout.Toggle("Replay Enabled", GetBool("_enableReplay"));

                    if (Application.isPlaying && manager != null)
                    {
                        EditorGUILayout.Toggle("Running", manager.IsRunning);
                        var stats = manager.GetTransportStatsSnapshot();
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
                        EditorGUIUtility.systemCopyBuffer = foxgloveWebUrl;

                    if (GUILayout.Button("Open Web"))
                        Application.OpenURL(foxgloveWebUrl);
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

            var snapshots = hub.GetRegisteredServiceSnapshots();
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
            var secureUrl = $"wss://{host}:{port}" + (string.IsNullOrEmpty(token) ? "" : "?token=REDACTED");
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
                var pfx = serializedObject.FindProperty("_certificatePfxPath");
                if (pfx != null)
                    DrawPathBrowse(pfx, "Select WSS PFX Certificate", "pfx", true, GetSmartDefault(pfx.stringValue, true));
                else
                    DrawMissingProperty("_certificatePfxPath");

                DrawPasswordProperty("_certificatePassword", "Certificate Password");
                DrawPasswordProperty("_sharedToken", "Shared Token");
                EditorGUILayout.HelpBox(
                    "Certificate passwords and shared tokens are serialized with the scene or prefab. Use local-development credentials here and avoid committing production secrets.",
                    MessageType.Warning);
                DrawProperty("_rootCaDistributorEnabled");
                DrawProperty("_rootCaDistributorHost");
                DrawProperty("_rootCaDistributorPort");

                var rootCa = serializedObject.FindProperty("_rootCaFilePath");
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
            var prop = serializedObject.FindProperty(propertyName);
            if (prop == null)
            {
                DrawMissingProperty(propertyName);
                return;
            }

            EditorGUILayout.PropertyField(prop, true);
        }

        private void DrawProperty(string propertyName, string label)
        {
            var prop = serializedObject.FindProperty(propertyName);
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
            var prop = serializedObject.FindProperty("_transportMode");
            if (prop == null)
            {
                DrawMissingProperty("_transportMode");
                return;
            }

            var current = prop.enumValueIndex == (int)FoxgloveTransportMode.SecureWebSocket
                ? FoxgloveTransportMode.SecureWebSocket
                : FoxgloveTransportMode.WebSocket;
            var selected = EditorGUILayout.Popup(
                "Transport Mode",
                current == FoxgloveTransportMode.SecureWebSocket ? 1 : 0,
                TransportModeLabels);

            prop.enumValueIndex = selected == 1
                ? (int)FoxgloveTransportMode.SecureWebSocket
                : (int)FoxgloveTransportMode.WebSocket;
        }

        private void DrawFloatProperty(string propertyName, string label, string tooltip)
        {
            var prop = serializedObject.FindProperty(propertyName);
            if (prop == null)
            {
                DrawMissingProperty(propertyName);
                return;
            }

            prop.floatValue = EditorGUILayout.FloatField(new GUIContent(label, tooltip), prop.floatValue);
        }

        private void DrawGlobalEncodingProperty(string propertyName, string label)
        {
            var prop = serializedObject.FindProperty(propertyName);
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

        private string GetString(string propertyName, string fallback)
        {
            var prop = serializedObject.FindProperty(propertyName);
            return prop != null ? prop.stringValue : fallback;
        }

        private int GetInt(string propertyName, int fallback)
        {
            var prop = serializedObject.FindProperty(propertyName);
            return prop != null ? prop.intValue : fallback;
        }

        private bool GetBool(string propertyName)
        {
            var prop = serializedObject.FindProperty(propertyName);
            return prop != null && prop.boolValue;
        }

        private void SetString(string propertyName, string value)
        {
            var prop = serializedObject.FindProperty(propertyName);
            if (prop != null)
                prop.stringValue = value ?? string.Empty;
        }

        private void SetBool(string propertyName, bool value)
        {
            var prop = serializedObject.FindProperty(propertyName);
            if (prop != null)
                prop.boolValue = value;
        }

        private void SetInt(string propertyName, int value)
        {
            var prop = serializedObject.FindProperty(propertyName);
            if (prop != null)
                prop.intValue = value;
        }

        private bool IsSecureMode()
        {
            var prop = serializedObject.FindProperty("_transportMode");
            return prop != null && prop.enumValueIndex == (int)FoxgloveTransportMode.SecureWebSocket;
        }

        private void DrawPasswordProperty(string propertyName, string label)
        {
            var prop = serializedObject.FindProperty(propertyName);
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
