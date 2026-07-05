// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Publishers
// Purpose: Diagnostics and backpressure helper methods for FoxgloveCameraPublisher.
using System;
using Foxglove.Schemas;
using Foxglove.Schemas.Video;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Schemas.Camera;
using Unity.FoxgloveSDK.Schemas.Ros2Msg;
using Unity.FoxgloveSDK.Util;
using UnityEngine;
using UnityEngine.Rendering;
using Stopwatch = System.Diagnostics.Stopwatch;
namespace Unity.FoxgloveSDK.Components
{
    public partial class FoxgloveCameraPublisher
    {
        /// <summary>
        /// Reports render, readback, encode, serialization and queue pressure separately
        /// so camera cost can be attributed before future pipeline changes.
        /// </summary>
        private void EmitCameraDiagnosticsIfNeeded()
        {
            _diagnostics.LogCameraIfNeeded(
                _logCameraDiagnostics,
                Time.unscaledTimeAsDouble,
                _cameraDiagnosticsIntervalSeconds,
                _pendingRequests,
                _jpegPublishPipeline?.EncodeQueueDepth ?? 0,
                _jpegPublishPipeline?.CompletedQueueDepth ?? 0,
                out var message);
            if (message != null)
                Debug.Log(message);
        }

        private void EmitCameraSlowStageIfNeeded(
            string stage,
            double elapsedMs,
            int pendingReadbacksBefore,
            int pendingReadbacksAfter)
        {
            if (!_diagnostics.TryBuildCameraSlowStageMessage(
                    _logCameraDiagnostics,
                    _cameraSlowStageThresholdMs,
                    stage,
                    elapsedMs,
                    pendingReadbacksBefore,
                    pendingReadbacksAfter,
                    _jpegPublishPipeline?.EncodeQueueDepth ?? 0,
                    _jpegPublishPipeline?.CompletedQueueDepth ?? 0,
                    out var message))
                return;

            Debug.LogFormat(
                LogType.Log,
                LogOption.NoStacktrace,
                this,
                "{0}",
                message);
        }


        /// <summary>
        /// Optional transport-drop cooldown for legacy behavior; the 138J path relies on
        /// static resource caps rather than frame-time feedback control.
        /// </summary>
        private bool AllowJpegCaptureByBackpressure()
        {
            if (!_enableBackpressureAdaptation)
                return _backpressureGate.AllowCapture(
                    enabled: false,
                    statsSupported: false,
                    totalDroppedDataFrames: 0,
                    currentTimeSec: 0,
                    cooldownSeconds: _backpressureCooldownSeconds,
                    logSkips: _logBackpressureSkips,
                    warning: out _);

            var stats = _manager.GetTransportStatsSnapshot();
            var allowCapture = _backpressureGate.AllowCapture(
                _enableBackpressureAdaptation,
                stats.Supported,
                stats.TotalDroppedDataFrames,
                Time.unscaledTimeAsDouble,
                _backpressureCooldownSeconds,
                _logBackpressureSkips,
                out var warning);
            if (!string.IsNullOrEmpty(warning))
                Debug.LogWarning(warning);
            return allowCapture;
        }


        private void EmitBackpressureWarning(string message)
        {
            if (_backpressureGate.TryRecordSkipWarning(_logBackpressureSkips, message, out var warning))
                Debug.LogWarning(warning);
        }
    }
}
