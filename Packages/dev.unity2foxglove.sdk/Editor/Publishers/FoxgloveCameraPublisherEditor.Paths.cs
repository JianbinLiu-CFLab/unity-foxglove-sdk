// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

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

        private void DrawFfmpegStatus(string configuredPath)
        {
            if (_ffmpegCheckTask != null)
            {
                EditorGUILayout.HelpBox("Checking FFmpeg executable...", MessageType.Info);
                return;
            }

            switch (_ffmpegCheck.Status)
            {
                case FfmpegExecutableStatus.Found:
                    var foundMessage = "Found: " + _ffmpegCheck.VersionLine;
                    if (!string.IsNullOrEmpty(_ffmpegCheck.ExecutablePath))
                        foundMessage += "\nPath: " + _ffmpegCheck.ExecutablePath;
                    EditorGUILayout.HelpBox(foundMessage, MessageType.Info);
                    break;
                case FfmpegExecutableStatus.Missing:
                    EditorGUILayout.HelpBox(
                        "FFmpeg was not found at the configured path. " + FfmpegRecoveryHint,
                        MessageType.Warning);
                    break;
                case FfmpegExecutableStatus.Invalid:
                    EditorGUILayout.HelpBox(
                        string.IsNullOrEmpty(_ffmpegCheck.ErrorMessage)
                            ? "Configured FFmpeg did not return a recognizable version. " + FfmpegRecoveryHint
                            : _ffmpegCheck.ErrorMessage + "\n" + FfmpegRecoveryHint,
                        MessageType.Error);
                    break;
                case FfmpegExecutableStatus.NotChecked:
                default:
                    var label = string.IsNullOrWhiteSpace(configuredPath) ? "system PATH: ffmpeg" : configuredPath;
                    EditorGUILayout.HelpBox("Status: Not Checked (" + label + ")", MessageType.None);
                    break;
            }
        }

        private static bool DrawFfmpegHelpAction()
            => GUILayout.Button("FFmpeg Help...");

        private void ResetFfmpegCheck()
        {
            EditorApplication.update -= CompleteFfmpegCheckIfReady;
            _ffmpegCheckTask = null;
            _ffmpegCheck = new FfmpegExecutableCheckResult(FfmpegExecutableStatus.NotChecked, "", "", "");
        }

        private void StartFfmpegCheck(string ffmpegPath)
        {
            ResetFfmpegCheck();
            _ffmpegCheckTask = Task.Run(() => FfmpegExecutableCheck.Check(ffmpegPath, 2000));
            EditorApplication.update -= CompleteFfmpegCheckIfReady;
            EditorApplication.update += CompleteFfmpegCheckIfReady;
            Repaint();
        }

        private void CompleteFfmpegCheckIfReady()
        {
            if (_ffmpegCheckTask == null || !_ffmpegCheckTask.IsCompleted)
                return;

            EditorApplication.update -= CompleteFfmpegCheckIfReady;
            var task = _ffmpegCheckTask;
            _ffmpegCheckTask = null;

            try
            {
                _ffmpegCheck = task.GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _ffmpegCheck = new FfmpegExecutableCheckResult(
                    FfmpegExecutableStatus.Invalid,
                    "",
                    "",
                    ex.Message);
            }

            if (this != null)
                Repaint();
        }

        private void DrawOpenH264Status(string helperPath, string dllPath)
        {
            if (_openH264CheckTask != null)
            {
                EditorGUILayout.HelpBox("Checking OpenH264 helper and DLL...", MessageType.Info);
                return;
            }

            switch (_openH264Check.Status)
            {
                case OpenH264ExecutableStatus.Found:
                    var foundMessage = "Found: OpenH264 helper and DLL validated.";
                    if (!string.IsNullOrEmpty(_openH264Check.HelperPath))
                        foundMessage += "\nHelper: " + _openH264Check.HelperPath;
                    if (!string.IsNullOrEmpty(_openH264Check.DllPath))
                        foundMessage += "\nDLL: " + _openH264Check.DllPath;
                    EditorGUILayout.HelpBox(foundMessage, MessageType.Info);
                    break;
                case OpenH264ExecutableStatus.Missing:
                    EditorGUILayout.HelpBox(
                        string.IsNullOrEmpty(_openH264Check.ErrorMessage)
                            ? "OpenH264 helper or DLL was not found. Choose both paths manually or use Install OpenH264 Runtime... to install the local helper and official Cisco DLL."
                            : _openH264Check.ErrorMessage,
                        MessageType.Warning);
                    break;
                case OpenH264ExecutableStatus.Invalid:
                    EditorGUILayout.HelpBox(
                        string.IsNullOrEmpty(_openH264Check.ErrorMessage)
                            ? "OpenH264 validation failed."
                            : _openH264Check.ErrorMessage,
                        MessageType.Error);
                    break;
                case OpenH264ExecutableStatus.NotChecked:
                default:
                    var helperLabel = string.IsNullOrWhiteSpace(helperPath) ? "not configured" : helperPath;
                    var dllLabel = string.IsNullOrWhiteSpace(dllPath) ? "not configured" : dllPath;
                    EditorGUILayout.HelpBox("Status: Not Checked\nHelper: " + helperLabel + "\nDLL: " + dllLabel, MessageType.None);
                    break;
            }
        }

        private void ResetOpenH264Check()
        {
            EditorApplication.update -= CompleteOpenH264CheckIfReady;
            _openH264CheckTask = null;
            _openH264Check = new OpenH264ExecutableCheckResult(OpenH264ExecutableStatus.NotChecked, "", "", "", "");
        }

        private void StartOpenH264Check(string helperPath, string dllPath)
        {
            ResetOpenH264Check();
            _openH264CheckTask = Task.Run(() => OpenH264ExecutableCheck.Check(helperPath, dllPath, 3000));
            EditorApplication.update -= CompleteOpenH264CheckIfReady;
            EditorApplication.update += CompleteOpenH264CheckIfReady;
            Repaint();
        }

        private void CompleteOpenH264CheckIfReady()
        {
            if (_openH264CheckTask == null || !_openH264CheckTask.IsCompleted)
                return;

            EditorApplication.update -= CompleteOpenH264CheckIfReady;
            var task = _openH264CheckTask;
            _openH264CheckTask = null;

            try
            {
                _openH264Check = task.GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _openH264Check = new OpenH264ExecutableCheckResult(
                    OpenH264ExecutableStatus.Invalid,
                    "",
                    "",
                    "",
                    ex.Message);
            }

            if (this != null)
                Repaint();
        }


    }
}
