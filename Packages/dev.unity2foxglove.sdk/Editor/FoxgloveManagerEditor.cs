// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor
// Purpose: Custom Inspector for FoxgloveManager — adds Browse buttons
// for file/folder path fields.

using System.IO;
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
    public class FoxgloveManagerEditor : UnityEditor.Editor
    {
        private static bool _serverExpanded = true;
        private static bool _encodingExpanded = true;
        private static bool _coordinateExpanded;
        private static bool _assetsExpanded;
        private static bool _playbackExpanded;
        private static bool _recordingExpanded;
        private static bool _replayExpanded;
        private static bool _securityExpanded;
        private static bool _transportExpanded;

        /// <summary>
        /// Draws a curated Inspector for Manager settings and runtime status.
        /// </summary>
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawScriptProperty();
            DrawCompactStatus();
            DrawRecordingReplayWarning();

            DrawSection("Server", ref _serverExpanded, DrawServerSection);
            DrawSection("Publisher Encoding", ref _encodingExpanded, DrawPublisherEncodingSection);
            DrawSection("Coordinate System", ref _coordinateExpanded, DrawCoordinateSection);
            DrawSection("Assets", ref _assetsExpanded, DrawAssetsSection);
            DrawSection("Playback Control", ref _playbackExpanded, DrawPlaybackSection);
            DrawSection("MCAP Recording", ref _recordingExpanded, DrawRecordingSection);
            DrawSection("MCAP Replay", ref _replayExpanded, DrawReplaySection);
            DrawSection("Security", ref _securityExpanded, DrawSecuritySection);
            DrawSection("Transport Health", ref _transportExpanded, DrawTransportHealth);

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

            EditorGUILayout.Space();
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Status Summary", EditorStyles.boldLabel);
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.TextField("Endpoint", $"ws://{host}:{port}");
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
                            if (stats.TotalQueuedFrames > 0)
                                EditorGUILayout.LongField("Queued Frames", stats.TotalQueuedFrames);
                            if (stats.TotalDroppedDataFrames > 0)
                                EditorGUILayout.LongField("Dropped Data Frames", stats.TotalDroppedDataFrames);
                        }
                    }
                }
            }
        }

        private void DrawRecordingReplayWarning()
        {
            if (GetBool("_enableRecording") && GetBool("_enableReplay"))
            {
                EditorGUILayout.HelpBox(
                    "Recording and Replay cannot both run at the same time. At runtime, recording is kept and replay is disabled.",
                    MessageType.Warning);
            }
        }

        private static void DrawSection(string title, ref bool expanded, System.Action drawContents)
        {
            EditorGUILayout.Space();
            expanded = EditorGUILayout.Foldout(expanded, title, true, EditorStyles.foldoutHeader);
            if (!expanded)
                return;

            EditorGUI.indentLevel++;
            drawContents();
            EditorGUI.indentLevel--;
        }

        private void DrawServerSection()
        {
            DrawProperty("_serverName");
            DrawProperty("_host");
            DrawProperty("_port");
            DrawProperty("_startOnEnable");
            DrawProperty("_runInBackground");
        }

        private void DrawPublisherEncodingSection()
        {
            DrawProperty("_defaultPublisherEncoding");
            DrawProperty("_allowPublisherOverride");
        }

        private void DrawCoordinateSection()
        {
            DrawProperty("_coordinateMode");
        }

        private void DrawAssetsSection()
        {
            DrawProperty("_assetRoots");
        }

        private void DrawPlaybackSection()
        {
            DrawProperty("_enablePlaybackControl");
            DrawProperty("_playbackStartOffsetSeconds");
            DrawProperty("_playbackDurationSeconds");
        }

        private void DrawRecordingSection()
        {
            DrawProperty("_enableRecording");
            DrawProperty("_recordingPrefix");
            var directory = serializedObject.FindProperty("_recordingDirectory");
            if (directory != null)
                DrawPathBrowse(directory, "Select Recording Directory", "", false, GetSmartDefault(directory.stringValue, false));
            else
                DrawMissingProperty("_recordingDirectory");
            DrawProperty("_recordingChunkSizeKB");
            DrawProperty("_recordingCompression");
        }

        private void DrawReplaySection()
        {
            DrawProperty("_enableReplay");
            var replayPath = serializedObject.FindProperty("_replayFilePath");
            if (replayPath != null)
                DrawPathBrowse(replayPath, "Select MCAP File", "mcap", true, GetSmartDefault(replayPath.stringValue, true));
            else
                DrawMissingProperty("_replayFilePath");
            DrawProperty("_replayAutoPlay");
            DrawProperty("_disableLivePublishers");
        }

        private void DrawSecuritySection()
        {
            DrawProperty("_allowedBrowserOrigins");
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

        private void DrawTransportHealth()
        {
            var manager = (Components.FoxgloveManager)target;
            if (manager == null) return;

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Transport stats are available in Play Mode.", MessageType.Info);
                return;
            }

            var stats = manager.GetTransportStatsSnapshot();
            if (!stats.Supported)
            {
                EditorGUILayout.HelpBox("Transport stats are not available for this backend.", MessageType.Info);
                return;
            }

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.Toggle("Running", stats.IsRunning);
                EditorGUILayout.IntField("Active Clients", stats.ActiveClientCount);
                EditorGUILayout.LongField("Total Accepted", stats.TotalAcceptedClients);
                EditorGUILayout.LongField("Total Disconnected", stats.TotalDisconnectedClients);
                EditorGUILayout.LongField("Queued Frames", stats.TotalQueuedFrames);
                EditorGUILayout.LongField("Queued Bytes", stats.TotalQueuedBytes);
                EditorGUILayout.LongField("Dropped Data Frames", stats.TotalDroppedDataFrames);
                EditorGUILayout.LongField("Control Overflow Disconnects", stats.ControlOverflowDisconnects);
            }

            if (stats.Clients != null && stats.Clients.Count > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Clients", EditorStyles.boldLabel);
                foreach (var c in stats.Clients)
                {
                    EditorGUILayout.LabelField(
                        $"#{c.ClientId}",
                        $"queued: {c.QueuedFrames} ({c.QueuedBytes} B)  dropped: {c.DroppedDataFrames}  sent: {c.SentFrames}  idle: {c.LastActivityAgeMs} ms",
                        EditorStyles.miniLabel);
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
        /// <list><item>
        /// <description>If <c>currentValue</c> is non-empty and resolves to an existing directory,
        /// that directory is used.</description>
        /// </item><item>
        /// <description>For folder pickers, falls back to a project-level
        /// <c>Recordings/</c> directory if it exists.</description>
        /// </item><item>
        /// <description>Final fallback is the project root.</description>
        /// </item></list>
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
    }

    /// <summary>
    /// Custom Inspector for publisher components. Draws the normal serialized
    /// fields plus a read-only encoding summary resolved from manager policy
    /// and publisher capabilities.
    /// </summary>
    [CustomEditor(typeof(Components.FoxglovePublisherBase), true)]
    public class FoxglovePublisherBaseEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var encodingOverride = serializedObject.FindProperty("_encodingOverride");
            var prop = serializedObject.GetIterator();
            if (prop.NextVisible(true))
            {
                do
                {
                    if (prop.name == "_encodingOverride")
                        continue;

                    using (new EditorGUI.DisabledScope(prop.propertyPath == "m_Script"))
                    {
                        EditorGUILayout.PropertyField(prop, true);
                    }
                }
                while (prop.NextVisible(false));
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Encoding Policy", EditorStyles.boldLabel);
            if (encodingOverride != null)
                EditorGUILayout.PropertyField(encodingOverride, new GUIContent("Encoding Override"));

            serializedObject.ApplyModifiedProperties();

            var publisher = (Components.FoxglovePublisherBase)target;
            var resolution = publisher.EncodingResolution;

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField("Supported Encodings", publisher.SupportedEncodingSummary);
                EditorGUILayout.EnumPopup("Effective Encoding", resolution.Effective);
            }

            if (publisher.ConfiguredManager != null
                && !publisher.ConfiguredManager.AllowPublisherOverride
                && publisher.EncodingOverride != Components.PublisherEncodingOverride.UseManager)
            {
                EditorGUILayout.HelpBox(
                    "FoxgloveManager disables publisher overrides; the global default is used.",
                    MessageType.Info);
            }
            else if (resolution.Effective == Components.PublisherEffectiveEncoding.Unsupported)
            {
                EditorGUILayout.HelpBox(
                    "This publisher declares no supported encoding and will not publish messages.",
                    MessageType.Error);
            }
            else if (resolution.FellBack)
            {
                EditorGUILayout.HelpBox(
                    $"Requested {resolution.RequestedLabel}, but this publisher will emit {resolution.EffectiveLabel}.",
                    MessageType.Warning);
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
