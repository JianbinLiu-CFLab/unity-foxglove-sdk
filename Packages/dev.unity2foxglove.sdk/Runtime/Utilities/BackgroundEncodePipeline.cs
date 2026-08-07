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
    internal sealed class BackgroundEncodePipeline<TRequest, TResult> : IDisposable
        where TRequest : class, IBackgroundEncodeRequest
    {
        private readonly BackgroundWorkerLifecycle _worker = new BackgroundWorkerLifecycle();
        private readonly AutoResetEvent _workerSignal = new AutoResetEvent(false);
        private readonly Queue<TResult> _completed = new Queue<TResult>();
        private readonly Func<TRequest, TResult> _encode;
        private readonly Action<Exception> _onEncodeError;
        private readonly Action<TRequest> _onDropRequest;
        private readonly Action<TResult> _onDropResult;
        private readonly string _threadName;
        private readonly int _completedCapacity;
        private readonly int _stopWaitMs;
        private TRequest _pending;
        private int _droppedCompletedCount;
        private int _encodeErrorCount;
        private int _activeWorkerCount;
        private bool _disposeHandlesWhenWorkersExit;
        private bool _handleDisposalClaimed;
        private bool _disposed;

        public BackgroundEncodePipeline(
            string threadName,
            int completedCapacity,
            int stopWaitMs,
            Func<TRequest, TResult> encode,
            Action<Exception> onEncodeError = null,
            Action<TRequest> onDropRequest = null,
            Action<TResult> onDropResult = null)
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
            _onDropRequest = onDropRequest;
            _onDropResult = onDropResult;
        }

        public bool Enqueue(TRequest request, out bool replacedPending, out string startError)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            ThrowIfDisposed();

            var startWorker = false;
            var workerGeneration = 0;
            var rejectStoppingGeneration = false;
            TRequest replacedRequest = null;
            startError = null;
            lock (_worker.Gate)
            {
                if (_worker.IsRunning && _worker.StopRequested)
                {
                    replacedPending = false;
                    startError = "Background encode worker is stopping.";
                    rejectStoppingGeneration = true;
                }
                else
                {
                    replacedRequest = _pending;
                    replacedPending = replacedRequest != null;
                    workerGeneration = _worker.StartOrReuseLocked(out startWorker);
                    if (startWorker)
                        _activeWorkerCount++;
                    request.Generation = workerGeneration;
                    _pending = request;
                    _workerSignal.Set();
                }
            }

            if (rejectStoppingGeneration)
            {
                DropRequest(request);
                return false;
            }

            DropRequest(replacedRequest);

            if (!startWorker)
                return true;

            try
            {
                StartWorker(workerGeneration);
                return true;
            }
            catch (Exception ex)
            {
                TRequest droppedRequest = null;
                var disposeHandles = false;
                lock (_worker.Gate)
                {
                    if (ReferenceEquals(_pending, request))
                    {
                        droppedRequest = _pending;
                        _pending = null;
                    }

                    _worker.MarkStartFailedIfCurrentLocked(workerGeneration);
                    _activeWorkerCount--;
                    disposeHandles = TryClaimHandleDisposalLocked();
                }

                DropRequest(droppedRequest);
                if (disposeHandles)
                    DisposeWorkerHandles();
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
            if (_disposed)
            {
                waitedForWorker = false;
                return true;
            }

            var shouldWait = false;
            TRequest pendingRequest;
            List<TResult> droppedResults = null;
            lock (_worker.Gate)
            {
                _worker.RequestStopLocked();
                pendingRequest = _pending;
                _pending = null;
                shouldWait = _worker.IsRunning;
                if (clearCompleted)
                {
                    if (_completed.Count > 0)
                    {
                        droppedResults = new List<TResult>(_completed.Count);
                        droppedResults.AddRange(_completed);
                    }

                    _completed.Clear();
                    _droppedCompletedCount = 0;
                    _encodeErrorCount = 0;
                }
            }

            _workerSignal.Set();
            DropRequest(pendingRequest);
            DropResults(droppedResults);

            waitedForWorker = shouldWait;
            if (!shouldWait)
                return true;

            if (_worker.Idle.Wait(_stopWaitMs))
                return true;

            lock (_worker.Gate)
                _worker.InvalidateTimedOutWorkerLocked();

            return false;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            Stop(clearCompleted: true, out _);

            var disposeHandles = false;
            lock (_worker.Gate)
            {
                _disposed = true;
                _disposeHandlesWhenWorkersExit = true;
                disposeHandles = TryClaimHandleDisposalLocked();
            }

            if (disposeHandles)
                DisposeWorkerHandles();
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
                    }

                    if (request == null)
                    {
                        _workerSignal.WaitOne();
                        continue;
                    }

                    if (request.Generation != workerGeneration)
                    {
                        DropRequest(request);
                        continue;
                    }

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

                        DropRequest(request);
                        continue;
                    }

                    var shouldDropResult = false;
                    List<TResult> droppedResults = null;
                    lock (_worker.Gate)
                    {
                        if (_worker.ShouldStopLocked(workerGeneration)
                            || request.Generation != workerGeneration)
                        {
                            shouldDropResult = true;
                        }
                        else
                        {
                            while (_completed.Count >= _completedCapacity)
                            {
                                var droppedResult = _completed.Dequeue();
                                droppedResults ??= new List<TResult>();
                                droppedResults.Add(droppedResult);
                                _droppedCompletedCount++;
                            }

                            _completed.Enqueue(result);
                        }
                    }

                    if (shouldDropResult)
                    {
                        DropResult(result);
                        continue;
                    }

                    DropResults(droppedResults);
                }
            }
            finally
            {
                var signalIdle = false;
                var disposeHandles = false;
                lock (_worker.Gate)
                {
                    signalIdle = _worker.MarkStoppedIfCurrentLocked(workerGeneration);
                    _activeWorkerCount--;
                    disposeHandles = TryClaimHandleDisposalLocked();
                }

                if (signalIdle)
                    _worker.Idle.Set();
                if (disposeHandles)
                    DisposeWorkerHandles();
            }
        }

        private bool TryClaimHandleDisposalLocked()
        {
            if (!_disposeHandlesWhenWorkersExit
                || _activeWorkerCount != 0
                || _handleDisposalClaimed)
                return false;

            _handleDisposalClaimed = true;
            return true;
        }

        private void DisposeWorkerHandles()
        {
            try
            {
                _workerSignal.Dispose();
            }
            finally
            {
                _worker.Dispose();
            }
        }

        private void DropRequest(TRequest request)
        {
            if (request == null || _onDropRequest == null)
                return;

            try
            {
                _onDropRequest(request);
            }
            catch
            {
                // Drop callbacks are cleanup paths and must not terminate workers.
            }
        }

        private void DropResult(TResult result)
        {
            if (_onDropResult == null)
                return;

            try
            {
                _onDropResult(result);
            }
            catch
            {
                // Drop callbacks are cleanup paths and must not terminate workers.
            }
        }

        private void DropResults(List<TResult> results)
        {
            if (results == null)
                return;

            foreach (var droppedResult in results)
                DropResult(droppedResult);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(GetType().Name);
        }
    }
}
