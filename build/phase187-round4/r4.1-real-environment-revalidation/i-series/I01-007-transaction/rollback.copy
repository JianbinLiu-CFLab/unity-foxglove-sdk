// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Manager

using System.IO;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Transport;
using UnityEngine;
using UnityEditor;

namespace Unity.FoxgloveSDK.Editor
{
    public partial class FoxgloveManagerEditor : UnityEditor.Editor
    {
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
}
