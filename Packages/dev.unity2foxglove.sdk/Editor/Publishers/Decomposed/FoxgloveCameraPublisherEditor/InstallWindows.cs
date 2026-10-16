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
            public static void ShowWindow(Action<string, string> onInstalled)
            {
                var window = CreateInstance<OpenH264InstallWindow>();
                window.titleContent = new GUIContent("Install OpenH264 Runtime");
                window.minSize = new Vector2(640, 360);
                window._onInstalled = onInstalled;
                window._installRoot = OpenH264InstallLocation.GetPreferredInstallRoot();
                window.ShowUtility();
            }

            private bool IsInstalling
                => _installTask != null && !_installTask.IsCompleted;

            private void OnDisable()
            {
                // Do not unregister PollInstallTask - the install runs to completion
                // even if the window is closed, preserving the _onInstalled callback.
            }

            private void OnGUI()
            {
                _scroll = EditorGUILayout.BeginScrollView(_scroll);

                EditorGUILayout.LabelField("OpenH264 runtime is required for H.264 (OpenH264) camera video.", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    "The SDK can download Cisco's official pinned OpenH264 DLL and build the local helper executable from SDK-shipped source and OpenH264 headers. It will not modify PATH, write to project folders, or require admin rights. After validation, only this camera component's OpenH264 helper and DLL paths are updated.",
                    MessageType.Info);
                EditorGUILayout.HelpBox(
                    "Cisco's DLL is downloaded by this machine as an explicit user action. The helper executable is built locally and does not bundle OpenH264 codec code.",
                    MessageType.Warning);
                if (!OpenH264OfficialBinaryInstaller.IsAutomaticInstallSupported)
                {
                    EditorGUILayout.HelpBox(
                        "Automatic installation is unavailable on this Editor platform. Use Manual Download and configure both paths.",
                        MessageType.Warning);
                }

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Source", OpenH264OfficialBinaryManifest.DownloadUrl);
                EditorGUILayout.LabelField("Release", OpenH264OfficialBinaryManifest.Version);
                EditorGUILayout.LabelField("Approximate Size", OpenH264OfficialBinaryManifest.ApproximateSizeLabel);
                EditorGUILayout.LabelField("Compressed SHA256", OpenH264OfficialBinaryManifest.CompressedAssetSha256);
                EditorGUILayout.LabelField("DLL SHA256", OpenH264OfficialBinaryManifest.DllSha256);
                EditorGUILayout.HelpBox(
                    OpenH264OfficialBinaryManifest.Attribution + "\nConfirm Cisco's binary license is appropriate for your project before installing.",
                    MessageType.Warning);

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Install Location", EditorStyles.boldLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    _installRoot = EditorGUILayout.TextField(_installRoot);
                    if (GUILayout.Button("Change...", GUILayout.Width(120)))
                    {
                        var selected = EditorUtility.OpenFolderPanel("Select OpenH264 Install Location", _installRoot, "");
                        if (!string.IsNullOrEmpty(selected))
                            _installRoot = selected;
                    }
                }

                EditorGUILayout.LabelField("Runtime Directory", OpenH264InstallLocation.GetVersionedDirectory(_installRoot));
                EditorGUILayout.LabelField("Helper Target", OpenH264InstallLocation.GetFinalHelperPath(_installRoot));
                EditorGUILayout.LabelField("DLL Target", OpenH264InstallLocation.GetFinalDllPath(_installRoot));

                if (!string.IsNullOrEmpty(_statusMessage))
                    EditorGUILayout.HelpBox(_statusMessage, _statusType);

                EditorGUILayout.EndScrollView();

                EditorGUILayout.Space();
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Manual Download"))
                        Application.OpenURL(OpenH264OfficialBinaryManifest.ReleasePageUrl);

                    using (new EditorGUI.DisabledScope(
                               IsInstalling || !OpenH264OfficialBinaryInstaller.IsAutomaticInstallSupported))
                    {
                        if (GUILayout.Button(IsInstalling ? "Installing..." : "Install OpenH264 Runtime"))
                            Install();
                    }

                    using (new EditorGUI.DisabledScope(IsInstalling))
                    {
                        if (GUILayout.Button("Cancel"))
                            Close();
                    }
                }
            }

            private void Install()
            {
                if (IsInstalling)
                    return;

                if (!OpenH264InstallLocation.IsAllowedInstallRoot(_installRoot, out var reason))
                {
                    _statusMessage = reason;
                    _statusType = MessageType.Error;
                    return;
                }

                _installRootInFlight = _installRoot;
                var packageRoot = OpenH264OfficialBinaryInstaller.GetPackageRoot();
                _statusMessage = "Installing OpenH264 runtime. Unity remains responsive while download and helper build run in the background.";
                _statusType = MessageType.Info;
                _installTask = Task.Run(() => OpenH264OfficialBinaryInstaller.Install(_installRootInFlight, packageRoot));
                EditorApplication.update -= PollInstallTask;
                EditorApplication.update += PollInstallTask;
                Repaint();
            }

            private void PollInstallTask()
            {
                if (_installTask == null || !_installTask.IsCompleted)
                    return;

                EditorApplication.update -= PollInstallTask;
                var task = _installTask;
                _installTask = null;
                var windowStillAlive = this != null;

                OpenH264InstallResult result;
                try
                {
                    result = task.GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    if (!windowStillAlive)
                        return;

                    _statusMessage = ex.Message;
                    _statusType = MessageType.Error;
                    Repaint();
                    return;
                }

                if (result.Success)
                {
                    OpenH264InstallLocation.SavePreferredInstallRoot(_installRootInFlight);
                    _onInstalled?.Invoke(result.HelperPath, result.DllPath);
                    if (!windowStillAlive)
                        return;

                    _statusMessage = "Installed OpenH264 runtime:\nHelper: " + result.HelperPath + "\nDLL: " + result.DllPath;
                    _statusType = MessageType.Info;
                    Close();
                    return;
                }

                if (!windowStillAlive)
                    return;

                _statusMessage = result.ErrorMessage;
                _statusType = MessageType.Error;
                Repaint();
            }
        }

        private sealed class FfmpegHelpWindow : EditorWindow
        {
            private Vector2 _scroll;

            public static void ShowWindow()
            {
                var window = CreateInstance<FfmpegHelpWindow>();
                window.titleContent = new GUIContent("FFmpeg Manual Setup");
                window.minSize = new Vector2(560, 320);
                window.ShowUtility();
            }

            private void OnGUI()
            {
                _scroll = EditorGUILayout.BeginScrollView(_scroll);

                EditorGUILayout.LabelField("FFmpeg is required for H.264/H.265 camera video.", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    "For Asset Store and commercial distribution safety, this SDK does not bundle, download, install, or modify PATH for FFmpeg. Video modes use only the executable configured in FFmpeg Path, or the system PATH when that field is empty.",
                    MessageType.Info);

                EditorGUILayout.HelpBox(
                    "Many FFmpeg builds that support H.264/H.265 use GPL components such as libx264/libx265. Set up FFmpeg yourself only after confirming the chosen build's license is appropriate for your project.",
                    MessageType.Warning);

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Manual Setup", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("1. Install an FFmpeg build that matches your project's license requirements.");
                EditorGUILayout.LabelField("2. Leave FFmpeg Path empty to use the system PATH, or use ... to choose ffmpeg.exe.");
                EditorGUILayout.LabelField("3. Click Check FFmpeg; the SDK checks the configured path only.");
                EditorGUILayout.LabelField("4. Switch back to JPEG for dependency-free camera output.");

                EditorGUILayout.EndScrollView();

                EditorGUILayout.Space();
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Manual Download"))
                        Application.OpenURL("https://ffmpeg.org/download.html");

                    if (GUILayout.Button("Open FFmpeg Legal"))
                        Application.OpenURL("https://www.ffmpeg.org/legal.html");

                    if (GUILayout.Button("Cancel"))
                        Close();
                }
            }
        }
    }
}
