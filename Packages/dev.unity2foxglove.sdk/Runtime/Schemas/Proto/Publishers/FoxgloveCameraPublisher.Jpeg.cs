// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Publishers
// Purpose: JPEG helper methods for FoxgloveCameraPublisher.
using System;
using Foxglove.Schemas;
using Foxglove.Schemas.Video;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Schemas.Camera;
using Unity.FoxgloveSDK.Schemas.Ros2Msg;
using Unity.FoxgloveSDK.Util;
using UnityEngine;
using UnityEngine.Rendering;
using System.Threading;
using Stopwatch = System.Diagnostics.Stopwatch;
namespace Unity.FoxgloveSDK.Components
{
    public partial class FoxgloveCameraPublisher
    {

        private CameraJpegPublishPipeline EnsureJpegPublishPipeline()
        {
            if (_jpegPublishPipeline == null)
                _jpegPublishPipeline = new CameraJpegPublishPipeline(() => Volatile.Read(ref _captureGeneration), _diagnostics);
            return _jpegPublishPipeline;
        }


        /// <summary>
        /// Applies static resource caps before rendering so camera visualization cannot
        /// consume unbounded readback or worker queue capacity.
        /// </summary>
        private bool AllowJpegCaptureByFrameBudget()
        {
            EnsureJpegPublishPipeline();
            return _jpegPublishPipeline.AllowCaptureByFrameBudget(
                _useAsyncJpeg,
                _pendingRequests,
                Math.Max(1, _maxPendingReadbacks),
                _jpegPublishPipeline.EncodeQueueDepth,
                _maxJpegEncodeQueue,
                _jpegPublishPipeline.CompletedQueueDepth,
                _maxCompletedJpegQueue,
                _width,
                _height,
                _maxPixelsPerFrame);
        }

        /// <summary>
        /// Copies readback bytes on the main thread into an owned buffer before handing
        /// work to the JPEG worker; the worker never touches Unity objects.
        /// </summary>
        private void QueueJpegFrame(
            AsyncGPUReadbackRequest req,
            ulong unixNs,
            int captureWidth,
            int captureHeight,
            bool publishWebSocket,
            bool publishBridge,
            bool publishNativeFrame,
            PublisherEffectiveEncoding webSocketEncoding,
            double readbackLatencyMs,
            byte[] frameBytes = null)
        {
            EnsureJpegPublishPipeline();
            var copyStart = Stopwatch.GetTimestamp();
            frameBytes ??= req.GetData<byte>().ToArray();
            _jpegPublishPipeline.TryQueueFrame(
                frameBytes,
                unixNs,
                captureWidth,
                captureHeight,
                publishWebSocket,
                publishBridge,
                publishNativeFrame,
                webSocketEncoding,
                readbackLatencyMs,
                _jpegQuality,
                ResolveFrameId(),
                _publishStandardRos2CompressedImage,
                _maxEncodedBytes,
                onReadbackCopy: (latency, _) =>
                {
                    var copyMs = ElapsedMs(copyStart);
                    _diagnostics.RecordReadbackCopy(
                        latency,
                        copyMs,
                        Time.realtimeSinceStartupAsDouble,
                        _pendingRequests,
                        _jpegPublishPipeline?.EncodeQueueDepth ?? 0,
                        _jpegPublishPipeline?.CompletedQueueDepth ?? 0);
                    EmitCameraSlowStageIfNeeded(
                        "readbackCopy",
                        copyMs,
                        _pendingRequests,
                        _pendingRequests);
                },
                onEncodeQueueDrop: () => _diagnostics.RecordEncodeQueueDrop());
        }

        /// <summary>
        /// Publishes a bounded number of completed worker results per frame to keep
        /// worker catch-up from monopolizing the main loop.
        /// </summary>
        private void DrainCompletedJpegFrames()
        {
            EnsureJpegPublishPipeline();
            var drained = _jpegPublishPipeline.DrainCompleted(
                _maxCompletedJpegPublishesPerFrame,
                PublishCompletedJpegFrame,
                out var droppedCompleted,
                out var elapsedMs);
            if (elapsedMs > 0)
            {
                _diagnostics.RecordCompletedJpegDrain(
                    elapsedMs,
                    Time.realtimeSinceStartupAsDouble,
                    _pendingRequests,
                    _jpegPublishPipeline?.EncodeQueueDepth ?? 0,
                    _jpegPublishPipeline?.CompletedQueueDepth ?? 0);
                EmitCameraSlowStageIfNeeded(
                    "completedJpegDrain",
                    elapsedMs,
                    _pendingRequests,
                    _pendingRequests);
            }
            if (droppedCompleted > 0)
                _diagnostics.RecordCompletedJpegDrops(droppedCompleted);

            EmitCameraDiagnosticsIfNeeded();
        }

        /// <summary>
        /// Rejects stale or out-of-order worker results before publishing the freshest
        /// serialized JPEG payloads.
        /// </summary>
        private void PublishCompletedJpegFrame(JpegEncodeResult result)
        {
            if (result.Request.Generation != Volatile.Read(ref _captureGeneration))
                return;

            var captureUnixNs = result.Request.CaptureUnixNs;
            if (!CameraJpegPublishOrderPolicy.ShouldPublish(captureUnixNs, _lastPublishedCaptureUnixNs))
            {
                _diagnostics.RecordLateJpegDrop();
                return;
            }

            _diagnostics.RecordJpegEncodeResult(result.EncodeMs, result.SerializeMs, result.JpegBytes);

            if (result.DroppedByEncodedBudget)
            {
                _diagnostics.RecordEncodedBudgetDrop();
                EmitBackpressureWarning(
                    $"[Foxglove] Camera frame dropped: encoded size {result.JpegBytes} exceeds budget {result.Request.MaxEncodedBytes}.");
                return;
            }

            if (!result.Success)
            {
                LogJpegWorkerFailure(result.Error);
                return;
            }

            if (result.Request.PublishNativeFrame && result.SensorFrame != null)
            {
                SensorCompressedImageReady?.Invoke(result.SensorFrame);
                _lastPublishedCaptureUnixNs = captureUnixNs;
                _backpressureGate.ResetSkipLogCount();
            }

            if (result.Request.PublishWebSocket && result.Request.WebSocketEncoding == PublisherEffectiveEncoding.Protobuf)
            {
                PublishProto(result.WebSocketPayload, captureUnixNs);
                _lastPublishedCaptureUnixNs = captureUnixNs;
                _backpressureGate.ResetSkipLogCount();
            }
            else if (result.Request.PublishWebSocket && result.Request.WebSocketEncoding == PublisherEffectiveEncoding.Ros2)
            {
                PublishRos2(result.WebSocketPayload, captureUnixNs);
                _lastPublishedCaptureUnixNs = captureUnixNs;
                _backpressureGate.ResetSkipLogCount();
            }
            else if (result.Request.PublishWebSocket)
            {
                Publish(result.JsonMessage, captureUnixNs);
                _lastPublishedCaptureUnixNs = captureUnixNs;
                _backpressureGate.ResetSkipLogCount();
            }

            if (result.Request.PublishBridge)
            {
                PublishRos2Bridge(result.BridgePayload, captureUnixNs);
                _lastPublishedCaptureUnixNs = captureUnixNs;
                _backpressureGate.ResetSkipLogCount();
            }

            EnsureJpegPublishPipeline().ResetWorkerFailure();
        }

        /// <summary>
        /// Synchronous JPEG fallback path; it still uses captured readback dimensions
        /// instead of mutable Inspector dimensions.
        /// </summary>
        private void PublishJpegFrame(AsyncGPUReadbackRequest req, ulong unixNs, int captureWidth, int captureHeight, byte[] frameBytes = null)
        {
            var jpeg = frameBytes == null
                ? _captureResources.EncodeJpeg(req, captureWidth, captureHeight, _jpegQuality)
                : _captureResources.EncodeJpeg(frameBytes, captureWidth, captureHeight, _jpegQuality);
            if (jpeg == null || jpeg.Length == 0) return;

            if (CameraBackpressurePolicy.ExceedsBudget(jpeg, _maxEncodedBytes))
            {
                EmitBackpressureWarning(
                    $"[Foxglove] Camera frame dropped: encoded size {jpeg.Length} exceeds budget {_maxEncodedBytes}.");
                return;
            }

            if (!CameraJpegPublishOrderPolicy.ShouldPublish(unixNs, _lastPublishedCaptureUnixNs))
            {
                _diagnostics.RecordLateJpegDrop();
                return;
            }

            var publishWebSocket = ShouldPreparePublishPayload();
            var publishBridge = ShouldPrepareRos2BridgePayload();
            var publishNativeFrame = HasSensorCompressedImageDemand();
            var frameId = ResolveFrameId();
            byte[] ros2Payload = null;

            if (publishWebSocket && EffectiveEncoding == PublisherEffectiveEncoding.Protobuf)
            {
                var payload = CameraCompressedImageBuilder.Serialize(unixNs, frameId, jpeg, "jpeg");
                PublishProto(payload, unixNs);
                _lastPublishedCaptureUnixNs = unixNs;
                _backpressureGate.ResetSkipLogCount();
            }
            else if (publishWebSocket && EffectiveEncoding == PublisherEffectiveEncoding.Ros2)
            {
                ros2Payload = SerializeRos2CompressedImage(unixNs, frameId, jpeg);
                PublishRos2(ros2Payload, unixNs);
                _lastPublishedCaptureUnixNs = unixNs;
                _backpressureGate.ResetSkipLogCount();
            }
            else if (publishWebSocket)
            {
                var msg = new CompressedImageMessage
                {
                    Timestamp = FoxgloveTimeUtil.ToFoxgloveTime(unixNs),
                    FrameId = frameId,
                    Data = Convert.ToBase64String(jpeg),
                    Format = "jpeg"
                };

                Publish(msg, unixNs);
                _lastPublishedCaptureUnixNs = unixNs;
                _backpressureGate.ResetSkipLogCount();
            }

            if (publishBridge)
            {
                ros2Payload ??= SerializeRos2CompressedImage(unixNs, frameId, jpeg);
                PublishRos2Bridge(ros2Payload, unixNs);
                _lastPublishedCaptureUnixNs = unixNs;
                _backpressureGate.ResetSkipLogCount();
            }

            if (publishNativeFrame)
            {
                SensorCompressedImageReady?.Invoke(new SensorCompressedImageFrame(unixNs, frameId, jpeg, "jpeg"));
                _lastPublishedCaptureUnixNs = unixNs;
                _backpressureGate.ResetSkipLogCount();
            }
        }

        /// <summary>
        /// Lazily starts the background JPEG worker after demand and budget gates pass.
        /// </summary>
        private bool EnsureJpegWorkerStarted()
        {
            EnsureJpegPublishPipeline();
            return _jpegPublishPipeline.EnsureWorkerStarted(reason =>
                {
                    if (!string.IsNullOrEmpty(reason))
                        LogJpegWorkerFailure(reason);
                });
        }

        /// <summary>
        /// Requests worker shutdown without blocking Play Mode indefinitely; late output is
        /// discarded by queue clearing and generation checks.
        /// </summary>
        private void StopJpegWorker(bool clearQueues)
        {
            _jpegPublishPipeline?.StopWorker(
                clearQueues,
                reason => Debug.LogWarning(reason));
        }

        private void ClearJpegQueues()
        {
            _jpegPublishPipeline?.ClearQueues();
        }

        private void ClearReadbackTiming()
        {
            _jpegPublishPipeline?.ClearReadbackTiming();
        }

        private void ResetJpegPipelineState()
        {
            EnsureJpegPublishPipeline().ResetState();
            _lastPublishedCaptureUnixNs = 0;
            _diagnostics.ResetCameraState();
        }

        /// <summary>
        /// Tracks readback latency for diagnostics without making timing data part of the
        /// publish contract.
        /// </summary>
        private void RememberReadbackStart(ulong unixNs, long ticks)
        {
            EnsureJpegPublishPipeline();
            _jpegPublishPipeline.RememberReadbackStart(unixNs, ticks);
        }

        private double TakeReadbackLatencyMs(ulong unixNs)
        {
            EnsureJpegPublishPipeline();
            return _jpegPublishPipeline.TakeReadbackLatencyMs(unixNs);
        }

        private void LogJpegWorkerFailure(string reason)
        {
            EnsureJpegPublishPipeline();
            _jpegPublishPipeline.TryLogWorkerFailure(msg =>
            {
                if (!string.IsNullOrWhiteSpace(msg))
                    Debug.LogWarning(msg);
            }, reason);
        }
    }
}
