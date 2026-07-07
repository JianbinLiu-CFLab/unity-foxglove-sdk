// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Video

using System;
using Unity.FoxgloveSDK.Components;

namespace Foxglove.Schemas.Video
{
    /// <summary>
    /// Owns the running camera video encoder sidecar and its restart state.
    /// </summary>
    internal sealed class CameraVideoSidecarSession : IDisposable
    {
        private const double RestartDebounceSeconds = 0.25d;

        private ICameraVideoEncoderSidecar _sidecar;
        private CameraOutputMode _mode = CameraOutputMode.Jpeg;
        private int _width;
        private int _height;
        private bool _restartPending;
        private int _pendingWidth;
        private int _pendingHeight;
        private double _restartDueSec;

        public ICameraVideoEncoderSidecar Sidecar => _sidecar;
        public CameraOutputMode Mode => _mode;
        public int Width => _width;
        public int Height => _height;
        public bool IsOpenH264Mode => _mode == CameraOutputMode.H264OpenH264;
        public int OutputQueueDepth => _sidecar?.OutputQueueDepth ?? 0;
        public int MaxOutputQueue => _sidecar?.MaxOutputQueue ?? 1;

        public bool EnsureStarted(
            CameraVideoOutputProfile profile,
            CameraVideoSidecarConfig config,
            Action drain,
            out string error)
        {
            error = "";
            if (!profile.IsVideo)
                return false;

            if (_sidecar != null && _sidecar.IsRunning && _mode == profile.Mode)
                return true;

            Stop(drain);
            _mode = profile.Mode;

            var started = false;
            switch (profile.Codec)
            {
                case CameraVideoCodec.H264 when profile.Mode == CameraOutputMode.H264Ffmpeg:
                    var h264 = new FfmpegH264EncoderSidecar();
                    _sidecar = h264;
                    started = h264.Start(CameraVideoSidecarOptionsFactory.CreateH264Options(
                        config.FfmpegPath,
                        config.Width,
                        config.Height,
                        config.FrameRate,
                        config.BitrateKbps,
                        config.KeyframeInterval,
                        config.MaxPendingReadbacks,
                        config.MaxOutputQueue));
                    break;
                case CameraVideoCodec.H264 when profile.Mode == CameraOutputMode.H264OpenH264:
                    var openH264 = new OpenH264EncoderSidecar();
                    _sidecar = openH264;
                    started = openH264.Start(CameraVideoSidecarOptionsFactory.CreateOpenH264Options(
                        config.OpenH264HelperPath,
                        config.OpenH264DllPath,
                        config.Width,
                        config.Height,
                        config.FrameRate,
                        config.BitrateKbps,
                        config.KeyframeInterval,
                        config.OpenH264MaxInputQueue,
                        config.MaxOutputQueue));
                    break;
                case CameraVideoCodec.H264 when profile.Mode == CameraOutputMode.H264MediaFoundationExperimental:
                    var nativeH264 = new MediaFoundationH264EncoderSidecar();
                    _sidecar = nativeH264;
                    started = nativeH264.Start(CameraVideoSidecarOptionsFactory.CreateMediaFoundationH264Options(
                        config.Width,
                        config.Height,
                        config.FrameRate,
                        config.BitrateKbps,
                        config.KeyframeInterval,
                        config.MaxPendingReadbacks,
                        config.MaxOutputQueue));
                    break;
                case CameraVideoCodec.H265:
                    var h265 = new FfmpegH265EncoderSidecar();
                    _sidecar = h265;
                    started = h265.Start(CameraVideoSidecarOptionsFactory.CreateH265Options(
                        config.FfmpegPath,
                        config.Width,
                        config.Height,
                        config.FrameRate,
                        config.BitrateKbps,
                        config.KeyframeInterval,
                        config.MaxPendingReadbacks,
                        config.MaxOutputQueue));
                    break;
            }

            if (started)
            {
                _width = config.Width;
                _height = config.Height;
                ResetRestartState();
                return true;
            }

            error = _sidecar?.LastError ?? "Failed to start video encoder.";
            Stop(drain);
            return false;
        }

        public CameraVideoSidecarMatchResult EnsureMatchesMode(
            CameraVideoOutputProfile profile,
            int desiredWidth,
            int desiredHeight,
            double nowSeconds,
            Action drain)
        {
            if (_sidecar == null)
                return CameraVideoSidecarMatchResult.Allow();

            if (!profile.IsVideo || _mode != profile.Mode)
            {
                Stop(drain);
                return CameraVideoSidecarMatchResult.Allow(resetEncoderWarning: true);
            }

            if (_width == desiredWidth && _height == desiredHeight)
            {
                ResetRestartState();
                return CameraVideoSidecarMatchResult.Allow();
            }

            if (!_restartPending || _pendingWidth != desiredWidth || _pendingHeight != desiredHeight)
            {
                _restartPending = true;
                _pendingWidth = desiredWidth;
                _pendingHeight = desiredHeight;
                _restartDueSec = nowSeconds + RestartDebounceSeconds;
            }

            if (nowSeconds < _restartDueSec)
            {
                return CameraVideoSidecarMatchResult.Drop(
                    $"dimensionMismatch=sidecarRestartPending sidecar={_width}x{_height} desired={desiredWidth}x{desiredHeight}");
            }

            Stop(drain);
            ResetRestartState();
            return CameraVideoSidecarMatchResult.Restart(resetEncoderWarning: true);
        }

        public bool TrySubmitFrame(byte[] frameBytes, ulong timestampNs)
        {
            var sidecar = _sidecar;
            if (sidecar == null)
                return false;

            return sidecar is ITimestampedCameraVideoEncoderSidecar timestampedSidecar
                ? timestampedSidecar.TrySubmitFrame(frameBytes, timestampNs)
                : sidecar.TrySubmitFrame(frameBytes);
        }

        public bool TryDrain(
            Func<ulong> fallbackTimestampNs,
            Action<byte[], ulong, string> publishAccessUnit,
            Action<ICameraVideoEncoderSidecar> observeSidecar)
        {
            var sidecar = _sidecar;
            if (sidecar == null)
                return false;

            var videoFormat = ResolveVideoFormat(_mode);
            if (sidecar is ITimestampedCameraVideoEncoderSidecar timestampedSidecar)
            {
                while (timestampedSidecar.TryDequeueEncodedAccessUnit(out EncodedVideoAccessUnit accessUnit))
                    publishAccessUnit(accessUnit.Data, accessUnit.TimestampNs, videoFormat);
            }
            else
            {
                while (sidecar.TryDequeueAccessUnit(out var accessUnit))
                    publishAccessUnit(accessUnit, fallbackTimestampNs(), videoFormat);
            }

            observeSidecar?.Invoke(sidecar);
            return true;
        }

        public string DescribeFailure(string fallback)
            => DescribeFailure(_sidecar, fallback);

        public void Stop(Action drain)
        {
            if (_sidecar == null)
            {
                ResetSidecarState();
                return;
            }

            drain?.Invoke();
            _sidecar.Dispose();
            drain?.Invoke();
            ResetSidecarState();
        }

        public void ResetRestartState()
        {
            _restartPending = false;
            _pendingWidth = 0;
            _pendingHeight = 0;
            _restartDueSec = 0d;
        }

        public void Dispose()
            => Stop(drain: null);

        private void ResetSidecarState()
        {
            _sidecar = null;
            _mode = CameraOutputMode.Jpeg;
            _width = 0;
            _height = 0;
            ResetRestartState();
        }

        private static string ResolveVideoFormat(CameraOutputMode mode)
        {
            var profile = CameraVideoOutputProfile.ForMode(mode);
            return profile.Codec == CameraVideoCodec.H264
                ? CameraCompressedVideoBuilder.H264Format
                : profile.Codec == CameraVideoCodec.H265
                    ? CameraCompressedVideoBuilder.H265Format
                    : profile.VideoFormat;
        }

        private static string DescribeFailure(ICameraVideoEncoderSidecar sidecar, string fallback)
        {
            var reason = string.IsNullOrWhiteSpace(sidecar?.LastError) ? fallback : sidecar.LastError;
            var diagnostic = sidecar?.LastDiagnosticLine;
            if (!string.IsNullOrWhiteSpace(diagnostic)
                && (string.IsNullOrWhiteSpace(reason)
                    || reason.IndexOf(diagnostic, StringComparison.OrdinalIgnoreCase) < 0))
            {
                reason = string.IsNullOrWhiteSpace(reason)
                    ? diagnostic
                    : reason + " Last diagnostic: " + diagnostic;
            }

            return string.IsNullOrWhiteSpace(reason) ? fallback : reason;
        }
    }

    internal readonly struct CameraVideoSidecarConfig
    {
        public CameraVideoSidecarConfig(
            string ffmpegPath,
            string openH264HelperPath,
            string openH264DllPath,
            int width,
            int height,
            int frameRate,
            int bitrateKbps,
            int keyframeInterval,
            int maxPendingReadbacks,
            int openH264MaxInputQueue,
            int maxOutputQueue)
        {
            FfmpegPath = ffmpegPath ?? "";
            OpenH264HelperPath = openH264HelperPath ?? "";
            OpenH264DllPath = openH264DllPath ?? "";
            Width = Math.Max(1, width);
            Height = Math.Max(1, height);
            FrameRate = Math.Max(1, frameRate);
            BitrateKbps = Math.Max(1, bitrateKbps);
            KeyframeInterval = Math.Max(1, keyframeInterval);
            MaxPendingReadbacks = Math.Max(1, maxPendingReadbacks);
            OpenH264MaxInputQueue = Math.Max(1, openH264MaxInputQueue);
            MaxOutputQueue = Math.Max(1, maxOutputQueue);
        }

        public string FfmpegPath { get; }
        public string OpenH264HelperPath { get; }
        public string OpenH264DllPath { get; }
        public int Width { get; }
        public int Height { get; }
        public int FrameRate { get; }
        public int BitrateKbps { get; }
        public int KeyframeInterval { get; }
        public int MaxPendingReadbacks { get; }
        public int OpenH264MaxInputQueue { get; }
        public int MaxOutputQueue { get; }
    }

    internal readonly struct CameraVideoSidecarMatchResult
    {
        private CameraVideoSidecarMatchResult(
            bool allowCapture,
            bool droppedWhilePending,
            bool restarted,
            bool resetEncoderWarning,
            string diagnostic)
        {
            AllowCapture = allowCapture;
            DroppedWhilePending = droppedWhilePending;
            Restarted = restarted;
            ResetEncoderWarning = resetEncoderWarning;
            Diagnostic = diagnostic ?? "";
        }

        public bool AllowCapture { get; }
        public bool DroppedWhilePending { get; }
        public bool Restarted { get; }
        public bool ResetEncoderWarning { get; }
        public string Diagnostic { get; }

        public static CameraVideoSidecarMatchResult Allow(bool resetEncoderWarning = false)
            => new CameraVideoSidecarMatchResult(true, false, false, resetEncoderWarning, "");

        public static CameraVideoSidecarMatchResult Drop(string diagnostic)
            => new CameraVideoSidecarMatchResult(false, true, false, false, diagnostic);

        public static CameraVideoSidecarMatchResult Restart(bool resetEncoderWarning)
            => new CameraVideoSidecarMatchResult(true, false, true, resetEncoderWarning, "");
    }
}
