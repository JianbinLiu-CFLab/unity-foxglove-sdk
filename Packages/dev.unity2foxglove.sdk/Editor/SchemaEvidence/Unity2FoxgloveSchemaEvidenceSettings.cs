// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/SchemaEvidence
// Purpose: Stores project-level schema evidence and identity defaults.

using Unity.FoxgloveSDK.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.FoxgloveSDK.Editor
{
    /// <summary>
    /// Project Settings owner for schema evidence defaults, synchronized into
    /// scene Manager instances so Inspector overrides start from one policy.
    /// </summary>
    [FilePath("ProjectSettings/Unity2FoxgloveSchemaEvidenceSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class Unity2FoxgloveSchemaEvidenceSettings : ScriptableSingleton<Unity2FoxgloveSchemaEvidenceSettings>
    {
        internal const string SettingsPath = "Project/Unity2Foxglove/Schema Evidence";

        [SerializeField] private SchemaIdentityMode _defaultIdentityMode = SchemaIdentityMode.Off;
        [SerializeField] private string _currentEvidenceRoot = Unity2FoxgloveSchemaEvidencePaths.DefaultCurrentEvidenceRoot;

        private static string s_lastValidatedRoot;
        private static bool s_lastRootValid;
        private static string s_lastNormalizedRoot;
        private static string s_lastRootError;
        private static string s_resolvedRootCacheKey;
        private static string s_resolvedRootCacheValue;

        public static SchemaIdentityMode DefaultIdentityMode
        {
            get => instance._defaultIdentityMode;
            set
            {
                instance._defaultIdentityMode = value;
                SaveAndSync();
            }
        }

        public static string CurrentEvidenceRoot
        {
            get => string.IsNullOrWhiteSpace(instance._currentEvidenceRoot)
                ? Unity2FoxgloveSchemaEvidencePaths.DefaultCurrentEvidenceRoot
                : instance._currentEvidenceRoot;
            set
            {
                if (!Unity2FoxgloveSchemaEvidencePaths.TryNormalizeAssetsRoot(value, out var normalized, out _))
                    return;

                instance._currentEvidenceRoot = normalized;
                Unity2FoxgloveSchemaEvidencePaths.InvalidateCurrentEvidenceRootCache();
                SaveAndSync();
            }
        }

        public static void SaveSettings()
        {
            SaveAndSync();
        }

        internal static bool SyncSerializedManager(SerializedObject serializedObject)
        {
            if (serializedObject == null)
                return false;

            var projectMode = serializedObject.FindProperty("_projectSettingsIdentityMode");
            var evidenceRoot = serializedObject.FindProperty("_schemaEvidenceRoot");
            if (projectMode == null || evidenceRoot == null)
                return false;

            var changed = false;
            var mode = (int)DefaultIdentityMode;
            if (projectMode.enumValueIndex != mode)
            {
                projectMode.enumValueIndex = mode;
                changed = true;
            }

            var root = Unity2FoxgloveSchemaEvidencePaths.CurrentEvidenceRootProjectRelative;
            if (evidenceRoot.stringValue != root)
            {
                evidenceRoot.stringValue = root;
                changed = true;
            }

            return changed;
        }

        internal static void SyncOpenSceneManagers()
        {
            for (var i = 0; i < SceneManager.sceneCount; i++)
                SyncManagersInScene(SceneManager.GetSceneAt(i));
        }

        internal static void SyncManagersInScene(UnityEngine.SceneManagement.Scene scene)
        {
            if (!scene.IsValid())
                return;

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var manager in root.GetComponentsInChildren<Unity.FoxgloveSDK.Components.FoxgloveManager>(true))
                    SyncManager(manager);
            }
        }

        private static void SyncManager(Unity.FoxgloveSDK.Components.FoxgloveManager manager)
        {
            using (var serialized = new SerializedObject(manager))
            {
                if (!SyncSerializedManager(serialized))
                    return;

                Undo.RecordObject(manager, "Sync Unity2Foxglove Schema Evidence Settings");
                serialized.ApplyModifiedProperties();
                EditorUtility.SetDirty(manager);
            }
        }

        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            return new SettingsProvider(SettingsPath, SettingsScope.Project)
            {
                label = "Schema Evidence & Identity",
                guiHandler = _ => DrawSettings()
            };
        }

        private static void DrawSettings()
        {
            var previousMode = DefaultIdentityMode;
            var previousRoot = CurrentEvidenceRoot;

            EditorGUI.BeginChangeCheck();
            var mode = (SchemaIdentityMode)EditorGUILayout.EnumPopup("Default Identity Mode", previousMode);
            var root = EditorGUILayout.TextField("Current Evidence Root", previousRoot);
            var changed = EditorGUI.EndChangeCheck();

            var shouldSave = false;
            if (changed && mode != previousMode)
            {
                instance._defaultIdentityMode = mode;
                shouldSave = true;
            }

            if (!TryNormalizeAssetsRootCached(root, out var normalized, out var error))
            {
                EditorGUILayout.HelpBox(error, MessageType.Error);
            }
            else if (changed && !string.Equals(previousRoot, normalized, System.StringComparison.Ordinal))
            {
                instance._currentEvidenceRoot = normalized;
                Unity2FoxgloveSchemaEvidencePaths.InvalidateCurrentEvidenceRootCache();
                shouldSave = true;
            }

            if (shouldSave)
                SaveAndSync();

            EditorGUILayout.Space();
            var resolvedRoot = ResolveCurrentEvidenceRootCached();
            EditorGUILayout.LabelField("Resolved Current Evidence Root", resolvedRoot);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Open Current Evidence"))
                {
                    System.IO.Directory.CreateDirectory(resolvedRoot);
                    EditorUtility.RevealInFinder(resolvedRoot);
                }

                if (GUILayout.Button("Reset Defaults"))
                {
                    instance._defaultIdentityMode = SchemaIdentityMode.Off;
                    instance._currentEvidenceRoot = Unity2FoxgloveSchemaEvidencePaths.DefaultCurrentEvidenceRoot;
                    Unity2FoxgloveSchemaEvidencePaths.InvalidateCurrentEvidenceRootCache();
                    SaveAndSync();
                }
            }
        }

        private static void SaveAndSync()
        {
            instance.Save(true);
            SyncOpenSceneManagers();
        }

        private static bool TryNormalizeAssetsRootCached(string root, out string normalized, out string error)
        {
            if (!string.Equals(root, s_lastValidatedRoot, System.StringComparison.Ordinal))
            {
                s_lastValidatedRoot = root;
                s_lastRootValid = Unity2FoxgloveSchemaEvidencePaths.TryNormalizeAssetsRoot(root, out s_lastNormalizedRoot, out s_lastRootError);
            }

            normalized = s_lastNormalizedRoot;
            error = s_lastRootError;
            return s_lastRootValid;
        }

        private static string ResolveCurrentEvidenceRootCached()
        {
            var key = CurrentEvidenceRoot;
            if (!string.Equals(key, s_resolvedRootCacheKey, System.StringComparison.Ordinal))
            {
                s_resolvedRootCacheKey = key;
                s_resolvedRootCacheValue = Unity2FoxgloveSchemaEvidencePaths.ResolveCurrentEvidenceRoot();
            }

            return s_resolvedRootCacheValue;
        }
    }
}
