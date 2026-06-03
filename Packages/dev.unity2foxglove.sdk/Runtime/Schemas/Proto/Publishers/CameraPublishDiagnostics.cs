// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Publishers

using System;
using Unity.FoxgloveSDK.Util;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Aggregates camera JPEG and video publish diagnostics outside the camera publisher.
    /// </summary>
    internal sealed class CameraPublishDiagnostics
    {
        private double _nextCameraDiagLogSec;
        private double _lastRenderMs;
        private double _lastReadbackLatencyMs;
        private double _lastReadbackCopyMs;
        private double _lastJpegEncodeMs;
        private double _lastSerializeMs;
        private double _lastPublishDrainMs;
        private int _lastJpegBytes;
        private int _readbackBudgetSkipCount;
        private int _encodeBudgetSkipCount;
        private int _completedBudgetSkipCount;
        private int _pixelBudgetSkipCount;
        private int _noDemandJpegDropCount;
        private int _droppedEncodeQueueCount;
        private int _droppedCompletedJpegCount;
        private int _droppedEncodedBudgetCount;
        private int _droppedLateJpegCount;

        private double _nextVideoDiagLogSec;
        private double _lastVideoSubmitMs;
        private double _lastVideoDrainMs;
        private int _lastVideoAccessUnitBytes;
        private int _videoFramesSubmittedCount;
        private int _videoAccessUnitsPublishedCount;
        private int _videoDimensionMismatchDropCount;
        private int _videoSubmitFailureCount;
        private int _videoSidecarRestartCount;
        private string _lastVideoDiagnostic = "";
        private bool _warnedVideoDimensionMismatch;

        public void RecordRenderMs(double elapsedMs)
            => _lastRenderMs = elapsedMs;

        public void RecordReadbackCopy(double latencyMs, double copyMs)
        {
            _lastReadbackLatencyMs = latencyMs;
            _lastReadbackCopyMs = copyMs;
        }

        public void RecordCameraBudgetSkip(CameraFrameBudgetSkipReason reason)
        {
            switch (reason)
            {
                case CameraFrameBudgetSkipReason.ReadbackQueueFull:
                    _readbackBudgetSkipCount++;
                    break;
                case CameraFrameBudgetSkipReason.EncodeQueueFull:
                    _encodeBudgetSkipCount++;
                    break;
                case CameraFrameBudgetSkipReason.CompletedQueueFull:
                    _completedBudgetSkipCount++;
                    break;
                case CameraFrameBudgetSkipReason.PixelBudgetExceeded:
                    _pixelBudgetSkipCount++;
                    break;
            }
        }

        public void RecordNoDemandJpegDrop()
            => _noDemandJpegDropCount++;

        public void RecordEncodeQueueDrop()
            => _droppedEncodeQueueCount++;

        public void RecordCompletedJpegDrops(int count)
        {
            if (count > 0)
                _droppedCompletedJpegCount += count;
        }

        public void RecordPublishDrainMs(double elapsedMs)
            => _lastPublishDrainMs = elapsedMs;

        public void RecordLateJpegDrop()
            => _droppedLateJpegCount++;

        public void RecordJpegEncodeResult(double encodeMs, double serializeMs, int jpegBytes)
        {
            _lastJpegEncodeMs = encodeMs;
            _lastSerializeMs = serializeMs;
            _lastJpegBytes = jpegBytes;
        }

        public void RecordEncodedBudgetDrop()
            => _droppedEncodedBudgetCount++;

        public void LogCameraIfNeeded(
            bool enabled,
            double nowSeconds,
            double intervalSeconds,
            int pendingReadbacks,
            int encodeQueueDepth,
            int completedQueueDepth,
            out string message)
        {
            message = null;
            if (!enabled)
                return;

            if (nowSeconds < _nextCameraDiagLogSec)
                return;

            _nextCameraDiagLogSec = nowSeconds + Math.Max(0.1d, intervalSeconds);
            message =
                "[Foxglove][CameraDiag] " +
                $"renderMs={_lastRenderMs:F2} readbackLatencyMs={_lastReadbackLatencyMs:F2} readbackCopyMs={_lastReadbackCopyMs:F2} " +
                $"jpegMs={_lastJpegEncodeMs:F2} serializeMs={_lastSerializeMs:F2} publishDrainMs={_lastPublishDrainMs:F2} " +
                $"bytes={_lastJpegBytes} pendingReadbacks={pendingReadbacks} encodeQueue={encodeQueueDepth} completedQueue={completedQueueDepth} " +
                $"skips(readback={_readbackBudgetSkipCount},encode={_encodeBudgetSkipCount},completed={_completedBudgetSkipCount},pixels={_pixelBudgetSkipCount}) " +
                $"drops(noDemand={_noDemandJpegDropCount},encodeQueue={_droppedEncodeQueueCount},completedQueue={_droppedCompletedJpegCount},encodedBudget={_droppedEncodedBudgetCount},late={_droppedLateJpegCount}).";
            ResetCameraCounters();
        }

        public void ResetCameraState()
        {
            _nextCameraDiagLogSec = 0;
            _lastRenderMs = 0;
            _lastReadbackLatencyMs = 0;
            _lastReadbackCopyMs = 0;
            _lastJpegEncodeMs = 0;
            _lastSerializeMs = 0;
            _lastPublishDrainMs = 0;
            _lastJpegBytes = 0;
            ResetCameraCounters();
        }

        public void RecordVideoSubmitFailure()
            => _videoSubmitFailureCount++;

        public void RecordVideoSubmitMs(double elapsedMs)
            => _lastVideoSubmitMs = elapsedMs;

        public void RecordVideoFrameSubmitted()
            => _videoFramesSubmittedCount++;

        public void RecordVideoDrainMs(double elapsedMs)
            => _lastVideoDrainMs = elapsedMs;

        public void RecordVideoAccessUnitPublished(int byteCount)
        {
            _lastVideoAccessUnitBytes = byteCount;
            _videoAccessUnitsPublishedCount++;
        }

        public void RecordVideoDiagnostic(string diagnostic)
        {
            if (!string.IsNullOrEmpty(diagnostic))
                _lastVideoDiagnostic = diagnostic;
        }

        public bool RecordVideoDimensionMismatchDrop(string reason, bool warnOnce)
        {
            _videoDimensionMismatchDropCount++;
            RecordVideoDiagnostic(reason);
            if (!warnOnce || _warnedVideoDimensionMismatch)
                return false;

            _warnedVideoDimensionMismatch = true;
            return true;
        }

        public void ResetVideoDimensionMismatchWarning()
            => _warnedVideoDimensionMismatch = false;

        public void RecordVideoSidecarRestart()
            => _videoSidecarRestartCount++;

        public void LogVideoIfNeeded(
            bool enabled,
            double nowSeconds,
            double intervalSeconds,
            string modeDisplayName,
            int width,
            int height,
            int pendingReadbacks,
            out string message)
        {
            message = null;
            if (!enabled)
                return;

            if (nowSeconds < _nextVideoDiagLogSec)
                return;

            _nextVideoDiagLogSec = nowSeconds + Math.Max(0.1d, intervalSeconds);
            message =
                "[Foxglove][VideoDiag] " +
                $"mode={modeDisplayName} width={width} height={height} " +
                $"videoSubmitMs={_lastVideoSubmitMs:F2} videoDrainMs={_lastVideoDrainMs:F2} accessUnitBytes={_lastVideoAccessUnitBytes} " +
                $"pendingReadbacks={pendingReadbacks} framesSubmitted={_videoFramesSubmittedCount} accessUnitsPublished={_videoAccessUnitsPublishedCount} " +
                $"dimensionMismatch={_videoDimensionMismatchDropCount} submitFailures={_videoSubmitFailureCount} sidecarRestarts={_videoSidecarRestartCount} " +
                $"lastDiagnostic={_lastVideoDiagnostic ?? ""}.";
            ResetVideoCounters();
        }

        public void ResetVideoState()
        {
            _nextVideoDiagLogSec = 0;
            _lastVideoSubmitMs = 0;
            _lastVideoDrainMs = 0;
            _lastVideoAccessUnitBytes = 0;
            _lastVideoDiagnostic = "";
            _warnedVideoDimensionMismatch = false;
            ResetVideoCounters();
        }

        private void ResetCameraCounters()
        {
            _readbackBudgetSkipCount = 0;
            _encodeBudgetSkipCount = 0;
            _completedBudgetSkipCount = 0;
            _pixelBudgetSkipCount = 0;
            _noDemandJpegDropCount = 0;
            _droppedEncodeQueueCount = 0;
            _droppedCompletedJpegCount = 0;
            _droppedEncodedBudgetCount = 0;
            _droppedLateJpegCount = 0;
        }

        private void ResetVideoCounters()
        {
            _videoFramesSubmittedCount = 0;
            _videoAccessUnitsPublishedCount = 0;
            _videoDimensionMismatchDropCount = 0;
            _videoSubmitFailureCount = 0;
            _videoSidecarRestartCount = 0;
            _lastVideoDiagnostic = "";
        }
    }
}
