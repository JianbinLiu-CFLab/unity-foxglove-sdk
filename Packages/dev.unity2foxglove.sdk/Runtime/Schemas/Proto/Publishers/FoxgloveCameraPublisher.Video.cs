// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Publishers
// Purpose: Video helper methods for FoxgloveCameraPublisher.
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
        private CameraVideoPublishPipeline EnsureVideoPublishPipeline()
        {
            if (_videoPublishPipeline == null)
                _videoPublishPipeline = new CameraVideoPublishPipeline(_diagnostics, Debug.LogWarning);
            return _videoPublishPipeline;
        }


        /// <summary>
        /// Submits a rendered camera frame to the active video sidecar using the
        /// dimensions captured with the same readback request.
        /// </summary>
        private void SubmitVideoFrame(AsyncGPUReadbackRequest req, ulong renderUnixNs, int captureWidth, int captureHeight)
        {
            var readbackData = req.GetData<byte>();
            EnsureVideoPublishPipeline();
            var result = _videoPublishPipeline.SubmitVideoFrame(
                () => readbackData.ToArray(),
                readbackData.Length,
                renderUnixNs,
                captureWidth,
                captureHeight);

            if (result.Submitted)
            {
                DrainEncodedAccessUnits();
                return;
            }

            switch (result.Outcome)
            {
                case CameraVideoSubmitOutcome.DimensionMismatch:
                    RecordVideoDimensionMismatchDrop(result.Reason);
                    break;
                case CameraVideoSubmitOutcome.FrameDataMissing:
                    EmitVideoDiagnosticsIfNeeded();
                    LogVideoEncoderUnavailable(ActiveProfile, result.Reason);
                    break;
                default:
                    EmitVideoDiagnosticsIfNeeded();
                    LogVideoEncoderUnavailable(ActiveProfile, result.Reason);
                    break;
            }
        }

        /// <summary>
        /// Starts explicit video modes only; video setup failure never falls through into
        /// extra JPEG work during the same publish tick.
        /// </summary>
        private bool EnsureVideoSidecarStarted(CameraVideoOutputProfile profile)
        {
            if (!profile.IsVideo)
                return false;

            EnsureVideoPublishPipeline();
            if (_videoPublishPipeline.EnsureVideoSidecarStarted(
                profile,
                CameraVideoSidecarConfigFactory.Create(
                    _ffmpegPath,
                    _openH264HelperPath,
                    _openH264DllPath,
                    _width,
                    _height,
                    EffectivePublishRateHz,
                    _videoBitrateKbps,
                    _videoKeyframeInterval,
                    Math.Max(1, _maxPendingReadbacks),
                    _openH264MaxInputQueue,
                    _videoMaxOutputQueue),
                DrainEncodedAccessUnits,
                out var error))
            {
                _diagnostics.ResetVideoDimensionMismatchWarning();
                return true;
            }

            LogVideoEncoderUnavailable(profile, error);
            return false;
        }


        private void DrainEncodedAccessUnits()
        {
            EnsureVideoPublishPipeline();
            if (!_videoPublishPipeline.TryDrainEncodedAccessUnits(
                () => CurrentLogTimeNs,
                PublishVideoAccessUnit,
                sidecar => LogEncoderStderrIfNeeded(sidecar),
                out var elapsedMs))
            {
                return;
            }

            _diagnostics.RecordVideoDrainMs(elapsedMs);
            EmitVideoDiagnosticsIfNeeded();
        }

        private void PublishVideoAccessUnit(byte[] accessUnit, ulong unixNs, string videoFormat)
        {
            if (accessUnit == null || accessUnit.Length == 0)
                return;

            if (unixNs == 0UL)
                unixNs = CurrentLogTimeNs;
            var payload = CameraCompressedVideoBuilder.Serialize(
                unixNs,
                ResolveFrameId(),
                accessUnit,
                videoFormat);
            PublishProto(payload, unixNs);
            _diagnostics.RecordVideoAccessUnitPublished(accessUnit.Length);
        }

        private void StopVideoSidecar()
        {
            EnsureVideoPublishPipeline();
            _videoPublishPipeline.StopVideoSidecar(DrainEncodedAccessUnits);
        }

        /// <summary>
        /// Keeps the running sidecar aligned with the locked mode and requested
        /// dimensions, debouncing restarts while Inspector edits settle.
        /// </summary>
        private bool EnsureSidecarMatchesMode(CameraVideoOutputProfile profile)
        {
            EnsureVideoPublishPipeline();
            var result = _videoPublishPipeline.EnsureSidecarMatchesMode(
                profile,
                DesiredVideoWidth,
                DesiredVideoHeight,
                Time.unscaledTimeAsDouble,
                DrainEncodedAccessUnits);
            if (result.ResetEncoderWarning)
                _videoPublishPipeline.ResetVideoEncoderWarning();

            if (!string.IsNullOrEmpty(result.Diagnostic))
                _diagnostics.RecordVideoDiagnostic(result.Diagnostic);

            if (result.DroppedWhilePending)
            {
                _diagnostics.RecordVideoDimensionMismatchDrop(result.Diagnostic, warnOnce: false);
                EmitVideoDiagnosticsIfNeeded();
                return false;
            }

            if (result.Restarted)
            {
                _diagnostics.RecordVideoSidecarRestart();
                EmitVideoDiagnosticsIfNeeded();
            }

            return result.AllowCapture;
        }


        /// <summary>
        /// Drops one stale or mismatched video frame and records the reason for diagnostics.
        /// </summary>
        private void RecordVideoDimensionMismatchDrop(string reason)
        {
            if (_diagnostics.RecordVideoDimensionMismatchDrop(reason, warnOnce: true))
                Debug.LogWarning("[Foxglove] Camera video frame dropped: " + reason);

            EmitVideoDiagnosticsIfNeeded();
        }

        /// <summary>
        /// Reports video submission and drain evidence separately from JPEG diagnostics.
        /// </summary>
        private void EmitVideoDiagnosticsIfNeeded()
        {
            EnsureVideoPublishPipeline();
            var profile = CameraVideoOutputProfile.ForMode(_videoPublishPipeline.Mode);
            _diagnostics.LogVideoIfNeeded(
                _logVideoDiagnostics,
                Time.unscaledTimeAsDouble,
                _cameraDiagnosticsIntervalSeconds,
                profile.DisplayName,
                _videoPublishPipeline.SidecarWidth,
                _videoPublishPipeline.SidecarHeight,
                _pendingRequests,
                out var message);
            if (message != null)
                Debug.Log(message);
        }

        /// <summary>
        /// Clears video-specific diagnostics state on enable.
        /// </summary>
        private void ResetVideoDiagnosticState()
        {
            EnsureVideoPublishPipeline().ResetState();
        }


        private void LogVideoEncoderUnavailable(CameraVideoOutputProfile profile, string reason)
        {
            EnsureVideoPublishPipeline();
            _videoPublishPipeline.TryLogVideoEncoderUnavailable(profile, reason);
        }


        private void LogEncoderStderrIfNeeded(ICameraVideoEncoderSidecar sidecar)
        {
            if (!_logEncoderStderr || sidecar == null)
                return;

            EnsureVideoPublishPipeline();
            _videoPublishPipeline.LogEncoderStderrIfNeeded(
                _logEncoderStderr,
                sidecar,
                CameraVideoOutputProfile.ForMode(_videoPublishPipeline.Mode).DisplayName);
        }
    }
}
