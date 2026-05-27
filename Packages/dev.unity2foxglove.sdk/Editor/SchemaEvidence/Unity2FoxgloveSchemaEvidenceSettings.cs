// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/SchemaEvidence
// Purpose: Stores project-level schema evidence and identity defaults.

using Unity.FoxgloveSDK.Core;
using UnityEditor;
using UnityEngine;

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
            foreach (var manager in Resources.FindObjectsOfTypeAll<Unity.FoxgloveSDK.Components.FoxgloveManager>())
            {
                if (manager == null || EditorUtility.IsPersistent(manager))
                    continue;

                SyncManager(manager);
            }
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
            var serialized = new SerializedObject(manager);
            if (!SyncSerializedManager(serialized))
                return;

            Undo.RecordObject(manager, "Sync Unity2Foxglove Schema Evidence Settings");
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(manager);
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
            EditorGUI.BeginChangeCheck();
            var mode = (SchemaIdentityMode)EditorGUILayout.EnumPopup("Default Identity Mode", DefaultIdentityMode);
            var root = EditorGUILayout.TextField("Current Evidence Root", CurrentEvidenceRoot);
            var changed = EditorGUI.EndChangeCheck();

            if (!Unity2FoxgloveSchemaEvidencePaths.TryNormalizeAssetsRoot(root, out var normalized, out var error))
            {
                EditorGUILayout.HelpBox(error, MessageType.Error);
            }
            else if (changed)
            {
                instance._defaultIdentityMode = mode;
                instance._currentEvidenceRoot = normalized;
                SaveAndSync();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Resolved Current Evidence Root", Unity2FoxgloveSchemaEvidencePaths.ResolveCurrentEvidenceRoot());
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Open Current Evidence"))
                {
                    System.IO.Directory.CreateDirectory(Unity2FoxgloveSchemaEvidencePaths.ResolveCurrentEvidenceRoot());
                    EditorUtility.RevealInFinder(Unity2FoxgloveSchemaEvidencePaths.ResolveCurrentEvidenceRoot());
                }

                if (GUILayout.Button("Reset Defaults"))
                {
                    instance._defaultIdentityMode = SchemaIdentityMode.Off;
                    instance._currentEvidenceRoot = Unity2FoxgloveSchemaEvidencePaths.DefaultCurrentEvidenceRoot;
                    SaveAndSync();
                }
            }
        }

        private static void SaveAndSync()
        {
            instance.Save(true);
            SyncOpenSceneManagers();
        }
    }
}
