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
        /// <remarks>Phase192 source sections: InspectorLifecycle.cs, DrawSections.cs, BrowsePaths.cs, StatusChecks.cs, InstallWindows.cs</remarks>
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

    }
}
