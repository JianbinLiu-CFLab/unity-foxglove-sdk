// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Publishers
// Purpose: Dedicated Inspector for the unified camera publisher.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Foxglove.Schemas.Video;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Ros2Bridge;
using UnityEditor;
using UnityEngine;

namespace Unity.FoxgloveSDK.Editor
{
    /// <summary>
    /// Inspector editor for <see cref="FoxgloveCameraPublisher"/> with separate
    /// blocks for JPEG, FFmpeg, OpenH264, and native H.264 options.
    /// </summary>
    [CustomEditor(typeof(FoxgloveCameraPublisher))]
    public class FoxgloveCameraPublisherEditor : UnityEditor.Editor
    {
        private const string FfmpegRecoveryHint =
            "Use ... to browse to an existing executable, leave FFmpeg Path empty for system PATH, or open FFmpeg Help... for manual setup and licensing notes.";
        private const string OpenH264Attribution = "OpenH264 Video Codec provided by Cisco Systems, Inc.";
        private static bool _showRos2Outputs;
        private static bool _showAdvancedJpeg;
        private static bool _showDiagnostics;

        private static readonly string[] CameraOutputModeLabels = BuildCameraOutputModeLabels();
        private static readonly Dictionary<string, GUIContent> GuiContentCache =
            new Dictionary<string, GUIContent>(StringComparer.Ordinal);

        private FfmpegExecutableCheckResult _ffmpegCheck =
            new FfmpegExecutableCheckResult(FfmpegExecutableStatus.NotChecked, "", "", "");
        private OpenH264ExecutableCheckResult _openH264Check =
            new OpenH264ExecutableCheckResult(OpenH264ExecutableStatus.NotChecked, "", "", "", "");
        private Task<OpenH264ExecutableCheckResult> _openH264CheckTask;
        private SerializedProperty _script;
        private SerializedProperty _manager;
        private SerializedProperty _topic;
        private SerializedProperty _outputMode;
        private SerializedProperty _publishOnEnable;
        private SerializedProperty _warnIfManagerMissing;
        private SerializedProperty _frameId;
        private SerializedProperty _width;
        private SerializedProperty _height;
        private SerializedProperty _jpegQuality;
        private SerializedProperty _maxPendingReadbacks;
        private SerializedProperty _useAsyncJpeg;
        private SerializedProperty _maxJpegEncodeQueue;
        private SerializedProperty _maxCompletedJpegQueue;
        private SerializedProperty _maxCompletedJpegPublishesPerFrame;
        private SerializedProperty _maxPixelsPerFrame;
        private SerializedProperty _logCameraDiagnostics;
        private SerializedProperty _cameraDiagnosticsIntervalSeconds;
        private SerializedProperty _sensorUnitProfile;
        private SerializedProperty _useSharedSensorClock;
        private SerializedProperty _publishStandardRos2CompressedImage;
        private SerializedProperty _publishStandardRos2RawImage;
        private SerializedProperty _sensorCameraRawImageTopic;
        private SerializedProperty _encodingOverride;
        private SerializedProperty _publishRateSource;
        private SerializedProperty _publishRateHz;
        private SerializedProperty _bridgeOutput;
        private SerializedProperty _bridgeTopicOverride;
        private SerializedProperty _ffmpegPath;
        private SerializedProperty _openH264HelperPath;
        private SerializedProperty _openH264DllPath;
        private SerializedProperty _openH264MaxInputQueue;
        private SerializedProperty _videoBitrateKbps;
        private SerializedProperty _videoKeyframeInterval;
        private SerializedProperty _videoMaxOutputQueue;
        private SerializedProperty _logVideoDiagnostics;
        private SerializedProperty _logEncoderStderr;
        private SerializedProperty _enableBackpressure;
        private SerializedProperty _backpressureCooldown;
        private SerializedProperty _maxEncodedBytes;
        private SerializedProperty _logBackpressureSkips;

        private void OnEnable()
        {
            _script = serializedObject.FindProperty("m_Script");
            _manager = serializedObject.FindProperty("_manager");
            _topic = serializedObject.FindProperty("_topic");
            _outputMode = serializedObject.FindProperty("_outputMode");
            _publishOnEnable = serializedObject.FindProperty("_publishOnEnable");
            _warnIfManagerMissing = serializedObject.FindProperty("_warnIfManagerMissing");
            _frameId = serializedObject.FindProperty("_frameId");
            _width = serializedObject.FindProperty("_width");
            _height = serializedObject.FindProperty("_height");
            _jpegQuality = serializedObject.FindProperty("_jpegQuality");
            _maxPendingReadbacks = serializedObject.FindProperty("_maxPendingReadbacks");
            _useAsyncJpeg = serializedObject.FindProperty("_useAsyncJpeg");
            _maxJpegEncodeQueue = serializedObject.FindProperty("_maxJpegEncodeQueue");
            _maxCompletedJpegQueue = serializedObject.FindProperty("_maxCompletedJpegQueue");
            _maxCompletedJpegPublishesPerFrame = serializedObject.FindProperty("_maxCompletedJpegPublishesPerFrame");
            _maxPixelsPerFrame = serializedObject.FindProperty("_maxPixelsPerFrame");
            _logCameraDiagnostics = serializedObject.FindProperty("_logCameraDiagnostics");
            _cameraDiagnosticsIntervalSeconds = serializedObject.FindProperty("_cameraDiagnosticsIntervalSeconds");
            _sensorUnitProfile = serializedObject.FindProperty("_sensorUnitProfile");
            _useSharedSensorClock = serializedObject.FindProperty("_useSharedSensorClock");
            _publishStandardRos2CompressedImage = serializedObject.FindProperty("_publishStandardRos2CompressedImage");
            _publishStandardRos2RawImage = serializedObject.FindProperty("_publishStandardRos2RawImage");
            _sensorCameraRawImageTopic = serializedObject.FindProperty("_sensorCameraRawImageTopic");
            _encodingOverride = serializedObject.FindProperty("_encodingOverride");
            _publishRateSource = serializedObject.FindProperty("_publishRateSource");
            _publishRateHz = serializedObject.FindProperty("_publishRateHz");
            _bridgeOutput = serializedObject.FindProperty("_ros2BridgeOutput");
            _bridgeTopicOverride = serializedObject.FindProperty("_ros2BridgeTopicOverride");
            _ffmpegPath = serializedObject.FindProperty("_ffmpegPath");
            _openH264HelperPath = serializedObject.FindProperty("_openH264HelperPath");
            _openH264DllPath = serializedObject.FindProperty("_openH264DllPath");
            _openH264MaxInputQueue = serializedObject.FindProperty("_openH264MaxInputQueue");
            _videoBitrateKbps = serializedObject.FindProperty("_videoBitrateKbps");
            _videoKeyframeInterval = serializedObject.FindProperty("_videoKeyframeInterval");
            _videoMaxOutputQueue = serializedObject.FindProperty("_videoMaxOutputQueue");
            _logVideoDiagnostics = serializedObject.FindProperty("_logVideoDiagnostics");
            _logEncoderStderr = serializedObject.FindProperty("_logEncoderStderr");
            _enableBackpressure = serializedObject.FindProperty("_enableBackpressureAdaptation");
            _backpressureCooldown = serializedObject.FindProperty("_backpressureCooldownSeconds");
            _maxEncodedBytes = serializedObject.FindProperty("_maxEncodedBytes");
            _logBackpressureSkips = serializedObject.FindProperty("_logBackpressureSkips");
        }

        public override void OnInspectorGUI()
        {
            CompleteOpenH264CheckIfReady();
            serializedObject.Update();

            EditorGUILayout.LabelField("Camera Output", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(_script);
            }

            var oldMode = GetMode(_outputMode);
            EditorGUI.BeginChangeCheck();
            DrawCameraOutputMode(_outputMode);
            if (EditorGUI.EndChangeCheck())
            {
                var newMode = GetMode(_outputMode);
                ApplyTopicForModeChange(_topic, oldMode, newMode);
                _ffmpegCheck = new FfmpegExecutableCheckResult(FfmpegExecutableStatus.NotChecked, "", "", "");
                _openH264Check = new OpenH264ExecutableCheckResult(OpenH264ExecutableStatus.NotChecked, "", "", "", "");
            }

            EditorGUILayout.PropertyField(_manager);
            EditorGUILayout.PropertyField(_topic);
            EditorGUILayout.PropertyField(_publishOnEnable, Label("Publish On Enable"));
            EditorGUILayout.PropertyField(_warnIfManagerMissing, Label("Warn If Manager Missing"));
            EditorGUILayout.PropertyField(_frameId, Label("Frame Id"));
            EditorGUILayout.PropertyField(_width);
            EditorGUILayout.PropertyField(_height);

            if (IsRos2CameraUiRelevant(
                _manager,
                _encodingOverride,
                _publishStandardRos2CompressedImage,
                _publishStandardRos2RawImage))
            {
                DrawRos2OutputsSection(
                    _sensorUnitProfile,
                    _useSharedSensorClock,
                    _publishStandardRos2CompressedImage,
                    _publishStandardRos2RawImage,
                    _sensorCameraRawImageTopic);
            }

            var mode = GetMode(_outputMode);
            var profile = CameraVideoOutputProfile.ForMode(mode);
            if (mode == CameraOutputMode.H264OpenH264)
            {
                DrawOpenH264VideoSection(
                    profile.DisplayName,
                    _openH264HelperPath,
                    _openH264DllPath,
                    _videoBitrateKbps,
                    _videoKeyframeInterval,
                    _maxPendingReadbacks,
                    _openH264MaxInputQueue,
                    _videoMaxOutputQueue,
                    _logVideoDiagnostics,
                    _logEncoderStderr);
            }
            else if (mode == CameraOutputMode.H264MediaFoundationExperimental)
            {
                DrawNativeH264Section(
                    profile.DisplayName,
                    _videoBitrateKbps,
                    _videoKeyframeInterval,
                    _maxPendingReadbacks,
                    _videoMaxOutputQueue,
                    _logVideoDiagnostics,
                    _logEncoderStderr);
            }
            else if (profile.IsVideo)
            {
                DrawVideoSection(mode, profile.DisplayName, _ffmpegPath, _videoBitrateKbps, _videoKeyframeInterval, _maxPendingReadbacks, _videoMaxOutputQueue, _logVideoDiagnostics, _logEncoderStderr);
            }
            else
            {
                DrawJpegSection(
                    _jpegQuality,
                    _maxPendingReadbacks,
                    _useAsyncJpeg,
                    _maxJpegEncodeQueue,
                    _maxCompletedJpegQueue,
                    _maxCompletedJpegPublishesPerFrame,
                    _maxPixelsPerFrame,
                    _enableBackpressure,
                    _backpressureCooldown,
                    _maxEncodedBytes,
                    _logBackpressureSkips,
                    _logCameraDiagnostics,
                    _cameraDiagnosticsIntervalSeconds);
            }

            DrawPublishRateSection();
            DrawEncodingPolicySection();
            if (IsRos2BridgeUiRelevant())
                DrawRos2BridgeSection();

            serializedObject.ApplyModifiedProperties();

            DrawResolvedSummaries();
        }

        private void OnDisable()
        {
            EditorApplication.update -= CompleteOpenH264CheckIfReady;
        }

        private static GUIContent Label(string text)
        {
            if (!GuiContentCache.TryGetValue(text, out var content))
            {
                content = new GUIContent(text);
                GuiContentCache.Add(text, content);
            }

            return content;
        }

        private void DrawJpegSection(
            SerializedProperty jpegQuality,
            SerializedProperty maxPendingReadbacks,
            SerializedProperty useAsyncJpeg,
            SerializedProperty maxJpegEncodeQueue,
            SerializedProperty maxCompletedJpegQueue,
            SerializedProperty maxCompletedJpegPublishesPerFrame,
            SerializedProperty maxPixelsPerFrame,
            SerializedProperty enableBackpressure,
            SerializedProperty backpressureCooldown,
            SerializedProperty maxEncodedBytes,
            SerializedProperty logBackpressureSkips,
            SerializedProperty logCameraDiagnostics,
            SerializedProperty cameraDiagnosticsIntervalSeconds)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("JPEG", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(jpegQuality, Label("JPEG Quality"));
            EditorGUILayout.PropertyField(maxPendingReadbacks, Label("Max Pending Readbacks"));

            _showAdvancedJpeg = EditorGUILayout.Foldout(_showAdvancedJpeg, "Advanced JPEG", true);
            if (_showAdvancedJpeg)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.PropertyField(useAsyncJpeg, Label("Use Async JPEG"));
                    using (new EditorGUI.DisabledScope(!useAsyncJpeg.boolValue))
                    {
                        EditorGUILayout.PropertyField(maxJpegEncodeQueue, Label("Max Encode Queue"));
                        EditorGUILayout.PropertyField(maxCompletedJpegQueue, Label("Max Completed Queue"));
                        EditorGUILayout.PropertyField(maxCompletedJpegPublishesPerFrame, Label("Max Completed Publishes / Frame"));
                    }

                    EditorGUILayout.PropertyField(maxPixelsPerFrame, Label("Max Pixels / Frame"));
                    EditorGUILayout.PropertyField(enableBackpressure, Label("Enable Backpressure Adaptation"));
                    using (new EditorGUI.DisabledScope(!enableBackpressure.boolValue))
                    {
                        EditorGUILayout.PropertyField(backpressureCooldown, Label("Backpressure Cooldown"));
                        EditorGUILayout.PropertyField(maxEncodedBytes, Label("Max Encoded Bytes"));
                        EditorGUILayout.PropertyField(logBackpressureSkips, Label("Log Backpressure Skips"));
                    }
                }
            }

            _showDiagnostics = EditorGUILayout.Foldout(_showDiagnostics, "Diagnostics", true);
            if (_showDiagnostics)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.PropertyField(logCameraDiagnostics, Label("Log Camera Diagnostics"));
                    using (new EditorGUI.DisabledScope(!logCameraDiagnostics.boolValue))
                    {
                        EditorGUILayout.PropertyField(cameraDiagnosticsIntervalSeconds, Label("Diagnostics Interval"));
                    }
                }
            }
        }

        private static void DrawRos2OutputsSection(
            SerializedProperty sensorUnitProfile,
            SerializedProperty useSharedSensorClock,
            SerializedProperty publishStandardRos2CompressedImage,
            SerializedProperty publishStandardRos2RawImage,
            SerializedProperty sensorCameraRawImageTopic)
        {
            EditorGUILayout.Space();
            _showRos2Outputs = EditorGUILayout.Foldout(_showRos2Outputs, "ROS2 Outputs", true);
            if (!_showRos2Outputs)
                return;

            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(sensorUnitProfile, Label("Sensor Unit Profile"));
                EditorGUILayout.PropertyField(useSharedSensorClock, Label("Use Shared Sensor Clock"));
                EditorGUILayout.PropertyField(
                    publishStandardRos2CompressedImage,
                    Label("Publish CompressedImage DDS"));
                EditorGUILayout.PropertyField(
                    publishStandardRos2RawImage,
                    Label("Publish Raw Image DDS"));
                if (publishStandardRos2RawImage.boolValue)
                {
                    EditorGUILayout.PropertyField(
                        sensorCameraRawImageTopic,
                        Label("Raw Image Topic"));
                }

                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.Toggle(Label("Publish CameraInfo DDS"), false);
                }

                EditorGUILayout.HelpBox(
                    "CameraInfo DDS is currently provided by the standalone CameraInfo publisher. Keep it paired with this camera when ROS2 tools need intrinsics.",
                    MessageType.None);
            }
        }

        private static bool IsRos2CameraUiRelevant(
            SerializedProperty manager,
            SerializedProperty encodingOverride,
            SerializedProperty publishStandardRos2CompressedImage,
            SerializedProperty publishStandardRos2RawImage)
        {
            if (publishStandardRos2CompressedImage != null && publishStandardRos2CompressedImage.boolValue)
                return true;
            if (publishStandardRos2RawImage != null && publishStandardRos2RawImage.boolValue)
                return true;
            if (encodingOverride != null
                && encodingOverride.enumValueIndex == (int)PublisherEncodingOverride.Ros2)
                return true;

            if (manager?.objectReferenceValue is FoxgloveManager configuredManager)
                return configuredManager.Ros2NativeEnabled || configuredManager.DefaultPublisherEncoding == GlobalEncoding.Ros2;

            return false;
        }

        /// <summary>
        /// Draws FFmpeg-backed H.264/H.265 settings and diagnostics toggles.
        /// </summary>
        private void DrawVideoSection(
            CameraOutputMode mode,
            string title,
            SerializedProperty ffmpegPath,
            SerializedProperty videoBitrateKbps,
            SerializedProperty videoKeyframeInterval,
            SerializedProperty maxPendingReadbacks,
            SerializedProperty videoMaxOutputQueue,
            SerializedProperty logVideoDiagnostics,
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

            EditorGUILayout.PropertyField(videoBitrateKbps, Label("Video Bitrate Kbps"));
            EditorGUILayout.PropertyField(videoKeyframeInterval, Label("Keyframe Interval"));
            EditorGUILayout.PropertyField(maxPendingReadbacks, Label("Max Pending Readbacks"));
            EditorGUILayout.PropertyField(videoMaxOutputQueue, Label("Max Output Queue"));
            EditorGUILayout.PropertyField(logVideoDiagnostics, Label("Log Video Diagnostics"));
            EditorGUILayout.PropertyField(logEncoderStderr, Label("Log Encoder Stderr"));
        }

        /// <summary>
        /// Draws OpenH264-specific video options and path/validation helpers.
        /// </summary>
        private void DrawOpenH264VideoSection(
            string title,
            SerializedProperty openH264HelperPath,
            SerializedProperty openH264DllPath,
            SerializedProperty videoBitrateKbps,
            SerializedProperty videoKeyframeInterval,
            SerializedProperty maxPendingReadbacks,
            SerializedProperty openH264MaxInputQueue,
            SerializedProperty videoMaxOutputQueue,
            SerializedProperty logVideoDiagnostics,
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
                ResetOpenH264Check);

            DrawOpenH264PathField(
                "OpenH264 DLL",
                openH264DllPath,
                "Select OpenH264 DLL",
                Application.platform == RuntimePlatform.WindowsEditor ? "dll" : "",
                ResetOpenH264Check);

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
                StartOpenH264Check(openH264HelperPath.stringValue, openH264DllPath.stringValue);

            if (revealRequested)
                RevealFolder(revealPath);

            if (installRequested)
            {
                OpenH264InstallWindow.ShowWindow((installedHelperPath, installedDllPath) =>
                {
                    if (this == null || serializedObject == null || serializedObject.targetObject == null)
                        return;

                    serializedObject.Update();
                    openH264HelperPath.stringValue = installedHelperPath;
                    openH264DllPath.stringValue = installedDllPath;
                    serializedObject.ApplyModifiedProperties();
                    StartOpenH264Check(installedHelperPath, installedDllPath);
                    Repaint();
                });
            }

            if (licenseRequested)
                Application.OpenURL(OpenH264OfficialBinaryManifest.BinaryLicenseUrl);

            DrawOpenH264Status(openH264HelperPath.stringValue, openH264DllPath.stringValue);
            EditorGUILayout.HelpBox(OpenH264Attribution, MessageType.None);

            EditorGUILayout.PropertyField(videoBitrateKbps, Label("Video Bitrate Kbps"));
            EditorGUILayout.PropertyField(videoKeyframeInterval, Label("Keyframe Interval"));
            EditorGUILayout.PropertyField(maxPendingReadbacks, Label("Max Pending Readbacks"));
            EditorGUILayout.PropertyField(openH264MaxInputQueue, Label("Max Input Queue"));
            EditorGUILayout.PropertyField(videoMaxOutputQueue, Label("Max Output Queue"));
            EditorGUILayout.PropertyField(logVideoDiagnostics, Label("Log Video Diagnostics"));
            EditorGUILayout.PropertyField(logEncoderStderr, Label("Log Encoder Diagnostics"));
        }

        /// <summary>
        /// Draws Windows Media Foundation H.264 settings and warning guidance.
        /// </summary>
        private static void DrawNativeH264Section(
            string title,
            SerializedProperty videoBitrateKbps,
            SerializedProperty videoKeyframeInterval,
            SerializedProperty maxPendingReadbacks,
            SerializedProperty videoMaxOutputQueue,
            SerializedProperty logVideoDiagnostics,
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

            EditorGUILayout.PropertyField(videoBitrateKbps, Label("Video Bitrate Kbps"));
            EditorGUILayout.PropertyField(videoKeyframeInterval, Label("Keyframe Interval"));
            EditorGUILayout.PropertyField(maxPendingReadbacks, Label("Max Pending Readbacks"));
            EditorGUILayout.PropertyField(videoMaxOutputQueue, Label("Max Output Queue"));
            EditorGUILayout.PropertyField(logVideoDiagnostics, Label("Log Video Diagnostics"));
            EditorGUILayout.PropertyField(logEncoderStderr, Label("Log Encoder Diagnostics"));
        }

        private void DrawPublishRateSection()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Publish Rate", EditorStyles.boldLabel);
            if (_publishRateSource != null)
                EditorGUILayout.PropertyField(_publishRateSource, Label("Publish Rate Source"));

            var usesLocalRate = _publishRateSource == null
                                || _publishRateSource.enumValueIndex == (int)PublisherRateSource.OverrideLocal;
            using (new EditorGUI.DisabledScope(!usesLocalRate))
            {
                if (_publishRateHz != null)
                    EditorGUILayout.PropertyField(_publishRateHz, Label("Publish Rate Hz"));
            }
        }

        private void DrawEncodingPolicySection()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Encoding Policy", EditorStyles.boldLabel);
            PublisherEncodingEditorLabels.DrawPublisherOverride(_encodingOverride, "Encoding Override");
        }

        private void DrawRos2BridgeSection()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("ROS2 Bridge", EditorStyles.boldLabel);
            PublisherEncodingEditorLabels.DrawRos2BridgeOverride(_bridgeOutput, "Bridge Output");
            if (_bridgeTopicOverride != null)
                EditorGUILayout.PropertyField(_bridgeTopicOverride, Label("Bridge Topic Override"));
            EditorGUILayout.HelpBox(
                "JPEG mode can mirror the same ROS2 CDR image payload to the optional local bridge. Video modes keep using WebSocket output only.",
                MessageType.Info);
        }

        private bool IsRos2BridgeUiRelevant()
        {
            var publisher = (FoxgloveCameraPublisher)target;
            if (publisher.BridgeOutputResolution.IsEnabled)
                return true;
            if (publisher.ConfiguredManager != null && publisher.ConfiguredManager.Ros2BridgeEnabled)
                return true;

            return _bridgeOutput != null
                   && _bridgeOutput.enumValueIndex == (int)Ros2BridgeOutputOverride.Enabled;
        }

        private void DrawResolvedSummaries()
        {
            var publisher = (FoxgloveCameraPublisher)target;
            var resolution = publisher.EncodingResolution;
            var bridgeResolution = publisher.BridgeOutputResolution;

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.FloatField("Effective Publish Rate Hz", publisher.EffectivePublishRateHz);
            }

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField("Supported Encodings", publisher.SupportedEncodingSummary);
                PublisherEncodingEditorLabels.DrawEffectiveEncoding(resolution.Effective, "Effective Encoding");
                PublisherEncodingEditorLabels.DrawEffectiveRos2BridgeOutput(bridgeResolution.Effective, "Effective ROS2 Bridge");
                EditorGUILayout.TextField("Effective Bridge Topic", publisher.EffectiveRos2BridgeTopic);
                EditorGUILayout.TextField("Effective Bridge QoS", publisher.EffectiveRos2BridgeQos.DisplaySummary);
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

            if (bridgeResolution.FellBack)
            {
                EditorGUILayout.HelpBox(
                    "Requested ROS2 Bridge output, but this camera mode cannot mirror a ROS2 payload.",
                    MessageType.Warning);
            }
        }

        private static void DrawCameraOutputMode(SerializedProperty outputMode)
        {
            if (outputMode == null)
                return;

            var currentIndex = outputMode.enumValueIndex;
            if (currentIndex < 0 || currentIndex >= CameraOutputModeLabels.Length)
            {
                EditorGUILayout.HelpBox(
                    "Camera output mode is outside the supported enum range. Update the SDK Inspector labels before editing this value.",
                    MessageType.Error);
                EditorGUILayout.Popup("Camera Output Mode", 0, CameraOutputModeLabels);
                return;
            }

            outputMode.enumValueIndex = EditorGUILayout.Popup("Camera Output Mode", currentIndex, CameraOutputModeLabels);
        }

        private static string[] BuildCameraOutputModeLabels()
        {
            var values = (CameraOutputMode[])Enum.GetValues(typeof(CameraOutputMode));
            var labels = new string[values.Length];
            for (var i = 0; i < values.Length; i++)
                labels[i] = CameraVideoOutputProfile.ForMode(values[i]).DisplayName;
            return labels;
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
                    ScheduleBrowsePath(
                        ffmpegPath,
                        "Select FFmpeg Executable",
                        Application.platform == RuntimePlatform.WindowsEditor ? "exe" : "",
                        () => _ffmpegCheck = new FfmpegExecutableCheckResult(FfmpegExecutableStatus.NotChecked, "", "", ""));
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
                if (capturedProperty.serializedObject == null || capturedProperty.serializedObject.targetObject == null)
                    return;

                var selected = EditorUtility.OpenFilePanel(dialogTitle, defaultDir, extension);
                if (string.IsNullOrEmpty(selected))
                    return;

                capturedProperty.serializedObject.Update();
                capturedProperty.stringValue = selected;
                capturedProperty.serializedObject.ApplyModifiedProperties();
                onChanged?.Invoke();
            };
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

        private sealed class OpenH264InstallWindow : EditorWindow
        {
            private Action<string, string> _onInstalled;
            private Vector2 _scroll;
            private string _installRoot;
            private string _statusMessage;
            private MessageType _statusType;
            private Task<OpenH264InstallResult> _installTask;
            private string _installRootInFlight;

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

                    using (new EditorGUI.DisabledScope(IsInstalling))
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

                OpenH264InstallResult result;
                try
                {
                    result = task.GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    _statusMessage = ex.Message;
                    _statusType = MessageType.Error;
                    Repaint();
                    return;
                }

                if (result.Success)
                {
                    OpenH264InstallLocation.SavePreferredInstallRoot(_installRootInFlight);
                    _onInstalled?.Invoke(result.HelperPath, result.DllPath);
                    _statusMessage = "Installed OpenH264 runtime:\nHelper: " + result.HelperPath + "\nDLL: " + result.DllPath;
                    _statusType = MessageType.Info;
                    Close();
                    return;
                }

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
