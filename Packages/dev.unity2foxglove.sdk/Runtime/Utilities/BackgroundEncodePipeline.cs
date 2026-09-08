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
        private readonly Queue<string> _encodeErrors = new Queue<string>();
        private readonly Func<TRequest, TResult> _encode;
        private readonly Action<Exception> _onEncodeError;
        private readonly Action<TRequest> _onDropRequest;
        private readonly Action<TResult> _onDropResult;
        private readonly string _threadName;
        private readonly int _completedCapacity;
        private readonly int _stopWaitMs;
        private TRequest _pending;
        private int _droppedCompletedCount;
        private int _activeWorkerCount;
        private bool _disposeHandlesWhenWorkersExit;
        private bool _handleDisposalClaimed;
        private bool _disposed;

        // Internal synchronization seam used by lifecycle regression tests.
        // Production callers leave this unset, so it has no behavioral effect.
        internal Action<string> TestHook { get; set; }

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
            InvokeTestHook("EnqueueAfterDisposedCheck");

            var startWorker = false;
            var workerGeneration = 0;
            var rejectStoppingGeneration = false;
            TRequest replacedRequest = null;
            TRequest droppedRequest = null;
            var disposeHandles = false;
            startError = null;
            lock (_worker.Gate)
            {
                if (_disposed)
                {
                    replacedPending = false;
                    throw new ObjectDisposedException(GetType().Name);
                }

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
                    InvokeTestHook("EnqueueBeforeSignal");
                    try
                    {
                        _workerSignal.Set();
                        if (startWorker)
                            StartWorker(workerGeneration);
                    }
                    catch (Exception ex)
                    {
                        if (ReferenceEquals(_pending, request))
                        {
                            droppedRequest = _pending;
                            _pending = null;
                        }

                        if (startWorker)
                        {
                            _worker.MarkStartFailedIfCurrentLocked(workerGeneration);
                            _activeWorkerCount--;
                        }

                        disposeHandles = TryClaimHandleDisposalLocked();
                        startError = ex.Message;
                        startWorker = false;
                    }
                }
            }

            if (rejectStoppingGeneration)
            {
                DropRequest(request);
                return false;
            }

            DropRequest(replacedRequest);
            if (droppedRequest != null)
                DropRequest(droppedRequest);
            if (disposeHandles)
            {
                InvokeTestHook("DisposeBeforeHandleDisposal");
                DisposeWorkerHandles();
            }

            return string.IsNullOrEmpty(startError);
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
                encodeErrors = _encodeErrors.Count;
                _droppedCompletedCount = 0;
                _encodeErrors.Clear();
                if (_completed.Count == 0)
                    return;

                results.AddRange(_completed);
                _completed.Clear();
            }
        }

        public void Drain(
            List<TResult> results,
            List<string> encodeErrorMessages,
            out int droppedCompletedResults)
        {
            if (results == null)
                throw new ArgumentNullException(nameof(results));
            if (encodeErrorMessages == null)
                throw new ArgumentNullException(nameof(encodeErrorMessages));

            lock (_worker.Gate)
            {
                results.Clear();
                encodeErrorMessages.Clear();
                droppedCompletedResults = _droppedCompletedCount;
                _droppedCompletedCount = 0;
                if (_encodeErrors.Count > 0)
                {
                    encodeErrorMessages.AddRange(_encodeErrors);
                    _encodeErrors.Clear();
                }

                if (_completed.Count == 0)
                    return;

                results.AddRange(_completed);
                _completed.Clear();
            }
        }

        public bool Stop(bool clearCompleted, out bool waitedForWorker)
        {
            if (Volatile.Read(ref _disposed))
            {
                waitedForWorker = false;
                return true;
            }
            InvokeTestHook("StopAfterDisposedCheck");

            var shouldWait = false;
            TRequest pendingRequest;
            List<TResult> droppedResults = null;
            lock (_worker.Gate)
            {
                if (_disposed)
                {
                    waitedForWorker = false;
                    return true;
                }

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
                    _encodeErrors.Clear();
                }

                // Admission, stop publication, and signalling share one
                // linearization point.  Disposal cannot claim the handle
                // until this critical section has completed.
                _workerSignal.Set();
            }

            DropRequest(pendingRequest);
            DropResults(droppedResults);

            waitedForWorker = shouldWait;
            if (!shouldWait)
                return true;

            return WaitForWorkerRetirement();
        }

        public void Dispose()
        {
            if (Volatile.Read(ref _disposed))
                return;
            InvokeTestHook("DisposeAfterDisposedCheck");
            // Keep the terminal stop admission observable to the lifecycle
            // regression harness without splitting the actual state change.
            InvokeTestHook("StopAfterDisposedCheck");

            TRequest pendingRequest;
            List<TResult> droppedResults = null;
            var shouldWait = false;
            var disposeHandles = false;
            lock (_worker.Gate)
            {
                if (_disposed)
                    return;

                // Publish terminal state before releasing the lifecycle gate.
                // Any caller that passed the outer fast path must recheck
                // here, before it can acquire ownership or touch a handle.
                _disposed = true;
                _worker.RequestStopLocked();
                pendingRequest = _pending;
                _pending = null;
                shouldWait = _worker.IsRunning;
                if (_completed.Count > 0)
                {
                    droppedResults = new List<TResult>(_completed.Count);
                    droppedResults.AddRange(_completed);
                }

                _completed.Clear();
                _droppedCompletedCount = 0;
                _encodeErrors.Clear();
                _disposeHandlesWhenWorkersExit = true;
                _workerSignal.Set();
                disposeHandles = TryClaimHandleDisposalLocked();
            }

            DropRequest(pendingRequest);
            DropResults(droppedResults);

            if (shouldWait)
                WaitForWorkerRetirement();

            // A worker may have retired between the first claim and the
            // bounded wait.  Reclaim exactly once after observing that state.
            lock (_worker.Gate)
                disposeHandles |= TryClaimHandleDisposalLocked();

            if (disposeHandles)
            {
                InvokeTestHook("DisposeBeforeHandleDisposal");
                DisposeWorkerHandles();
            }
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
                            {
                                while (_encodeErrors.Count >= _completedCapacity)
                                    _encodeErrors.Dequeue();
                                _encodeErrors.Enqueue(ex?.Message ?? string.Empty);
                            }
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
                    if (signalIdle)
                        _worker.Idle.Set();
                    disposeHandles = TryClaimHandleDisposalLocked();
                }

                if (disposeHandles)
                {
                    InvokeTestHook("DisposeBeforeHandleDisposal");
                    DisposeWorkerHandles();
                }
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
            if (Volatile.Read(ref _disposed))
                throw new ObjectDisposedException(GetType().Name);
        }

        private bool WaitForWorkerRetirement()
        {
            try
            {
                if (_worker.Idle.Wait(_stopWaitMs))
                    return true;
            }
            catch (ObjectDisposedException)
            {
                // A terminal caller can retire and release the wait handle
                // while an earlier Stop is still unwinding its bounded wait.
                lock (_worker.Gate)
                    return !_worker.IsRunning;
            }

            lock (_worker.Gate)
            {
                if (!_worker.IsRunning)
                    return true;

                _worker.InvalidateTimedOutWorkerLocked();
            }

            return false;
        }

        private void InvokeTestHook(string point)
        {
            try
            {
                TestHook?.Invoke(point);
            }
            catch
            {
                // Test synchronization must never alter production control flow.
            }
        }
    }
}
