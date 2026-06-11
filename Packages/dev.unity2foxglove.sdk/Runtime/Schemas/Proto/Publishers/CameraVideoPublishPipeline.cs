// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Publishers
// Purpose: Encapsulates camera video publish-side pipeline concerns for FoxgloveCameraPublisher.

using System;
using Stopwatch = System.Diagnostics.Stopwatch;
using Foxglove.Schemas;
using Foxglove.Schemas.Video;
using Unity.FoxgloveSDK.Util;

namespace Unity.FoxgloveSDK.Components
{
    internal enum CameraVideoSubmitOutcome
    {
        Submitted,
        FrameDataMissing,
        SidecarUnavailable,
        SidecarNotRunning,
        DimensionMismatch,
        ConversionFailed,
        SubmitRejected
    }

    internal readonly struct CameraVideoSubmitResult
    {
        public CameraVideoSubmitResult(CameraVideoSubmitOutcome outcome, string reason, double submitMs)
        {
            Outcome = outcome;
            Reason = reason;
            SubmitMs = submitMs;
        }

        public CameraVideoSubmitOutcome Outcome { get; }
        public string Reason { get; }
        public double SubmitMs { get; }

        public bool Submitted => Outcome == CameraVideoSubmitOutcome.Submitted;
    }

    internal interface ICameraVideoFrameBytesSource
    {
        int Length { get; }
        byte[] ToArray();
    }

    internal sealed class CameraVideoPublishPipeline
    {
        private readonly CameraPublishDiagnostics _diagnostics;
        private readonly Action<string> _logWarning;
        private readonly CameraVideoSidecarSession _videoSidecarSession = new CameraVideoSidecarSession();
        private bool _warnedVideoEncoderUnavailable;
        private string _lastLoggedStderr;

        public CameraVideoPublishPipeline(CameraPublishDiagnostics diagnostics, Action<string> logWarning = null)
        {
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            _logWarning = logWarning ?? (_ => { });
        }

        public int SidecarWidth => _videoSidecarSession.Width;
        public int SidecarHeight => _videoSidecarSession.Height;
        public CameraOutputMode Mode => _videoSidecarSession.Mode;
        public bool IsOpenH264Mode => _videoSidecarSession.IsOpenH264Mode;

        public void ResetState()
        {
            _warnedVideoEncoderUnavailable = false;
            _lastLoggedStderr = null;
            _videoSidecarSession.ResetRestartState();
            _diagnostics.ResetVideoState();
        }

        public bool EnsureVideoSidecarStarted(
            CameraVideoOutputProfile profile,
            CameraVideoSidecarConfig config,
            Action drainEncodedAccessUnits,
            out string error)
        {
            error = "";
            if (!profile.IsVideo)
                return false;

            if (_videoSidecarSession.EnsureStarted(
                profile,
                config,
                drainEncodedAccessUnits,
                out error))
            {
                _warnedVideoEncoderUnavailable = false;
                _diagnostics.ResetVideoDimensionMismatchWarning();
                return true;
            }

            return false;
        }

        public CameraVideoSubmitResult SubmitVideoFrame<TFrameBytes>(
            TFrameBytes frameBytes,
            ulong renderUnixNs,
            int captureWidth,
            int captureHeight) where TFrameBytes : struct, ICameraVideoFrameBytesSource
        {
            var submitStart = Stopwatch.GetTimestamp();
            if (frameBytes.Length <= 0)
                return new CameraVideoSubmitResult(CameraVideoSubmitOutcome.FrameDataMissing, "Video frame data is empty.", 0d);

            var sidecar = _videoSidecarSession.Sidecar;
            if (sidecar == null)
            {
                _diagnostics.RecordVideoSubmitFailure();
                var result = new CameraVideoSubmitResult(
                    CameraVideoSubmitOutcome.SidecarUnavailable,
                    "Video encoder is not running.",
                    ElapsedMs(submitStart));
                _diagnostics.RecordVideoSubmitMs(result.SubmitMs);
                return result;
            }

            if (!sidecar.IsRunning)
            {
                _diagnostics.RecordVideoSubmitFailure();
                var result = new CameraVideoSubmitResult(
                    CameraVideoSubmitOutcome.SidecarNotRunning,
                    _videoSidecarSession.DescribeFailure("Video encoder process exited."),
                    ElapsedMs(submitStart));
                _diagnostics.RecordVideoSubmitMs(result.SubmitMs);
                return result;
            }

            captureWidth = Math.Max(1, captureWidth);
            captureHeight = Math.Max(1, captureHeight);
            if (!CameraVideoFrameValidator.TryValidateCapturedFrame(
                captureWidth,
                captureHeight,
                frameBytes.Length,
                _videoSidecarSession.Width,
                _videoSidecarSession.Height,
                out var dimensionError))
            {
                var result = new CameraVideoSubmitResult(
                    CameraVideoSubmitOutcome.DimensionMismatch,
                    dimensionError,
                    ElapsedMs(submitStart));
                _diagnostics.RecordVideoSubmitMs(result.SubmitMs);
                return result;
            }

            var ownedFrameBytes = frameBytes.ToArray();
            if (ownedFrameBytes == null || ownedFrameBytes.Length == 0)
            {
                _diagnostics.RecordVideoSubmitFailure();
                var result = new CameraVideoSubmitResult(
                    CameraVideoSubmitOutcome.FrameDataMissing,
                    "Video frame data is empty.",
                    ElapsedMs(submitStart));
                _diagnostics.RecordVideoSubmitMs(result.SubmitMs);
                return result;
            }

            if (_videoSidecarSession.IsOpenH264Mode)
            {
                var i420 = new byte[captureWidth * captureHeight * 3 / 2];
                if (!Rgb24ToI420Converter.TryConvertRgb24ToI420(
                    ownedFrameBytes,
                    captureWidth,
                    captureHeight,
                    i420,
                    flipVertical: true,
                    out var conversionError))
                {
                    _diagnostics.RecordVideoSubmitFailure();
                    var result = new CameraVideoSubmitResult(
                        CameraVideoSubmitOutcome.ConversionFailed,
                        conversionError,
                        ElapsedMs(submitStart));
                    _diagnostics.RecordVideoSubmitMs(result.SubmitMs);
                    return result;
                }

                ownedFrameBytes = i420;
            }

            if (!_videoSidecarSession.TrySubmitFrame(ownedFrameBytes, renderUnixNs))
            {
                _diagnostics.RecordVideoSubmitFailure();
                var result = new CameraVideoSubmitResult(
                    CameraVideoSubmitOutcome.SubmitRejected,
                    _videoSidecarSession.DescribeFailure("Video encoder refused the frame."),
                    ElapsedMs(submitStart));
                _diagnostics.RecordVideoSubmitMs(result.SubmitMs);
                return result;
            }

            _diagnostics.RecordVideoFrameSubmitted();
            var submitted = new CameraVideoSubmitResult(CameraVideoSubmitOutcome.Submitted, "", ElapsedMs(submitStart));
            _diagnostics.RecordVideoSubmitMs(submitted.SubmitMs);
            return submitted;
        }

        public bool TryDrainEncodedAccessUnits(
            Func<ulong> fallbackTimestampNs,
            Action<byte[], ulong, string> publishAccessUnit,
            Action<ICameraVideoEncoderSidecar> observeSidecar,
            out double elapsedMs)
        {
            if (fallbackTimestampNs == null)
                throw new ArgumentNullException(nameof(fallbackTimestampNs));
            if (publishAccessUnit == null)
                throw new ArgumentNullException(nameof(publishAccessUnit));

            var drainStart = Stopwatch.GetTimestamp();
            if (!_videoSidecarSession.TryDrain(fallbackTimestampNs, publishAccessUnit, observeSidecar))
            {
                elapsedMs = 0;
                return false;
            }

            elapsedMs = ElapsedMs(drainStart);
            return true;
        }

        public void StopVideoSidecar(Action drainEncodedAccessUnits)
            => _videoSidecarSession.Stop(drainEncodedAccessUnits);

        public CameraVideoSidecarMatchResult EnsureSidecarMatchesMode(
            CameraVideoOutputProfile profile,
            int desiredWidth,
            int desiredHeight,
            double nowSeconds,
            Action drainEncodedAccessUnits)
        {
            var result = _videoSidecarSession.EnsureMatchesMode(
                profile,
                desiredWidth,
                desiredHeight,
                nowSeconds,
                drainEncodedAccessUnits);
            if (result.ResetEncoderWarning)
                _warnedVideoEncoderUnavailable = false;

            return result;
        }

        public bool TryLogVideoEncoderUnavailable(CameraVideoOutputProfile profile, string reason)
        {
            if (_warnedVideoEncoderUnavailable)
                return false;

            _warnedVideoEncoderUnavailable = true;
            _logWarning("[Foxglove] " + profile.DisplayName + " camera video disabled: " + reason);
            return true;
        }

        public void ResetVideoEncoderWarning()
        {
            _warnedVideoEncoderUnavailable = false;
            _lastLoggedStderr = null;
        }

        public void LogEncoderStderrIfNeeded(bool logEncoderStderr, ICameraVideoEncoderSidecar sidecar, string videoDisplayName)
        {
            if (!logEncoderStderr || sidecar == null)
                return;

            var line = sidecar.LastDiagnosticLine;
            if (string.IsNullOrEmpty(line) || line == _lastLoggedStderr)
                return;

            _lastLoggedStderr = line;
            _logWarning("[Foxglove] " + videoDisplayName + ": " + line);
        }

        public static double ElapsedMs(long startTicks)
            => (Stopwatch.GetTimestamp() - startTicks) * 1000d / Stopwatch.Frequency;
    }
}
