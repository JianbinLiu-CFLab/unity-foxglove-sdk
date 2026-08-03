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
                Debug.LogFormat(
                    LogType.Log,
                    LogOption.NoStacktrace,
                    this,
                    "{0}",
                    message);
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

        private bool AllowCameraCaptureBySourceRate(ulong unixNs)
        {
            var rateHz = _maxCaptureRateHz;
            if (rateHz <= 0f || float.IsNaN(rateHz) || float.IsInfinity(rateHz))
                return true;

            var intervalNs = ResolveMaxCaptureIntervalNs(rateHz);
            var timestampNs = unixNs == 0UL ? FoxgloveTimeUtil.NowUnixTimeNs() : unixNs;
            if (CameraCaptureRateGate.ShouldCapture(ref _lastSourceCaptureUnixNs, timestampNs, intervalNs))
                return true;

            _diagnostics.RecordRateSkip();
            return false;
        }

        private ulong ResolveMaxCaptureIntervalNs(float rateHz)
        {
            if (!rateHz.Equals(_cachedMaxCaptureRateHz))
            {
                _cachedMaxCaptureRateHz = rateHz;
                _cachedMaxCaptureIntervalNs = CameraCaptureRateGate.ResolveIntervalNs(rateHz);
            }

            return _cachedMaxCaptureIntervalNs;
        }

        private bool AllowCameraCaptureByHealthPolicy(CameraVideoOutputProfile profile)
        {
            EnsureJpegPublishPipeline();
            if (profile.IsVideo)
                EnsureVideoPublishPipeline();

            WarnIfCameraHealthLimitsInvalid(profile);

            var videoPipeline = _videoPublishPipeline;
            var result = CameraPipelineHealthPolicy.Evaluate(new CameraPipelineHealthInput
            {
                Mode = _cameraHealthMode,
                PendingReadbacks = _pendingRequests,
                MaxPendingReadbacks = Math.Max(1, _maxPendingReadbacks),
                EncodeQueueDepth = profile.IsVideo ? 0 : _jpegPublishPipeline.EncodeQueueDepth,
                MaxEncodeQueueDepth = profile.IsVideo ? 1 : _maxJpegEncodeQueue,
                CompletedQueueDepth = profile.IsVideo ? 0 : _jpegPublishPipeline.CompletedQueueDepth,
                MaxCompletedQueueDepth = profile.IsVideo ? 1 : _maxCompletedJpegQueue,
                VideoOutputQueueDepth = profile.IsVideo ? videoPipeline?.OutputQueueDepth ?? 0 : 0,
                MaxVideoOutputQueueDepth = profile.IsVideo ? videoPipeline?.MaxOutputQueue ?? 1 : 1,
                Width = _width,
                Height = _height,
                MaxPixelsPerFrame = _maxPixelsPerFrame,
                RenderPressureCooldownActive = PipelineCooldownActive()
            });
            if (result.AllowCapture)
                return true;

            _diagnostics.RecordHealthSkip(result.SkipReason);
            return false;
        }

        private void WarnIfCameraHealthLimitsInvalid(CameraVideoOutputProfile profile)
        {
            var invalid = _maxPendingReadbacks <= 0
                          || (!profile.IsVideo && (_maxJpegEncodeQueue <= 0 || _maxCompletedJpegQueue <= 0))
                          || (profile.IsVideo && _videoMaxOutputQueue <= 0);
            if (!invalid)
            {
                _cameraHealthLimitWarningIssued = false;
                return;
            }

            if (_cameraHealthLimitWarningIssued)
                return;

            _cameraHealthLimitWarningIssued = true;
            Debug.LogWarning(
                "[Foxglove] Camera health queue limits must be positive; using minimum value 1 for capture admission.");
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
