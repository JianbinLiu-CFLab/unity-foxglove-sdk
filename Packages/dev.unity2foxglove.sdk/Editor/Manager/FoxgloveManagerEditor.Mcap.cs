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
        }

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
