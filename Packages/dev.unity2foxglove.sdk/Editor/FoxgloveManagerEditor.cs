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
    /// Custom Inspector for <c>FoxgloveManager</c> that renders Browse buttons
    /// for file/folder path fields alongside the default property drawer.
    /// </summary>
    [CustomEditor(typeof(Components.FoxgloveManager))]
    public class FoxgloveManagerEditor : UnityEditor.Editor
    {
        /// <summary>
        /// Draws the default Inspector with Browse buttons for
        /// <c>_replayFilePath</c> and <c>_recordingDirectory</c> fields.
        /// </summary>
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var prop = serializedObject.GetIterator();
            if (prop.NextVisible(true))
            {
                do
                {
                    if (prop.name == "_replayFilePath")
                        DrawPathBrowse(prop, "Select MCAP File", "mcap", true, GetSmartDefault(prop.stringValue, true));
                    else if (prop.name == "_recordingDirectory")
                        DrawPathBrowse(prop, "Select Recording Directory", "", false, GetSmartDefault(prop.stringValue, false));
                    else
                        EditorGUILayout.PropertyField(prop, true);
                }
                while (prop.NextVisible(false));
            }

            serializedObject.ApplyModifiedProperties();
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

            // Default to Recordings/ if it exists (only for directories, not files)
            if (!isFile)
            {
                var recordingsDir = Path.Combine(GetDefaultDir(), "Recordings");
                if (Directory.Exists(recordingsDir))
                    return recordingsDir;
            }

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
