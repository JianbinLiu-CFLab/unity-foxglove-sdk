// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Manager

using System.IO;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Ros2Bridge;
using Unity.FoxgloveSDK.Transport;
using UnityEngine;
using UnityEditor;

namespace Unity.FoxgloveSDK.Editor
{
    public partial class FoxgloveManagerEditor : UnityEditor.Editor
    {
        private readonly McapReplayPreflightDrawer _mcapReplayPreflight = new McapReplayPreflightDrawer();

        private void DrawRecordingReplayWarning()
        {
            if (GetBool("_enableRecording") && GetBool("_enableReplay"))
            {
                EditorGUILayout.HelpBox(
                    "Recording and Replay cannot both run at the same time. At runtime, recording is kept and replay is disabled.",
                    MessageType.Warning);
            }
        }

        private void DrawMcapSection()
        {
            FoxgloveManagerInspectorLayout.Subheader("Playback Control");
            DrawProperty("_enablePlaybackControl");
            DrawProperty("_playbackStartOffsetSeconds");
            DrawProperty("_playbackDurationSeconds");

            DrawSchemaEvidenceSection();

            DrawProperty("_enableRecording");
            DrawProperty("_recordingPrefix");
            var directory = FindCachedProperty("_recordingDirectory");
            if (directory != null)
                DrawPathBrowse(directory, "Select Recording Directory", "", false, GetSmartDefault(directory.stringValue, false));
            else
                DrawMissingProperty("_recordingDirectory");
            DrawProperty("_recordingChunkSizeKB");
            DrawProperty("_recordingCompression");

            DrawProperty("_enableReplay");
            DrawReplayAutoPlayControl();
            DrawProperty("_disableLivePublishers");
            var replayPath = FindCachedProperty("_replayFilePath");
            if (replayPath != null)
            {
                DrawStackedPathBrowse(replayPath,
                    "Replay File Path",
                    "Select MCAP File",
                    "mcap",
                    true,
                    GetSmartDefault(replayPath.stringValue, true));
            }
            else
            {
                DrawMissingProperty("_replayFilePath");
            }

            if (replayPath != null)
            {
                _mcapReplayPreflight.Draw(serializedObject, target, replayPath);
            }

            DrawRemoteFileAccessSection(replayPath);
        }

        private void DrawReplayAutoPlayControl()
        {
            var replayAutoPlay = FindCachedProperty("_replayAutoPlay");
            if (replayAutoPlay == null)
            {
                DrawMissingProperty("_replayAutoPlay");
                return;
            }

            var remoteFileServerEnabled = GetBool("_enableRemoteMcapFileServer");
            if (remoteFileServerEnabled && replayAutoPlay.boolValue)
            {
                Undo.RegisterCompleteObjectUndo(target, "Disable Replay Auto Play");
                replayAutoPlay.boolValue = false;
                serializedObject.ApplyModifiedProperties();
            }

            using (new EditorGUI.DisabledScope(remoteFileServerEnabled))
            {
                EditorGUILayout.PropertyField(replayAutoPlay, true);
            }

            if (remoteFileServerEnabled)
            {
                EditorGUILayout.HelpBox(
                    "Foxglove as Replay Timeline is on. Replay Auto Play is unavailable because Foxglove owns replay time.",
                    MessageType.Warning);
            }
        }

        private void DrawRemoteFileAccessSection(SerializedProperty replayPath)
        {
            if (!FoxgloveManagerInspectorLayout.WorkflowSubsection(
                    "Foxglove Timeline Replay",
                    InspectorFoldoutKey("RemoteFileAccess"),
                    ref _remoteFileAccessExpanded))
                return;

            EditorGUI.indentLevel++;
            EditorGUILayout.HelpBox(
                "Serves the selected Replay File Path as a local MCAP URL using the direct /v1/files/... route so Foxglove can load it and control replay time. When enabled, Unity follows the Foxglove timeline; Replay Auto Play is disabled.",
                MessageType.Info);

            DrawProperty("_enableRemoteMcapFileServer", "Foxglove as Replay Timeline");
            using (new EditorGUI.DisabledScope(!GetBool("_enableRemoteMcapFileServer")))
            {
                DrawProperty("_remoteMcapFileServerHost", "Host");
                DrawProperty("_remoteMcapFileServerPort", "Port");
                DrawPasswordProperty("_remoteMcapFileServerToken", "Bearer Token");
                EditorGUILayout.HelpBox(
                    "Prefer FOXGLOVE_REMOTE_MCAP_TOKEN for bearer credentials that must not be serialized into scenes. The Inspector value is a local fallback.",
                    string.IsNullOrEmpty(GetString("_remoteMcapFileServerToken", ""))
                        ? MessageType.Info
                        : MessageType.Warning);

                var remoteUrl = BuildRemoteMcapDirectFileUrl();
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.TextField("Foxglove MCAP URL", remoteUrl);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Copy Foxglove URL"))
                        EditorGUIUtility.systemCopyBuffer = remoteUrl;

                    if (GUILayout.Button("Open in Foxglove"))
                        OpenFoxgloveTarget(remoteUrl);
                }
            }

            EditorGUI.indentLevel--;
        }

        private string BuildRemoteMcapBaseUrl()
        {
            RefreshRemoteMcapUrlCache(
                GetString("_remoteMcapFileServerHost", "127.0.0.1"),
                GetInt("_remoteMcapFileServerPort", 8891),
                GetString("_remoteMcapFileServerSourceId", "local-mcap"));
            return _cachedRemoteBaseUrl;
        }

        private string BuildRemoteMcapDirectFileUrl()
        {
            RefreshRemoteMcapUrlCache(
                GetString("_remoteMcapFileServerHost", "127.0.0.1"),
                GetInt("_remoteMcapFileServerPort", 8891),
                GetString("_remoteMcapFileServerSourceId", "local-mcap"));
            return _cachedRemoteDirectFileUrl;
        }

        private static void OpenFoxgloveTarget(string remoteUrl)
        {
            if (string.IsNullOrWhiteSpace(remoteUrl))
                return;

            var foxgloveUrl = FoxgloveAppUrl.BuildRemoteFileDesktopUrl(remoteUrl);
            Application.OpenURL(foxgloveUrl);
            Debug.Log("[Foxglove] Opening Remote files URL in Foxglove Desktop: " + remoteUrl);
        }

        private void DrawSchemaEvidenceSection()
        {
            if (!FoxgloveManagerInspectorLayout.WorkflowSubsection(
                    "Schema Evidence (Advanced)",
                    InspectorFoldoutKey("SchemaEvidenceAdvanced"),
                    ref _schemaEvidenceAdvancedExpanded))
                return;

            EditorGUI.indentLevel++;

            var source = FindCachedProperty("_identityModeSource");
            var overrideMode = FindCachedProperty("_identityModeOverride");
            var projectMode = FindCachedProperty("_projectSettingsIdentityMode");
            var evidenceRoot = FindCachedProperty("_schemaEvidenceRoot");

            if (source == null || overrideMode == null || projectMode == null || evidenceRoot == null)
            {
                DrawMissingProperty("_identityModeSource / _identityModeOverride / _projectSettingsIdentityMode / _schemaEvidenceRoot");
                EditorGUI.indentLevel--;
                return;
            }

            EditorGUILayout.PropertyField(source, new GUIContent("Identity Mode Source"));
            if (EnumPropertyIs(source, nameof(SchemaIdentityModeSource.Override), (int)SchemaIdentityModeSource.Override))
            {
                EditorGUILayout.PropertyField(overrideMode, new GUIContent("Identity Mode", IdentityModeTooltip(SchemaIdentityModeFromProperty(overrideMode))));
            }
            else
            {
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.PropertyField(projectMode, new GUIContent("Identity Mode", IdentityModeTooltip(SchemaIdentityModeFromProperty(projectMode))));
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.TextField("Current Evidence Root", evidenceRoot.stringValue);
                if (GUILayout.Button("Edit Project Settings", GUILayout.Width(150)))
                    SettingsService.OpenProjectSettings(Unity2FoxgloveSchemaEvidenceSettings.SettingsPath);
            }

            EditorGUILayout.HelpBox(
                "Evidence refreshes automatically on Play, Build, and Recording. Use manual refresh for inspection.",
                MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Apply Project Defaults"))
                {
                    SetEnumProperty(source, nameof(SchemaIdentityModeSource.ProjectSettings), (int)SchemaIdentityModeSource.ProjectSettings);
                    Unity2FoxgloveSchemaEvidenceSettings.SyncSerializedManager(serializedObject);
                }

                if (GUILayout.Button("Refresh Evidence Now"))
                    GenerateSchemaEvidenceNow();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Open Current Evidence"))
                    OpenCurrentEvidenceRoot();

                if (GUILayout.Button("Copy Hash"))
                    CopyCurrentSchemaEvidenceHash();
            }

            EditorGUI.indentLevel--;
        }

        private static string IdentityModeTooltip(SchemaIdentityMode mode)
        {
            switch (mode)
            {
                case SchemaIdentityMode.Warn:
                    return "Reports schema mismatches and continues best-effort replay.";
                case SchemaIdentityMode.Strict:
                    return "Blocks replay startup when the recorded FoxRun schema hash does not match the current project.";
                default:
                    return "Skips schema identity checks.";
            }
        }

        private static void GenerateSchemaEvidenceNow()
        {
            try
            {
                var aggregate = Unity2FoxgloveSchemaManifestGenerator.GenerateArtifacts();
                EditorGUIUtility.systemCopyBuffer = aggregate.SdkSchemaManifestHash;
                AssetDatabase.Refresh();
                Debug.Log("[Foxglove] Generated schema evidence. SDK hash copied to clipboard: " + aggregate.SdkSchemaManifestHash);
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[Foxglove] Failed to generate schema evidence:\n" + ex);
            }
        }

        private static void OpenCurrentEvidenceRoot()
        {
            try
            {
                var currentRoot = Unity2FoxgloveSchemaEvidencePaths.ResolveCurrentEvidenceRoot();
                Directory.CreateDirectory(currentRoot);
                EditorUtility.RevealInFinder(currentRoot);
            }
            catch (System.Exception ex)
            {
                EditorUtility.DisplayDialog(
                    "Open Current Evidence",
                    "Failed to open current schema evidence:\n" + ex.Message,
                    "OK");
                Debug.LogError("[Foxglove] Failed to open current schema evidence: " + ex.Message);
            }
        }

        private static void CopyCurrentSchemaEvidenceHash()
        {
            try
            {
                var aggregateHash = Path.Combine(
                    Unity2FoxgloveSchemaEvidencePaths.ResolveUnity2FoxgloveOutputDirectory(),
                    "unity2foxglove.schema-manifest.hash");
                var foxRunHash = Path.Combine(
                    Unity2FoxgloveSchemaEvidencePaths.ResolveFoxRunOutputDirectory(),
                    "foxrun.manifest.hash");

                if (File.Exists(aggregateHash))
                {
                    EditorGUIUtility.systemCopyBuffer = File.ReadAllText(aggregateHash).Trim();
                    return;
                }

                if (File.Exists(foxRunHash))
                {
                    EditorGUIUtility.systemCopyBuffer = File.ReadAllText(foxRunHash).Trim();
                    return;
                }

                EditorUtility.DisplayDialog(
                    "Copy Schema Evidence Hash",
                    "No current schema evidence hash was found. Refresh evidence first.",
                    "OK");
            }
            catch (System.Exception ex)
            {
                EditorUtility.DisplayDialog(
                    "Copy Schema Evidence Hash",
                    "Failed to copy schema evidence hash:\n" + ex.Message,
                    "OK");
                Debug.LogError("[Foxglove] Failed to copy schema evidence hash: " + ex.Message);
            }
        }
    }
}
