// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Utilities
// Purpose: Reusable generation-guarded background encode pipeline.

using System;
using System.Collections.Generic;
using System.Threading;

namespace Unity.FoxgloveSDK.Util
{
    /// <summary>Request contract for generation-guarded background encode pipelines.</summary>
    internal interface IBackgroundEncodeRequest
    {
        int Generation { get; set; }
    }

    /// <summary>
    /// Last-value-wins background encode pipeline with bounded completed results.
    /// The encode delegate runs on a background thread; drain and stop are called
    /// from the owning main-thread component.
    /// </summary>
    internal sealed class BackgroundEncodePipeline<TRequest, TResult>
        where TRequest : class, IBackgroundEncodeRequest
    {
        private readonly BackgroundWorkerLifecycle _worker = new BackgroundWorkerLifecycle();
        private readonly Queue<TResult> _completed = new Queue<TResult>();
        private readonly Func<TRequest, TResult> _encode;
        private readonly Action<Exception> _onEncodeError;
        private readonly string _threadName;
        private readonly int _completedCapacity;
        private readonly int _stopWaitMs;
        private TRequest _pending;
        private int _droppedCompletedCount;
        private int _encodeErrorCount;

        public BackgroundEncodePipeline(
            string threadName,
            int completedCapacity,
            int stopWaitMs,
            Func<TRequest, TResult> encode,
            Action<Exception> onEncodeError = null)
        {
            if (string.IsNullOrWhiteSpace(threadName))
                throw new ArgumentException("Thread name is required.", nameof(threadName));
            if (completedCapacity < 1)
                throw new ArgumentOutOfRangeException(nameof(completedCapacity), "Completed capacity must be positive.");
            if (stopWaitMs < 0)
                throw new ArgumentOutOfRangeException(nameof(stopWaitMs), "Stop wait must be non-negative.");

            _threadName = threadName;
            _completedCapacity = completedCapacity;
            _stopWaitMs = stopWaitMs;
            _encode = encode ?? throw new ArgumentNullException(nameof(encode));
            _onEncodeError = onEncodeError;
        }

        public bool Enqueue(TRequest request, out bool replacedPending, out string startError)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            var startWorker = false;
            var workerGeneration = 0;
            startError = null;
            lock (_worker.Gate)
            {
                replacedPending = _pending != null;
                workerGeneration = _worker.StartOrReuseLocked(out startWorker);
                request.Generation = workerGeneration;
                _pending = request;
            }

            if (!startWorker)
                return true;

            try
            {
                StartWorker(workerGeneration);
                return true;
            }
            catch (Exception ex)
            {
                lock (_worker.Gate)
                    _worker.MarkStartFailedIfCurrentLocked(workerGeneration);

                startError = ex.Message;
                return false;
            }
        }

        public void Drain(List<TResult> results, out int droppedCompletedResults)
            => Drain(results, out droppedCompletedResults, out _);

        public void Drain(List<TResult> results, out int droppedCompletedResults, out int encodeErrors)
        {
            if (results == null)
                throw new ArgumentNullException(nameof(results));

            lock (_worker.Gate)
            {
                results.Clear();
                droppedCompletedResults = _droppedCompletedCount;
                encodeErrors = _encodeErrorCount;
                _droppedCompletedCount = 0;
                _encodeErrorCount = 0;
                if (_completed.Count == 0)
                    return;

                results.AddRange(_completed);
                _completed.Clear();
            }
        }

        public bool Stop(bool clearCompleted, out bool waitedForWorker)
        {
            var shouldWait = false;
            lock (_worker.Gate)
            {
                _worker.RequestStopLocked();
                _pending = null;
                shouldWait = _worker.IsRunning;
                if (clearCompleted)
                {
                    _completed.Clear();
                    _droppedCompletedCount = 0;
                    _encodeErrorCount = 0;
                }
            }

            waitedForWorker = shouldWait;
            if (!shouldWait)
                return true;

            if (_worker.Idle.Wait(_stopWaitMs))
                return true;

            lock (_worker.Gate)
                _worker.InvalidateTimedOutWorkerLocked();

            return false;
        }

        private void StartWorker(int workerGeneration)
        {
            var worker = new Thread(() => RunWorker(workerGeneration))
            {
                IsBackground = true,
                Name = _threadName,
                Priority = ThreadPriority.BelowNormal
            };
            worker.Start();
        }

        private void RunWorker(int workerGeneration)
        {
            try
            {
                while (true)
                {
                    TRequest request;
                    lock (_worker.Gate)
                    {
                        if (_worker.ShouldStopLocked(workerGeneration))
                        {
                            _worker.MarkStoppedIfCurrentLocked(workerGeneration);
                            return;
                        }

                        request = _pending;
                        _pending = null;
                        if (request == null)
                        {
                            _worker.MarkStoppedIfCurrentLocked(workerGeneration);
                            return;
                        }
                    }

                    if (request.Generation != workerGeneration)
                        continue;

                    TResult result;
                    try
                    {
                        result = _encode(request);
                    }
                    catch (Exception ex)
                    {
                        lock (_worker.Gate)
                        {
                            if (!_worker.ShouldStopLocked(workerGeneration)
                                && request.Generation == workerGeneration)
                                _encodeErrorCount++;
                        }

                        try
                        {
                            _onEncodeError?.Invoke(ex);
                        }
                        catch
                        {
                            // Diagnostics callbacks must not terminate the worker.
                        }

                        continue;
                    }

                    lock (_worker.Gate)
                    {
                        if (_worker.ShouldStopLocked(workerGeneration)
                            || request.Generation != workerGeneration)
                            continue;

                        while (_completed.Count >= _completedCapacity)
                        {
                            _completed.Dequeue();
                            _droppedCompletedCount++;
                        }

                        _completed.Enqueue(result);
                    }
                }
            }
            finally
            {
                var signalIdle = false;
                lock (_worker.Gate)
                    signalIdle = _worker.MarkStoppedIfCurrentLocked(workerGeneration);

                if (signalIdle)
                    _worker.Idle.Set();
            }
        }
    }
}
