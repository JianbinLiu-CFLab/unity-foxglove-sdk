// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Publishers
// Purpose: Phase192 source section for the camera publisher Inspector.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Foxglove.Schemas.Video;
using Unity.FoxgloveSDK.Components;
using UnityEditor;
using UnityEngine;

namespace Unity.FoxgloveSDK.Editor
{
    public partial class FoxgloveCameraPublisherEditor
    {
        private void DrawFfmpegPathField(SerializedProperty ffmpegPath)
        {
            EditorGUILayout.LabelField("FFmpeg Path", EditorStyles.miniBoldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                var nextPath = EditorGUILayout.TextField(ffmpegPath.stringValue);
                if (EditorGUI.EndChangeCheck())
                {
                    ffmpegPath.stringValue = nextPath;
                    ResetFfmpegCheck();
                }

                if (GUILayout.Button("...", GUILayout.Width(30)))
                {
                    ScheduleBrowsePath(
                        ffmpegPath,
                        "Select FFmpeg Executable",
                        Application.platform == RuntimePlatform.WindowsEditor ? "exe" : "",
                        ResetFfmpegCheck);
                }
            }

            EditorGUILayout.HelpBox(
                "Empty path uses system PATH (ffmpeg). Use ... to choose an explicit executable.",
                MessageType.None);
        }

        private static void DrawOpenH264PathField(
            string label,
            SerializedProperty property,
            string dialogTitle,
            string extension,
            Action onChanged)
        {
            EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                var nextPath = EditorGUILayout.TextField(property.stringValue);
                if (EditorGUI.EndChangeCheck())
                {
                    property.stringValue = nextPath;
                    onChanged?.Invoke();
                }

                if (GUILayout.Button("...", GUILayout.Width(30)))
                {
                    ScheduleBrowsePath(property, dialogTitle, extension, onChanged);
                }
            }
        }

        private static void ScheduleBrowsePath(
            SerializedProperty property,
            string dialogTitle,
            string extension,
            Action onChanged)
        {
            var capturedProperty = property.Copy();
            var defaultDir = ResolveBrowseDefaultDirectory(property.stringValue);
            EditorApplication.delayCall += () =>
            {
                var serializedObject = TryGetSerializedObject(capturedProperty);
                if (serializedObject == null || serializedObject.targetObject == null)
                    return;

                var selected = EditorUtility.OpenFilePanel(dialogTitle, defaultDir, extension);
                if (string.IsNullOrEmpty(selected))
                    return;

                if (!TryApplyBrowsePath(serializedObject, capturedProperty, selected))
                    return;

                onChanged?.Invoke();
            };
        }

        private static SerializedObject TryGetSerializedObject(SerializedProperty property)
        {
            try
            {
                return property.serializedObject;
            }
            catch (MissingReferenceException)
            {
                return null;
            }
            catch (NullReferenceException)
            {
                return null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        private static bool TryApplyBrowsePath(SerializedObject serializedObject, SerializedProperty property, string selected)
        {
            try
            {
                serializedObject.Update();
                property.stringValue = selected;
                serializedObject.ApplyModifiedProperties();
                return true;
            }
            catch (MissingReferenceException)
            {
                return false;
            }
            catch (NullReferenceException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private static string ResolveBrowseDefaultDirectory(string current)
        {
            if (!string.IsNullOrWhiteSpace(current) && Path.IsPathRooted(current))
            {
                if (File.Exists(current))
                    return Path.GetDirectoryName(current) ?? FoxgloveManagerEditor.GetDefaultDir();
                if (Directory.Exists(current))
                    return current;

                var parent = Path.GetDirectoryName(current);
                if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                    return parent;
            }

            return FoxgloveManagerEditor.GetDefaultDir();
        }

        private static bool CanRevealFfmpegFolder(string configuredPath)
            => !string.IsNullOrEmpty(GetFfmpegFolderPath(configuredPath));

        private static void RevealFfmpegFolder(string configuredPath)
        {
            var dir = GetFfmpegFolderPath(configuredPath);
            if (string.IsNullOrEmpty(dir))
                return;

            EditorUtility.OpenWithDefaultApp(dir);
        }

        private static string GetFfmpegFolderPath(string configuredPath)
        {
            if (string.IsNullOrWhiteSpace(configuredPath) || !Path.IsPathRooted(configuredPath))
                return "";

            try
            {
                if (File.Exists(configuredPath))
                    return Path.GetDirectoryName(configuredPath) ?? "";

                if (Directory.Exists(configuredPath))
                    return configuredPath;

                var dir = Path.GetDirectoryName(configuredPath);
                return !string.IsNullOrEmpty(dir) && Directory.Exists(dir) ? dir : "";
            }
            catch
            {
                return "";
            }
        }

        private string GetRevealFfmpegPath(string configuredPath)
        {
            if (!string.IsNullOrWhiteSpace(_ffmpegCheck.ExecutablePath)
                && Path.IsPathRooted(_ffmpegCheck.ExecutablePath)
                && File.Exists(_ffmpegCheck.ExecutablePath))
            {
                return _ffmpegCheck.ExecutablePath;
            }

            return configuredPath;
        }

        private static bool CanRevealFolder(string configuredPath)
            => !string.IsNullOrEmpty(GetFolderPath(configuredPath));

        private static void RevealFolder(string configuredPath)
        {
            var dir = GetFolderPath(configuredPath);
            if (string.IsNullOrEmpty(dir))
                return;

            EditorUtility.OpenWithDefaultApp(dir);
        }

        private static string GetFolderPath(string configuredPath)
        {
            if (string.IsNullOrWhiteSpace(configuredPath) || !Path.IsPathRooted(configuredPath))
                return "";

            try
            {
                if (File.Exists(configuredPath))
                    return Path.GetDirectoryName(configuredPath) ?? "";

                if (Directory.Exists(configuredPath))
                    return configuredPath;

                var dir = Path.GetDirectoryName(configuredPath);
                return !string.IsNullOrEmpty(dir) && Directory.Exists(dir) ? dir : "";
            }
            catch
            {
                return "";
            }
        }

        private string GetRevealOpenH264Path(string dllPath, string helperPath)
        {
            if (!string.IsNullOrWhiteSpace(_openH264Check.DllPath)
                && Path.IsPathRooted(_openH264Check.DllPath)
                && File.Exists(_openH264Check.DllPath))
            {
                return _openH264Check.DllPath;
            }

            if (!string.IsNullOrWhiteSpace(dllPath))
                return dllPath;

            return helperPath;
        }

    }
}
