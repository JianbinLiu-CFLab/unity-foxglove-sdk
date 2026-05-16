// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Publishers
// Purpose: Dedicated Inspector for the unified camera publisher.

using System;
using System.IO;
using Foxglove.Schemas.Video;
using Unity.FoxgloveSDK.Components;
using UnityEditor;
using UnityEngine;

namespace Unity.FoxgloveSDK.Editor
{
    [CustomEditor(typeof(FoxgloveCameraPublisher))]
    public class FoxgloveCameraPublisherEditor : UnityEditor.Editor
    {
        private const string FfmpegRecoveryHint =
            "Use ... to browse to an existing executable, leave FFmpeg Path empty for system PATH, or open FFmpeg Help... for manual setup and licensing notes.";
        private const string OpenH264Attribution = "OpenH264 Video Codec provided by Cisco Systems, Inc.";

        private static readonly string[] CameraOutputModeLabels =
        {
            "JPEG",
            "H.264 (FFmpeg)",
            "H.265 / HEVC (FFmpeg)",
            "H.264 (OpenH264)",
            "H.264 (Windows Native, Experimental)"
        };

        private FfmpegExecutableCheckResult _ffmpegCheck =
            new FfmpegExecutableCheckResult(FfmpegExecutableStatus.NotChecked, "", "", "");
        private OpenH264ExecutableCheckResult _openH264Check =
            new OpenH264ExecutableCheckResult(OpenH264ExecutableStatus.NotChecked, "", "", "", "");

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var manager = serializedObject.FindProperty("_manager");
            var topic = serializedObject.FindProperty("_topic");
            var outputMode = serializedObject.FindProperty("_outputMode");
            var publishOnEnable = serializedObject.FindProperty("_publishOnEnable");
            var warnIfManagerMissing = serializedObject.FindProperty("_warnIfManagerMissing");
            var frameId = serializedObject.FindProperty("_frameId");
            var width = serializedObject.FindProperty("_width");
            var height = serializedObject.FindProperty("_height");
            var jpegQuality = serializedObject.FindProperty("_jpegQuality");
            var maxPendingReadbacks = serializedObject.FindProperty("_maxPendingReadbacks");
            var ffmpegPath = serializedObject.FindProperty("_ffmpegPath");
            var openH264HelperPath = serializedObject.FindProperty("_openH264HelperPath");
            var openH264DllPath = serializedObject.FindProperty("_openH264DllPath");
            var openH264MaxInputQueue = serializedObject.FindProperty("_openH264MaxInputQueue");
            var videoBitrateKbps = serializedObject.FindProperty("_videoBitrateKbps");
            var videoKeyframeInterval = serializedObject.FindProperty("_videoKeyframeInterval");
            var videoMaxOutputQueue = serializedObject.FindProperty("_videoMaxOutputQueue");
            var logEncoderStderr = serializedObject.FindProperty("_logEncoderStderr");
            var enableBackpressure = serializedObject.FindProperty("_enableBackpressureAdaptation");
            var backpressureCooldown = serializedObject.FindProperty("_backpressureCooldownSeconds");
            var maxEncodedBytes = serializedObject.FindProperty("_maxEncodedBytes");
            var logBackpressureSkips = serializedObject.FindProperty("_logBackpressureSkips");

            EditorGUILayout.LabelField("Camera Output", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));
            }

            var oldMode = GetMode(outputMode);
            EditorGUI.BeginChangeCheck();
            DrawCameraOutputMode(outputMode);
            if (EditorGUI.EndChangeCheck())
            {
                var newMode = GetMode(outputMode);
                ApplyTopicForModeChange(topic, oldMode, newMode);
                _ffmpegCheck = new FfmpegExecutableCheckResult(FfmpegExecutableStatus.NotChecked, "", "", "");
                _openH264Check = new OpenH264ExecutableCheckResult(OpenH264ExecutableStatus.NotChecked, "", "", "", "");
            }

            EditorGUILayout.PropertyField(manager);
            EditorGUILayout.PropertyField(topic);
            EditorGUILayout.PropertyField(publishOnEnable, new GUIContent("Publish On Enable"));
            EditorGUILayout.PropertyField(warnIfManagerMissing, new GUIContent("Warn If Manager Missing"));
            EditorGUILayout.PropertyField(frameId, new GUIContent("Frame Id"));
            EditorGUILayout.PropertyField(width);
            EditorGUILayout.PropertyField(height);

            var mode = GetMode(outputMode);
            var profile = CameraVideoOutputProfile.ForMode(mode);
            if (mode == CameraOutputMode.H264OpenH264)
            {
                DrawOpenH264VideoSection(
                    profile.DisplayName,
                    openH264HelperPath,
                    openH264DllPath,
                    videoBitrateKbps,
                    videoKeyframeInterval,
                    maxPendingReadbacks,
                    openH264MaxInputQueue,
                    videoMaxOutputQueue,
                    logEncoderStderr);
            }
            else if (mode == CameraOutputMode.H264MediaFoundationExperimental)
            {
                DrawNativeH264Section(
                    profile.DisplayName,
                    videoBitrateKbps,
                    videoKeyframeInterval,
                    maxPendingReadbacks,
                    videoMaxOutputQueue,
                    logEncoderStderr);
            }
            else if (profile.IsVideo)
            {
                DrawVideoSection(mode, profile.DisplayName, ffmpegPath, videoBitrateKbps, videoKeyframeInterval, maxPendingReadbacks, videoMaxOutputQueue, logEncoderStderr);
            }
            else
            {
                DrawJpegSection(jpegQuality, maxPendingReadbacks, enableBackpressure, backpressureCooldown, maxEncodedBytes, logBackpressureSkips);
            }

            DrawPublishRateSection();
            DrawEncodingPolicySection();

            serializedObject.ApplyModifiedProperties();

            DrawResolvedSummaries();
        }

        private void DrawJpegSection(
            SerializedProperty jpegQuality,
            SerializedProperty maxPendingReadbacks,
            SerializedProperty enableBackpressure,
            SerializedProperty backpressureCooldown,
            SerializedProperty maxEncodedBytes,
            SerializedProperty logBackpressureSkips)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("JPEG", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(jpegQuality, new GUIContent("JPEG Quality"));
            EditorGUILayout.PropertyField(maxPendingReadbacks, new GUIContent("Max Pending Readbacks"));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Backpressure", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(enableBackpressure, new GUIContent("Enable Backpressure Adaptation"));
            EditorGUILayout.PropertyField(backpressureCooldown, new GUIContent("Backpressure Cooldown"));
            EditorGUILayout.PropertyField(maxEncodedBytes, new GUIContent("Max Encoded Bytes"));
            EditorGUILayout.PropertyField(logBackpressureSkips, new GUIContent("Log Backpressure Skips"));
        }

        private void DrawVideoSection(
            CameraOutputMode mode,
            string title,
            SerializedProperty ffmpegPath,
            SerializedProperty videoBitrateKbps,
            SerializedProperty videoKeyframeInterval,
            SerializedProperty maxPendingReadbacks,
            SerializedProperty videoMaxOutputQueue,
            SerializedProperty logEncoderStderr)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            if (mode == CameraOutputMode.H265Ffmpeg)
            {
                EditorGUILayout.HelpBox(
                    "H.265/HEVC playback depends on platform decoder support. If Foxglove cannot display it, validate the MCAP or stream with FFmpeg.",
                    MessageType.Warning);
            }

            DrawFfmpegPathField(ffmpegPath);
            var checkRequested = false;
            var revealRequested = false;
            var helpRequested = false;
            var revealPath = GetRevealFfmpegPath(ffmpegPath.stringValue);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Check FFmpeg"))
                    checkRequested = true;

                using (new EditorGUI.DisabledScope(!CanRevealFfmpegFolder(revealPath)))
                {
                    if (GUILayout.Button("Reveal Folder"))
                        revealRequested = true;
                }
            }

            if (checkRequested)
                _ffmpegCheck = FfmpegExecutableCheck.Check(ffmpegPath.stringValue, 2000);

            if (revealRequested)
                RevealFfmpegFolder(revealPath);

            DrawFfmpegStatus(ffmpegPath.stringValue);
            helpRequested = DrawFfmpegHelpAction();

            if (helpRequested)
                FfmpegHelpWindow.ShowWindow();

            EditorGUILayout.PropertyField(videoBitrateKbps, new GUIContent("Video Bitrate Kbps"));
            EditorGUILayout.PropertyField(videoKeyframeInterval, new GUIContent("Keyframe Interval"));
            EditorGUILayout.PropertyField(maxPendingReadbacks, new GUIContent("Max Pending Readbacks"));
            EditorGUILayout.PropertyField(videoMaxOutputQueue, new GUIContent("Max Output Queue"));
            EditorGUILayout.PropertyField(logEncoderStderr, new GUIContent("Log Encoder Stderr"));
        }

        private void DrawOpenH264VideoSection(
            string title,
            SerializedProperty openH264HelperPath,
            SerializedProperty openH264DllPath,
            SerializedProperty videoBitrateKbps,
            SerializedProperty videoKeyframeInterval,
            SerializedProperty maxPendingReadbacks,
            SerializedProperty openH264MaxInputQueue,
            SerializedProperty videoMaxOutputQueue,
            SerializedProperty logEncoderStderr)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "OpenH264 mode uses the local OpenH264 helper executable plus Cisco's official OpenH264 DLL. The SDK does not bundle either binary.",
                MessageType.Info);

            DrawOpenH264PathField(
                "OpenH264 Helper",
                openH264HelperPath,
                "Select OpenH264 Helper Executable",
                Application.platform == RuntimePlatform.WindowsEditor ? "exe" : "",
                () => _openH264Check = new OpenH264ExecutableCheckResult(OpenH264ExecutableStatus.NotChecked, "", "", "", ""));

            DrawOpenH264PathField(
                "OpenH264 DLL",
                openH264DllPath,
                "Select OpenH264 DLL",
                Application.platform == RuntimePlatform.WindowsEditor ? "dll" : "",
                () => _openH264Check = new OpenH264ExecutableCheckResult(OpenH264ExecutableStatus.NotChecked, "", "", "", ""));

            var checkRequested = false;
            var revealRequested = false;
            var installRequested = false;
            var licenseRequested = false;
            var revealPath = GetRevealOpenH264Path(openH264DllPath.stringValue, openH264HelperPath.stringValue);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Install OpenH264 Runtime..."))
                    installRequested = true;

                if (GUILayout.Button("Check OpenH264"))
                    checkRequested = true;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!CanRevealFolder(revealPath)))
                {
                    if (GUILayout.Button("Reveal Folder"))
                        revealRequested = true;
                }

                if (GUILayout.Button("Open License"))
                    licenseRequested = true;
            }

            if (checkRequested)
                _openH264Check = OpenH264ExecutableCheck.Check(openH264HelperPath.stringValue, openH264DllPath.stringValue, 3000);

            if (revealRequested)
                RevealFolder(revealPath);

            if (installRequested)
            {
                OpenH264InstallWindow.ShowWindow((installedHelperPath, installedDllPath) =>
                {
                    serializedObject.Update();
                    openH264HelperPath.stringValue = installedHelperPath;
                    openH264DllPath.stringValue = installedDllPath;
                    serializedObject.ApplyModifiedProperties();
                    _openH264Check = OpenH264ExecutableCheck.Check(installedHelperPath, installedDllPath, 3000);
                    Repaint();
                });
            }

            if (licenseRequested)
                Application.OpenURL(OpenH264OfficialBinaryManifest.BinaryLicenseUrl);

            DrawOpenH264Status(openH264HelperPath.stringValue, openH264DllPath.stringValue);
            EditorGUILayout.HelpBox(OpenH264Attribution, MessageType.None);

            EditorGUILayout.PropertyField(videoBitrateKbps, new GUIContent("Video Bitrate Kbps"));
            EditorGUILayout.PropertyField(videoKeyframeInterval, new GUIContent("Keyframe Interval"));
            EditorGUILayout.PropertyField(maxPendingReadbacks, new GUIContent("Max Pending Readbacks"));
            EditorGUILayout.PropertyField(openH264MaxInputQueue, new GUIContent("Max Input Queue"));
            EditorGUILayout.PropertyField(videoMaxOutputQueue, new GUIContent("Max Output Queue"));
            EditorGUILayout.PropertyField(logEncoderStderr, new GUIContent("Log Encoder Diagnostics"));
        }

        private static void DrawNativeH264Section(
            string title,
            SerializedProperty videoBitrateKbps,
            SerializedProperty videoKeyframeInterval,
            SerializedProperty maxPendingReadbacks,
            SerializedProperty videoMaxOutputQueue,
            SerializedProperty logEncoderStderr)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Experimental Windows-only H.264 path using Media Foundation. It does not use FFmpeg, OpenH264, or external binaries.",
                MessageType.Warning);
            EditorGUILayout.HelpBox(
                "This backend depends on Windows encoder availability and driver behavior. Prefer OpenH264 for predictable cross-platform behavior.",
                MessageType.Info);

            EditorGUILayout.PropertyField(videoBitrateKbps, new GUIContent("Video Bitrate Kbps"));
            EditorGUILayout.PropertyField(videoKeyframeInterval, new GUIContent("Keyframe Interval"));
            EditorGUILayout.PropertyField(maxPendingReadbacks, new GUIContent("Max Pending Readbacks"));
            EditorGUILayout.PropertyField(videoMaxOutputQueue, new GUIContent("Max Output Queue"));
            EditorGUILayout.PropertyField(logEncoderStderr, new GUIContent("Log Encoder Diagnostics"));
        }

        private void DrawPublishRateSection()
        {
            var publishRateSource = serializedObject.FindProperty("_publishRateSource");
            var publishRateHz = serializedObject.FindProperty("_publishRateHz");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Publish Rate", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(publishRateSource, new GUIContent("Publish Rate Source"));

            var usesLocalRate = publishRateSource.enumValueIndex == (int)PublisherRateSource.OverrideLocal;
            using (new EditorGUI.DisabledScope(!usesLocalRate))
            {
                EditorGUILayout.PropertyField(publishRateHz, new GUIContent("Publish Rate Hz"));
            }
        }

        private void DrawEncodingPolicySection()
        {
            var encodingOverride = serializedObject.FindProperty("_encodingOverride");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Encoding Policy", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(encodingOverride, new GUIContent("Encoding Override"));
        }

        private void DrawResolvedSummaries()
        {
            var publisher = (FoxgloveCameraPublisher)target;
            var resolution = publisher.EncodingResolution;

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.FloatField("Effective Publish Rate Hz", publisher.EffectivePublishRateHz);
            }

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField("Supported Encodings", publisher.SupportedEncodingSummary);
                EditorGUILayout.EnumPopup("Effective Encoding", resolution.Effective);
            }

            if (publisher.ConfiguredManager != null
                && !publisher.ConfiguredManager.AllowPublisherOverride
                && publisher.EncodingOverride != PublisherEncodingOverride.UseManager)
            {
                EditorGUILayout.HelpBox(
                    "FoxgloveManager disables publisher overrides; the global default is used.",
                    MessageType.Info);
            }
            else if (resolution.Effective == PublisherEffectiveEncoding.Unsupported)
            {
                EditorGUILayout.HelpBox(
                    "This publisher declares no supported encoding and will not publish messages.",
                    MessageType.Error);
            }
            else if (resolution.FellBack)
            {
                EditorGUILayout.HelpBox(
                    $"Requested {resolution.RequestedLabel}, but this publisher will emit {resolution.EffectiveLabel}.",
                    MessageType.Warning);
            }
        }

        private static void DrawCameraOutputMode(SerializedProperty outputMode)
        {
            if (outputMode == null)
                return;

            var currentIndex = outputMode.enumValueIndex;
            if (currentIndex < 0 || currentIndex >= CameraOutputModeLabels.Length)
                currentIndex = 0;

            outputMode.enumValueIndex = EditorGUILayout.Popup("Camera Output Mode", currentIndex, CameraOutputModeLabels);
        }

        private static CameraOutputMode GetMode(SerializedProperty outputMode)
            => outputMode == null ? CameraOutputMode.Jpeg : (CameraOutputMode)outputMode.enumValueIndex;

        private static void ApplyTopicForModeChange(SerializedProperty topic, CameraOutputMode oldMode, CameraOutputMode newMode)
        {
            if (topic == null || oldMode == newMode)
                return;

            var oldDefault = CameraVideoOutputProfile.ForMode(oldMode).DefaultTopic;
            var newDefault = CameraVideoOutputProfile.ForMode(newMode).DefaultTopic;
            if (string.IsNullOrEmpty(topic.stringValue) || topic.stringValue == oldDefault)
                topic.stringValue = newDefault;
        }

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
                    _ffmpegCheck = new FfmpegExecutableCheckResult(FfmpegExecutableStatus.NotChecked, "", "", "");
                }

                if (GUILayout.Button("...", GUILayout.Width(30)))
                {
                    BrowseFfmpeg(ffmpegPath);
                    _ffmpegCheck = new FfmpegExecutableCheckResult(FfmpegExecutableStatus.NotChecked, "", "", "");
                }
            }

            EditorGUILayout.HelpBox(
                "Empty path uses system PATH (ffmpeg). Use ... to choose an explicit executable.",
                MessageType.None);
        }

        private static void BrowseFfmpeg(SerializedProperty ffmpegPath)
        {
            var current = ffmpegPath.stringValue;
            var defaultDir = "";
            if (!string.IsNullOrWhiteSpace(current) && Path.IsPathRooted(current))
                defaultDir = File.Exists(current) ? Path.GetDirectoryName(current) : Path.GetDirectoryName(current);

            var extension = Application.platform == RuntimePlatform.WindowsEditor ? "exe" : "";
            var selected = EditorUtility.OpenFilePanel("Select FFmpeg Executable", defaultDir ?? "", extension);
            if (!string.IsNullOrEmpty(selected))
                ffmpegPath.stringValue = selected;
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
                    BrowseOpenH264Path(property, dialogTitle, extension);
                    onChanged?.Invoke();
                }
            }
        }

        private static void BrowseOpenH264Path(SerializedProperty property, string dialogTitle, string extension)
        {
            var current = property.stringValue;
            var defaultDir = "";
            if (!string.IsNullOrWhiteSpace(current) && Path.IsPathRooted(current))
                defaultDir = File.Exists(current) ? Path.GetDirectoryName(current) : Path.GetDirectoryName(current);

            var selected = EditorUtility.OpenFilePanel(dialogTitle, defaultDir ?? "", extension);
            if (!string.IsNullOrEmpty(selected))
                property.stringValue = selected;
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

        private void DrawOpenH264Status(string helperPath, string dllPath)
        {
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

        private sealed class OpenH264InstallWindow : EditorWindow
        {
            private Action<string, string> _onInstalled;
            private Vector2 _scroll;
            private string _installRoot;
            private string _statusMessage;
            private MessageType _statusType;

            public static void ShowWindow(Action<string, string> onInstalled)
            {
                var window = CreateInstance<OpenH264InstallWindow>();
                window.titleContent = new GUIContent("Install OpenH264 Runtime");
                window.minSize = new Vector2(640, 360);
                window._onInstalled = onInstalled;
                window._installRoot = OpenH264InstallLocation.GetPreferredInstallRoot();
                window.ShowUtility();
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

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Source", OpenH264OfficialBinaryManifest.DownloadUrl);
                EditorGUILayout.LabelField("Release", OpenH264OfficialBinaryManifest.Version);
                EditorGUILayout.LabelField("Approximate Size", OpenH264OfficialBinaryManifest.ApproximateSizeLabel);
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

                    if (GUILayout.Button("Install OpenH264 Runtime"))
                        Install();

                    if (GUILayout.Button("Cancel"))
                        Close();
                }
            }

            private void Install()
            {
                if (!OpenH264InstallLocation.IsAllowedInstallRoot(_installRoot, out var reason))
                {
                    _statusMessage = reason;
                    _statusType = MessageType.Error;
                    return;
                }

                var result = OpenH264OfficialBinaryInstaller.Install(_installRoot);
                if (result.Success)
                {
                    OpenH264InstallLocation.SavePreferredInstallRoot(_installRoot);
                    _onInstalled?.Invoke(result.HelperPath, result.DllPath);
                    _statusMessage = "Installed OpenH264 runtime:\nHelper: " + result.HelperPath + "\nDLL: " + result.DllPath;
                    _statusType = MessageType.Info;
                    Close();
                    return;
                }

                _statusMessage = result.ErrorMessage;
                _statusType = MessageType.Error;
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
