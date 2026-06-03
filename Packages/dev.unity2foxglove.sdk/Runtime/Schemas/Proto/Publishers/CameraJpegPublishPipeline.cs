// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Publishers
// Purpose: Encapsulates JPEG publish-side pipeline concerns for FoxgloveCameraPublisher.

using System;
using Unity.FoxgloveSDK.Util;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace Unity.FoxgloveSDK.Components
{
    internal sealed class CameraJpegPublishPipeline
    {
        private const int JpegWorkerStopWaitMs = 500;

        private readonly Func<int> _captureGeneration;
        private readonly CameraPublishDiagnostics _diagnostics;
        private readonly CameraReadbackTiming _readbackTiming = new CameraReadbackTiming();

        private CameraJpegPipeline _jpegPipeline;
        private bool _warnedWorkerFailure;
        private bool _warnedWorkerShutdown;
        private int _maxEncodeQueue = 1;
        private int _maxCompletedQueue = 1;

        public CameraJpegPublishPipeline(Func<int> captureGeneration, CameraPublishDiagnostics diagnostics)
        {
            _captureGeneration = captureGeneration ?? throw new ArgumentNullException(nameof(captureGeneration));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        }

        public int EncodeQueueDepth => _jpegPipeline?.EncodeQueueDepth ?? 0;
        public int CompletedQueueDepth => _jpegPipeline?.CompletedQueueDepth ?? 0;

        public void EnsureQueues(int maxEncodeQueue, int maxCompletedQueue)
        {
            _maxEncodeQueue = Math.Max(1, maxEncodeQueue);
            _maxCompletedQueue = Math.Max(1, maxCompletedQueue);

            if (_jpegPipeline == null)
                _jpegPipeline = new CameraJpegPipeline(_captureGeneration, JpegWorkerStopWaitMs);

            _jpegPipeline.Configure(_maxEncodeQueue, _maxCompletedQueue);
        }

        public bool EnsureWorkerStarted(Action<string> onStartFailure)
        {
            if (_jpegPipeline == null)
                EnsureQueues(_maxEncodeQueue, _maxCompletedQueue);

            if (_jpegPipeline.Start())
            {
                _warnedWorkerShutdown = false;
                return true;
            }

            onStartFailure?.Invoke("Unable to start JPEG worker: " + _jpegPipeline.LastStartError);
            return false;
        }

        public bool StopWorker(bool clearQueues, Action<string> onStopTimeout)
        {
            if (_jpegPipeline == null)
            {
                if (clearQueues)
                    ClearQueues();
                return true;
            }

            if (_jpegPipeline.Stop(clearQueues))
            {
                _warnedWorkerShutdown = false;
                if (clearQueues)
                    ClearReadbackTiming();
                return true;
            }

            if (!_warnedWorkerShutdown)
            {
                onStopTimeout?.Invoke("[Foxglove] Camera JPEG worker is still stopping; stale output will be ignored.");
                _warnedWorkerShutdown = true;
            }

            if (clearQueues)
                ClearReadbackTiming();

            return false;
        }

        public void ClearQueues()
        {
            _jpegPipeline?.Clear();
            ClearReadbackTiming();
        }

        public void ClearReadbackTiming()
        {
            _readbackTiming.Clear();
        }

        public void ResetState()
        {
            EnsureQueues(_maxEncodeQueue, _maxCompletedQueue);
            ClearQueues();
            _warnedWorkerFailure = false;
            _warnedWorkerShutdown = false;
            _diagnostics.ResetCameraState();
        }

        public void RememberReadbackStart(ulong unixNs, long ticks)
            => _readbackTiming.Remember(unixNs, ticks);

        public double TakeReadbackLatencyMs(ulong unixNs)
            => _readbackTiming.TakeLatencyMs(unixNs);

        public bool AllowCaptureByFrameBudget(
            bool useAsyncJpeg,
            int pendingRequests,
            int maxPendingReadbacks,
            int encodeQueueDepth,
            int maxEncodeQueue,
            int completedQueueDepth,
            int maxCompletedQueue,
            int width,
            int height,
            int maxPixelsPerFrame)
        {
            EnsureQueues(maxEncodeQueue, maxCompletedQueue);

            var result = CameraFrameBudgetPolicy.Evaluate(new CameraFrameBudgetInput
            {
                PendingReadbacks = pendingRequests,
                MaxPendingReadbacks = Math.Max(1, maxPendingReadbacks),
                EncodeQueueDepth = useAsyncJpeg ? encodeQueueDepth : 0,
                MaxEncodeQueueDepth = useAsyncJpeg ? Math.Max(1, maxEncodeQueue) : int.MaxValue,
                CompletedQueueDepth = useAsyncJpeg ? completedQueueDepth : 0,
                MaxCompletedQueueDepth = useAsyncJpeg ? Math.Max(1, maxCompletedQueue) : int.MaxValue,
                Width = Math.Max(1, width),
                Height = Math.Max(1, height),
                MaxPixelsPerFrame = Math.Max(0, maxPixelsPerFrame)
            });

            if (result.AllowCapture)
                return true;

            _diagnostics.RecordCameraBudgetSkip(result.SkipReason);
            return false;
        }

        public bool TryQueueFrame(
            byte[] frameBytes,
            ulong unixNs,
            int captureWidth,
            int captureHeight,
            bool publishWebSocket,
            bool publishBridge,
            bool publishNativeFrame,
            PublisherEffectiveEncoding webSocketEncoding,
            double readbackLatencyMs,
            int jpegQuality,
            string frameId,
            bool useStandardRos2CompressedImage,
            int maxEncodedBytes,
            Action<double, double> onReadbackCopy,
            Action onEncodeQueueDrop)
        {
            if (frameBytes == null)
                return false;

            EnsureQueues(_maxEncodeQueue, _maxCompletedQueue);
            onReadbackCopy?.Invoke(readbackLatencyMs, 0);

            var request = new JpegEncodeRequest(
                frameBytes,
                Math.Max(1, captureWidth),
                Math.Max(1, captureHeight),
                Math.Clamp(jpegQuality, 10, 100),
                unixNs,
                frameId,
                publishWebSocket,
                publishBridge,
                publishNativeFrame,
                useStandardRos2CompressedImage,
                webSocketEncoding,
                Math.Max(0, maxEncodedBytes),
                _captureGeneration(),
                _jpegPipeline.WorkerGeneration);

            var dropped = _jpegPipeline.Queue(request);
            if (dropped)
                onEncodeQueueDrop?.Invoke();
            return dropped;
        }

        public int DrainCompleted(
            int maxResults,
            Action<JpegEncodeResult> onResult,
            out int droppedCompleted,
            out double elapsedMs)
        {
            if (onResult == null)
                throw new ArgumentNullException(nameof(onResult));

            if (_jpegPipeline == null)
            {
                droppedCompleted = 0;
                elapsedMs = 0;
                return 0;
            }

            var drainStart = Stopwatch.GetTimestamp();
            var drained = _jpegPipeline.Drain(
                Math.Max(1, maxResults),
                onResult,
                out droppedCompleted);
            elapsedMs = drained > 0 ? ElapsedMs(drainStart) : 0;
            return drained;
        }

        public bool TryLogWorkerFailure(Action<string> onFailure, string reason)
        {
            if (_warnedWorkerFailure)
                return false;

            _warnedWorkerFailure = true;
            onFailure?.Invoke("[Foxglove] Camera JPEG worker disabled: " + (string.IsNullOrWhiteSpace(reason) ? "unknown failure" : reason));
            return true;
        }

        public void ResetWorkerFailure()
        {
            _warnedWorkerFailure = false;
        }

        private static double ElapsedMs(long startTicks)
            => (Stopwatch.GetTimestamp() - startTicks) * 1000d / Stopwatch.Frequency;
    }
}
