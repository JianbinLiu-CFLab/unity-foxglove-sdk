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
using UnityEditor;
using UnityEngine;

namespace Unity.FoxgloveSDK.Editor
{
    /// <summary>
    /// Inspector editor for <see cref="FoxgloveCameraPublisher"/> with separate
    /// blocks for JPEG, FFmpeg, OpenH264, and native H.264 options.
    /// </summary>
    [CustomEditor(typeof(FoxgloveCameraPublisher))]
    public partial class FoxgloveCameraPublisherEditor : UnityEditor.Editor
    {
        private const string FfmpegRecoveryHint =
            "Use ... to browse to an existing executable, leave FFmpeg Path empty for system PATH, or open FFmpeg Help... for manual setup and licensing notes.";
        private const string OpenH264Attribution = "OpenH264 Video Codec provided by Cisco Systems, Inc.";
        private bool _showAdvancedJpeg;
        private bool _showDiagnostics;

        private static string[] CameraOutputModeLabels = BuildCameraOutputModeLabels();
        private static readonly Dictionary<string, GUIContent> GuiContentCache =
            new Dictionary<string, GUIContent>(StringComparer.Ordinal);

        private FfmpegExecutableCheckResult _ffmpegCheck =
            new FfmpegExecutableCheckResult(FfmpegExecutableStatus.NotChecked, "", "", "");
        private OpenH264ExecutableCheckResult _openH264Check =
            new OpenH264ExecutableCheckResult(OpenH264ExecutableStatus.NotChecked, "", "", "", "");
        private Task<FfmpegExecutableCheckResult> _ffmpegCheckTask;
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
        private SerializedProperty _maxCaptureRateHz;
        private SerializedProperty _cameraHealthMode;
        private SerializedProperty _useAsyncJpeg;
        private SerializedProperty _maxJpegEncodeQueue;
        private SerializedProperty _maxCompletedJpegQueue;
        private SerializedProperty _maxCompletedJpegPublishesPerFrame;
        private SerializedProperty _maxPixelsPerFrame;
        private SerializedProperty _requireIdleJpegPipeline;
        private SerializedProperty _pipelineCooldownThresholdMs;
        private SerializedProperty _pipelineCooldownMs;
        private SerializedProperty _mainLoopCaptureCooldownThresholdMs;
        private SerializedProperty _mainLoopStableFramesBeforeCapture;
        private SerializedProperty _logCameraDiagnostics;
        private SerializedProperty _cameraDiagnosticsIntervalSeconds;
        private SerializedProperty _cameraSlowStageThresholdMs;
        private SerializedProperty _sensorUnitProfile;
        private SerializedProperty _useSharedSensorClock;
        private SerializedProperty _encodingOverride;
        private SerializedProperty _publishRateSource;
        private SerializedProperty _publishRateHz;
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

        [InitializeOnLoadMethod]
        private static void RegisterStaticCacheReset()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= ResetStaticEditorCaches;
            AssemblyReloadEvents.beforeAssemblyReload += ResetStaticEditorCaches;
            ResetStaticEditorCaches();
        }

        private static void ResetStaticEditorCaches()
        {
            CameraOutputModeLabels = BuildCameraOutputModeLabels();
            GuiContentCache.Clear();
        }

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
            _maxCaptureRateHz = serializedObject.FindProperty("_maxCaptureRateHz");
            _cameraHealthMode = serializedObject.FindProperty("_cameraHealthMode");
            _useAsyncJpeg = serializedObject.FindProperty("_useAsyncJpeg");
            _maxJpegEncodeQueue = serializedObject.FindProperty("_maxJpegEncodeQueue");
            _maxCompletedJpegQueue = serializedObject.FindProperty("_maxCompletedJpegQueue");
            _maxCompletedJpegPublishesPerFrame = serializedObject.FindProperty("_maxCompletedJpegPublishesPerFrame");
            _maxPixelsPerFrame = serializedObject.FindProperty("_maxPixelsPerFrame");
            _requireIdleJpegPipeline = serializedObject.FindProperty("_requireIdleJpegPipeline");
            _pipelineCooldownThresholdMs = serializedObject.FindProperty("_pipelineCooldownThresholdMs");
            _pipelineCooldownMs = serializedObject.FindProperty("_pipelineCooldownMs");
            _mainLoopCaptureCooldownThresholdMs = serializedObject.FindProperty("_mainLoopCaptureCooldownThresholdMs");
            _mainLoopStableFramesBeforeCapture = serializedObject.FindProperty("_mainLoopStableFramesBeforeCapture");
            _logCameraDiagnostics = serializedObject.FindProperty("_logCameraDiagnostics");
            _cameraDiagnosticsIntervalSeconds = serializedObject.FindProperty("_cameraDiagnosticsIntervalSeconds");
            _cameraSlowStageThresholdMs = serializedObject.FindProperty("_cameraSlowStageThresholdMs");
            _sensorUnitProfile = serializedObject.FindProperty("_sensorUnitProfile");
            _useSharedSensorClock = serializedObject.FindProperty("_useSharedSensorClock");
            _encodingOverride = serializedObject.FindProperty("_encodingOverride");
            _publishRateSource = serializedObject.FindProperty("_publishRateSource");
            _publishRateHz = serializedObject.FindProperty("_publishRateHz");
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
            CompleteFfmpegCheckIfReady();
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
                ResetFfmpegCheck();
                ResetOpenH264Check();
            }

            EditorGUILayout.PropertyField(_manager);
            EditorGUILayout.PropertyField(_topic);
            EditorGUILayout.PropertyField(_publishOnEnable, Label("Publish On Enable"));
            EditorGUILayout.PropertyField(_warnIfManagerMissing, Label("Warn If Manager Missing"));
            EditorGUILayout.PropertyField(_frameId, Label("Frame Id"));
            EditorGUILayout.PropertyField(_width);
            EditorGUILayout.PropertyField(_height);
            EditorGUILayout.PropertyField(_maxCaptureRateHz, Label("Max Capture Rate Hz"));
            EditorGUILayout.PropertyField(_cameraHealthMode, Label("Camera Health Mode"));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                "Provider Payload",
                EditorStyles.boldLabel);
            if (_sensorUnitProfile != null)
            {
                EditorGUILayout.PropertyField(
                    _sensorUnitProfile,
                    Label("Sensor Unit Profile"));
            }
            if (_useSharedSensorClock != null)
            {
                EditorGUILayout.PropertyField(
                    _useSharedSensorClock,
                    Label("Use Shared Sensor Clock"));
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
                    _requireIdleJpegPipeline,
                    _pipelineCooldownThresholdMs,
                    _pipelineCooldownMs,
                    _mainLoopCaptureCooldownThresholdMs,
                    _mainLoopStableFramesBeforeCapture,
                    _enableBackpressure,
                    _backpressureCooldown,
                    _maxEncodedBytes,
                    _logBackpressureSkips,
                    _logCameraDiagnostics,
                    _cameraDiagnosticsIntervalSeconds,
                    _cameraSlowStageThresholdMs);
            }

            DrawPublishRateSection();
            DrawEncodingPolicySection();

            serializedObject.ApplyModifiedProperties();

            DrawResolvedSummaries();
        }

        private void OnDisable()
        {
            EditorApplication.update -= CompleteFfmpegCheckIfReady;
            EditorApplication.update -= CompleteOpenH264CheckIfReady;
            _ffmpegCheckTask = null;
            _openH264CheckTask = null;
        }


    }
}
