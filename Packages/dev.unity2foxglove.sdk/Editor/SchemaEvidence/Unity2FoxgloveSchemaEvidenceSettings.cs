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
    /// Project-level schema evidence settings used by generators and the
    /// FoxgloveManager Inspector.
    /// </summary>
    [FilePath("ProjectSettings/Unity2FoxgloveSchemaEvidenceSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class Unity2FoxgloveSchemaEvidenceSettings : ScriptableSingleton<Unity2FoxgloveSchemaEvidenceSettings>
    {
        [SerializeField] private SchemaIdentityMode _defaultIdentityMode = SchemaIdentityMode.Off;
        [SerializeField] private string _currentEvidenceRoot = Unity2FoxgloveSchemaEvidencePaths.DefaultCurrentEvidenceRoot;

        public static SchemaIdentityMode DefaultIdentityMode
        {
            get => instance._defaultIdentityMode;
            set
            {
                instance._defaultIdentityMode = value;
                instance.Save(true);
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
                instance.Save(true);
            }
        }

        public static void SaveSettings()
        {
            instance.Save(true);
        }

        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            return new SettingsProvider("Project/Unity2Foxglove/Schema Evidence", SettingsScope.Project)
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
                instance.Save(true);
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
                    instance.Save(true);
                }
            }
        }
    }
}
