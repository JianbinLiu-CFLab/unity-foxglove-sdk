// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Publishers
// Purpose: Dedicated Inspector for the unified camera publisher.

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
        private static readonly string[] CameraOutputModeLabels =
        {
            "JPEG",
            "H.264 (FFmpeg)",
            "H.265 / HEVC (FFmpeg)"
        };

        private FfmpegExecutableCheckResult _ffmpegCheck =
            new FfmpegExecutableCheckResult(FfmpegExecutableStatus.NotChecked, "", "", "");

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
            if (profile.IsVideo)
                DrawVideoSection(mode, profile.DisplayName, ffmpegPath, videoBitrateKbps, videoKeyframeInterval, maxPendingReadbacks, videoMaxOutputQueue, logEncoderStderr);
            else
                DrawJpegSection(jpegQuality, maxPendingReadbacks, enableBackpressure, backpressureCooldown, maxEncodedBytes, logBackpressureSkips);

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
            EditorGUILayout.PropertyField(videoBitrateKbps, new GUIContent("Video Bitrate Kbps"));
            EditorGUILayout.PropertyField(videoKeyframeInterval, new GUIContent("Keyframe Interval"));
            EditorGUILayout.PropertyField(maxPendingReadbacks, new GUIContent("Max Pending Readbacks"));
            EditorGUILayout.PropertyField(videoMaxOutputQueue, new GUIContent("Max Output Queue"));
            EditorGUILayout.PropertyField(logEncoderStderr, new GUIContent("Log Encoder Stderr"));
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
                        "FFmpeg was not found at the configured path. Install FFmpeg, restart Unity after PATH changes, or browse to the executable.",
                        MessageType.Warning);
                    break;
                case FfmpegExecutableStatus.Invalid:
                    EditorGUILayout.HelpBox(
                        string.IsNullOrEmpty(_ffmpegCheck.ErrorMessage)
                            ? "Configured FFmpeg did not return a recognizable version."
                            : _ffmpegCheck.ErrorMessage,
                        MessageType.Error);
                    break;
                case FfmpegExecutableStatus.NotChecked:
                default:
                    var label = string.IsNullOrWhiteSpace(configuredPath) ? "system PATH: ffmpeg" : configuredPath;
                    EditorGUILayout.HelpBox("Status: Not Checked (" + label + ")", MessageType.None);
                    break;
            }
        }
    }
}
