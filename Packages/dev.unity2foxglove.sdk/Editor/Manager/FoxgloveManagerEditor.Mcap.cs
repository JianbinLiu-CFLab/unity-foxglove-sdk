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
            var directory = serializedObject.FindProperty("_recordingDirectory");
            if (directory != null)
                DrawPathBrowse(directory, "Select Recording Directory", "", false, GetSmartDefault(directory.stringValue, false));
            else
                DrawMissingProperty("_recordingDirectory");
            DrawProperty("_recordingChunkSizeKB");
            DrawProperty("_recordingCompression");

            DrawProperty("_enableReplay");
            DrawProperty("_replayAutoPlay");
            DrawProperty("_disableLivePublishers");
            var replayPath = serializedObject.FindProperty("_replayFilePath");
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
            DrawCursorBridgeSection();
        }

        private void DrawRemoteFileAccessSection(SerializedProperty replayPath)
        {
            if (!FoxgloveManagerInspectorLayout.WorkflowSubsection("Remote File Access", ref _remoteFileAccessExpanded))
                return;

            EditorGUI.indentLevel++;
            EditorGUILayout.HelpBox(
                "Serves the selected Replay File Path as a Foxglove Remote files URL. Use the direct .mcap URL below; /v1/manifest is only a backend diagnostic endpoint.",
                MessageType.Info);

            DrawProperty("_enableRemoteMcapFileServer", "Enable Remote File URL");
            using (new EditorGUI.DisabledScope(!GetBool("_enableRemoteMcapFileServer")))
            {
                DrawProperty("_remoteMcapFileServerHost", "Host");
                DrawProperty("_remoteMcapFileServerPort", "Port");

                var remoteUrl = BuildRemoteMcapDirectFileUrl();
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.TextField("Remote MCAP URL", remoteUrl);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Copy Remote URL"))
                        EditorGUIUtility.systemCopyBuffer = remoteUrl;

                    if (GUILayout.Button("Open in Foxglove"))
                        OpenFoxgloveTarget(remoteUrl);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    var localPath = replayPath == null ? string.Empty : ResolveProjectPath(replayPath.stringValue);
                    using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(localPath) || !File.Exists(localPath)))
                    {
                        if (GUILayout.Button("Open Local MCAP"))
                            OpenFoxgloveTarget(localPath);
                    }

                    if (GUILayout.Button("Copy Manifest URL"))
                        EditorGUIUtility.systemCopyBuffer = BuildRemoteMcapBaseUrl() + "/v1/manifest";
                }
            }

            EditorGUI.indentLevel--;
        }

        private void DrawCursorBridgeSection()
        {
            if (!FoxgloveManagerInspectorLayout.WorkflowSubsection("Cursor Bridge (Advanced)", ref _cursorBridgeAdvancedExpanded))
                return;

            EditorGUI.indentLevel++;
            EditorGUILayout.HelpBox(
                "Optional loopback endpoint for a Foxglove extension that forwards timeline cursor metadata to Unity replay. It is disabled by default and is separate from Remote Data Loader /v1/data requests.",
                MessageType.Info);
            DrawProperty("_enableReplayCursorBridge", "Enable Cursor Bridge");
            using (new EditorGUI.DisabledScope(!GetBool("_enableReplayCursorBridge")))
            {
                DrawProperty("_replayCursorBridgeHost", "Host");
                DrawProperty("_replayCursorBridgePort", "Port");
                DrawPasswordProperty("_replayCursorBridgeToken", "Bearer Token");
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.TextField(
                        "Endpoint",
                        $"http://{GetString("_replayCursorBridgeHost", "127.0.0.1")}:{GetInt("_replayCursorBridgePort", 8892)}/v1/replay-cursor");
                }
            }

            EditorGUI.indentLevel--;
        }

        private string BuildRemoteMcapBaseUrl()
        {
            var host = GetString("_remoteMcapFileServerHost", "127.0.0.1");
            if (string.IsNullOrWhiteSpace(host))
                host = "127.0.0.1";

            return "http://" + host.Trim() + ":" + GetInt("_remoteMcapFileServerPort", 8891).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private string BuildRemoteMcapDirectFileUrl()
        {
            var sourceId = GetString("_remoteMcapFileServerSourceId", "local-mcap");
            if (string.IsNullOrWhiteSpace(sourceId))
                sourceId = "local-mcap";

            return BuildRemoteMcapBaseUrl() + "/v1/files/" + System.Uri.EscapeDataString(sourceId.Trim()) + ".mcap";
        }

        private static void OpenFoxgloveTarget(string targetArg)
        {
            if (string.IsNullOrWhiteSpace(targetArg))
                return;

            var cli = FindExecutableOnPath("foxglove");
            if (!string.IsNullOrEmpty(cli) && StartProcess(cli, targetArg))
                return;

            var desktop = FindFoxgloveDesktopExecutable();
            if (!string.IsNullOrEmpty(desktop) && StartProcess(desktop, targetArg))
                return;

            EditorGUIUtility.systemCopyBuffer = targetArg;
            Application.OpenURL(targetArg);
        }

        private static bool StartProcess(string executable, string argument)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = QuoteProcessArgument(argument),
                    UseShellExecute = false
                });
                return true;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[Foxglove] Failed to open Foxglove target with " + executable + ": " + ex.Message);
                return false;
            }
        }

        private static string FindFoxgloveDesktopExecutable()
        {
            if (Application.platform != RuntimePlatform.WindowsEditor)
                return string.Empty;

            var localAppData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);
            var programFiles = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles);
            var candidates = new[]
            {
                Path.Combine(localAppData, "Programs", "foxglove", "Foxglove.exe"),
                Path.Combine(programFiles, "Foxglove", "Foxglove.exe")
            };
            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                    return candidate;
            }

            return string.Empty;
        }

        private static string FindExecutableOnPath(string executableName)
        {
            var path = System.Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(path))
                return string.Empty;

            foreach (var directory in path.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(directory))
                    continue;

                var candidate = Path.Combine(directory.Trim(), executableName);
                if (File.Exists(candidate))
                    return candidate;
                if (Application.platform == RuntimePlatform.WindowsEditor && File.Exists(candidate + ".exe"))
                    return candidate + ".exe";
            }

            return string.Empty;
        }

        private static string QuoteProcessArgument(string value)
            => "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";

        private void DrawSchemaEvidenceSection()
        {
            if (!FoxgloveManagerInspectorLayout.WorkflowSubsection("Schema Evidence (Advanced)", ref _schemaEvidenceAdvancedExpanded))
                return;

            EditorGUI.indentLevel++;

            var source = serializedObject.FindProperty("_identityModeSource");
            var overrideMode = serializedObject.FindProperty("_identityModeOverride");
            var projectMode = serializedObject.FindProperty("_projectSettingsIdentityMode");
            var evidenceRoot = serializedObject.FindProperty("_schemaEvidenceRoot");

            if (source == null || overrideMode == null || projectMode == null || evidenceRoot == null)
            {
                DrawMissingProperty("_identityModeSource / _identityModeOverride / _projectSettingsIdentityMode / _schemaEvidenceRoot");
                EditorGUI.indentLevel--;
                return;
            }

            EditorGUILayout.PropertyField(source, new GUIContent("Identity Mode Source"));
            if (source.enumValueIndex == (int)SchemaIdentityModeSource.Override)
            {
                EditorGUILayout.PropertyField(overrideMode, new GUIContent("Identity Mode", IdentityModeTooltip((SchemaIdentityMode)overrideMode.enumValueIndex)));
            }
            else
            {
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.PropertyField(projectMode, new GUIContent("Identity Mode", IdentityModeTooltip((SchemaIdentityMode)projectMode.enumValueIndex)));
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
                    source.enumValueIndex = (int)SchemaIdentityModeSource.ProjectSettings;
                    Unity2FoxgloveSchemaEvidenceSettings.SyncSerializedManager(serializedObject);
                }

                if (GUILayout.Button("Refresh Evidence Now"))
                    GenerateSchemaEvidenceNow();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Open Current Evidence"))
                {
                    Directory.CreateDirectory(Unity2FoxgloveSchemaEvidencePaths.ResolveCurrentEvidenceRoot());
                    EditorUtility.RevealInFinder(Unity2FoxgloveSchemaEvidencePaths.ResolveCurrentEvidenceRoot());
                }

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

        private static void CopyCurrentSchemaEvidenceHash()
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
                EditorGUIUtility.systemCopyBuffer = File.ReadAllText(foxRunHash).Trim();
        }
    }
}
