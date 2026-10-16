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
        private void DrawJpegSection(
            SerializedProperty jpegQuality,
            SerializedProperty maxPendingReadbacks,
            SerializedProperty useAsyncJpeg,
            SerializedProperty maxJpegEncodeQueue,
            SerializedProperty maxCompletedJpegQueue,
            SerializedProperty maxCompletedJpegPublishesPerFrame,
            SerializedProperty maxPixelsPerFrame,
            SerializedProperty requireIdleJpegPipeline,
            SerializedProperty pipelineCooldownThresholdMs,
            SerializedProperty pipelineCooldownMs,
            SerializedProperty mainLoopCaptureCooldownThresholdMs,
            SerializedProperty mainLoopStableFramesBeforeCapture,
            SerializedProperty enableBackpressure,
            SerializedProperty backpressureCooldown,
            SerializedProperty maxEncodedBytes,
            SerializedProperty logBackpressureSkips,
            SerializedProperty logCameraDiagnostics,
            SerializedProperty cameraDiagnosticsIntervalSeconds,
            SerializedProperty cameraSlowStageThresholdMs)
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
                    EditorGUILayout.PropertyField(requireIdleJpegPipeline, Label("Require Idle JPEG Pipeline"));
                    EditorGUILayout.PropertyField(pipelineCooldownThresholdMs, Label("Pipeline Cooldown Threshold Ms"));
                    EditorGUILayout.PropertyField(pipelineCooldownMs, Label("Pipeline Cooldown Ms"));
                    EditorGUILayout.PropertyField(mainLoopCaptureCooldownThresholdMs, Label("Main Loop Cooldown Threshold Ms"));
                    EditorGUILayout.PropertyField(mainLoopStableFramesBeforeCapture, Label("Main Loop Stable Frames"));
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
                        EditorGUILayout.PropertyField(cameraSlowStageThresholdMs, Label("Slow Stage Threshold Ms"));
                    }
                }
            }
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
                using (new EditorGUI.DisabledScope(_ffmpegCheckTask != null))
                {
                    if (GUILayout.Button(_ffmpegCheckTask == null ? "Check FFmpeg" : "Checking..."))
                        checkRequested = true;
                }

                using (new EditorGUI.DisabledScope(!CanRevealFfmpegFolder(revealPath)))
                {
                    if (GUILayout.Button("Reveal Folder"))
                        revealRequested = true;
                }
            }

            if (checkRequested)
                StartFfmpegCheck(ffmpegPath.stringValue);

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

            if (!OpenH264OfficialBinaryInstaller.IsAutomaticInstallSupported)
            {
                EditorGUILayout.HelpBox(
                    "Automatic OpenH264 installation is supported only in the Windows Unity Editor. Use Manual Download and select the helper and DLL paths.",
                    MessageType.Warning);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!OpenH264OfficialBinaryInstaller.IsAutomaticInstallSupported))
                {
                    if (GUILayout.Button("Install OpenH264 Runtime..."))
                        installRequested = true;
                }

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
                PublisherEncodingEditorLabels.DrawEffectiveEncoding(resolution.Effective, "Effective Encoding");
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
            {
                EditorGUILayout.HelpBox(
                    "Camera output mode is outside the supported enum range. Update the SDK Inspector labels before editing this value.",
                    MessageType.Error);
                EditorGUILayout.LabelField("Camera Output Mode", "Unsupported value: " + currentIndex);
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

    }
}
