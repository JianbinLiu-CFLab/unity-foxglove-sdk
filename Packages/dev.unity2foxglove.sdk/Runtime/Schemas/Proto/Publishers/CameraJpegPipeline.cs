// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Publishers
// Purpose: Owns the async JPEG worker queue, signal, and generation lifecycle.

using System;
using System.Threading;
using Unity.FoxgloveSDK.Util;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Background JPEG pipeline for owned RGB buffers. Unity API access stays in
    /// the publisher; this type only owns queueing, worker, and generation state.
    /// </summary>
    internal sealed class CameraJpegPipeline : IDisposable
    {
        private const int WorkerWaitMs = 50;

        private readonly Func<int> _currentCaptureGeneration;
        private readonly int _workerStopWaitMs;
        private DropOldestBoundedQueue<JpegEncodeRequest> _encodeQueue;
        private DropOldestBoundedQueue<JpegEncodeResult> _completedQueue;
        private AutoResetEvent _workerSignal;
        private Thread _worker;
        private Thread _orphanedWorker;
        private volatile bool _workerStopping;
        private int _workerGeneration;
        private int _encodeCapacity = 1;
        private int _completedCapacity = 1;
        private int _droppedCompletedCount;
        private int _disposed;

        /// <param name="currentCaptureGeneration">
        /// Worker-thread-safe generation reader. The delegate must read plain
        /// managed state via Volatile/Interlocked and must not touch Unity APIs.
        /// </param>
        public CameraJpegPipeline(Func<int> currentCaptureGeneration, int workerStopWaitMs)
        {
            _currentCaptureGeneration = currentCaptureGeneration ?? throw new ArgumentNullException(nameof(currentCaptureGeneration));
            if (workerStopWaitMs < 0)
                throw new ArgumentOutOfRangeException(nameof(workerStopWaitMs), "Stop wait must be non-negative.");

            _workerStopWaitMs = workerStopWaitMs;
        }

        public string LastStartError { get; private set; }
        public int WorkerGeneration => Volatile.Read(ref _workerGeneration);
        public int EncodeQueueDepth => Volatile.Read(ref _encodeQueue)?.Count ?? 0;
        public int CompletedQueueDepth => Volatile.Read(ref _completedQueue)?.Count ?? 0;

        public void Configure(int maxEncodeQueue, int maxCompletedQueue)
        {
            ThrowIfDisposed();
            _encodeCapacity = Math.Max(1, maxEncodeQueue);
            _completedCapacity = Math.Max(1, maxCompletedQueue);
            EnsureQueues();
        }

        public bool Start()
        {
            ThrowIfDisposed();
            if (!TryJoinOrphanedWorker())
            {
                LastStartError = "The previous JPEG worker is still stopping; restart was rejected.";
                return false;
            }
            EnsureQueues();
            if (_worker != null && _worker.IsAlive && !_workerStopping)
                return true;

            AutoResetEvent workerSignal = null;
            try
            {
                var workerGeneration = Interlocked.Increment(ref _workerGeneration);

                _workerStopping = false;
                workerSignal = new AutoResetEvent(false);
                _workerSignal = workerSignal;

                _worker = new Thread(() => EncodeJpegWorkerLoop(workerGeneration, workerSignal))
                {
                    IsBackground = true,
                    Name = "FoxgloveCameraJpegEncoder"
                };
                _worker.Start();
                LastStartError = null;
                return true;
            }
            catch (Exception ex)
            {
                _worker = null;
                if (ReferenceEquals(_workerSignal, workerSignal))
                    _workerSignal = null;
                workerSignal?.Dispose();
                LastStartError = ex.Message;
                return false;
            }
        }

        public bool Queue(JpegEncodeRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            ThrowIfDisposed();
            var queue = Volatile.Read(ref _encodeQueue);
            if (queue == null)
                throw new InvalidOperationException("JPEG queues must be configured before queueing frames.");

            var dropped = queue.Enqueue(request);
            try
            {
                _workerSignal?.Set();
            }
            catch (ObjectDisposedException)
            {
            }

            return dropped;
        }

        public int Drain(int maxResults, Action<JpegEncodeResult> publish, out int droppedCompleted)
        {
            if (publish == null)
                throw new ArgumentNullException(nameof(publish));

            ThrowIfDisposed();
            droppedCompleted = Interlocked.Exchange(ref _droppedCompletedCount, 0);
            var queue = Volatile.Read(ref _completedQueue);
            if (queue == null)
                return 0;

            var drained = 0;
            var limit = Math.Max(1, maxResults);
            while (drained < limit && queue.TryDequeue(out var result))
            {
                drained++;
                publish(result);
            }

            return drained;
        }

        public bool Stop(bool clearQueues)
        {
            if (Volatile.Read(ref _disposed) != 0)
                return true;

            return StopCore(clearQueues);
        }

        private bool StopCore(bool clearQueues)
        {
            _workerStopping = true;
            Interlocked.Increment(ref _workerGeneration);
            var signal = _workerSignal;
            try
            {
                signal?.Set();
            }
            catch (ObjectDisposedException)
            {
            }

            var worker = _worker;
            if (worker != null && worker.IsAlive && !worker.Join(_workerStopWaitMs))
            {
                if (clearQueues)
                    ClearCore();
                _orphanedWorker = worker;
                _worker = null;
                if (ReferenceEquals(_workerSignal, signal))
                    _workerSignal = null;
                _workerStopping = false;
                return false;
            }

            _worker = null;
            _workerStopping = false;
            if (ReferenceEquals(_workerSignal, signal))
                _workerSignal = null;

            if (clearQueues)
                ClearCore();

            return true;
        }

        public void Clear()
        {
            ThrowIfDisposed();
            ClearCore();
        }

        private void ClearCore()
        {
            Volatile.Read(ref _encodeQueue)?.Clear();
            Volatile.Read(ref _completedQueue)?.Clear();
            Interlocked.Exchange(ref _droppedCompletedCount, 0);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            StopCore(clearQueues: true);
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) != 0)
                throw new ObjectDisposedException(nameof(CameraJpegPipeline));
        }

        private void EnsureQueues()
        {
            var encodeQueue = Volatile.Read(ref _encodeQueue);
            if (encodeQueue == null || encodeQueue.Capacity != _encodeCapacity)
                Volatile.Write(ref _encodeQueue, new DropOldestBoundedQueue<JpegEncodeRequest>(_encodeCapacity));

            var completedQueue = Volatile.Read(ref _completedQueue);
            if (completedQueue == null || completedQueue.Capacity != _completedCapacity)
                Volatile.Write(ref _completedQueue, new DropOldestBoundedQueue<JpegEncodeResult>(_completedCapacity));
        }

        private bool TryJoinOrphanedWorker()
        {
            var orphaned = _orphanedWorker;
            if (orphaned == null)
                return true;

            if (!orphaned.IsAlive || orphaned.Join(_workerStopWaitMs))
            {
                _orphanedWorker = null;
                return true;
            }

            return false;
        }

        private void EncodeJpegWorkerLoop(int workerGeneration, AutoResetEvent workerSignal)
        {
            try
            {
                while (!_workerStopping && workerGeneration == WorkerGeneration)
                {
                    var queue = Volatile.Read(ref _encodeQueue);
                    if (queue != null && queue.TryDequeue(out var request))
                    {
                        if (request.Generation != _currentCaptureGeneration())
                            continue;
                        if (request.JpegWorkerGeneration != workerGeneration)
                            continue;

                        var result = CameraJpegWorkerEncoder.EncodeJpegRequest(request);
                        if (!_workerStopping
                            && workerGeneration == WorkerGeneration
                            && result.Request.JpegWorkerGeneration == workerGeneration)
                        {
                            var completed = Volatile.Read(ref _completedQueue);
                            if (completed != null && completed.Enqueue(result))
                                Interlocked.Increment(ref _droppedCompletedCount);
                        }

                        continue;
                    }

                    workerSignal.WaitOne(WorkerWaitMs);
                }
            }
            finally
            {
                if (ReferenceEquals(_workerSignal, workerSignal))
                    _workerSignal = null;
                workerSignal.Dispose();
            }
        }
    }
}
