// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Publishers
// Purpose: Unified native point-cloud encode drain/queue/stop wrapper.

using System;
using System.Collections.Generic;
using Unity.FoxgloveSDK.Util;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Coordinates a generation-guarded background encode queue and main-thread
    /// drain/stop lifecycle for point-cloud payload encoders.
    /// </summary>
    internal sealed class PointCloudEncodePipeline<TRequest, TResult>
        where TRequest : class, IBackgroundEncodeRequest
    {
        private readonly BackgroundEncodePipeline<TRequest, TResult> _pipeline;
        private readonly List<TResult> _drainedResults = new List<TResult>();
        private readonly Func<TResult, bool> _isSuccess;
        private readonly Func<TResult, string> _failureMessage;
        private readonly Func<string, string> _formatFailureWarning;
        private readonly Action<TResult> _publishCompleted;
        private readonly Action<string> _logWarning;
        private readonly string _replacedPendingWarning;
        private readonly string _queueFailureMessagePrefix;
        private readonly Func<int, string> _droppedCompletedWarning;
        private readonly string _workerShutdownWarning;
        private readonly int _failureWarningIntervalFrames;
        private int _failureCount;
        private bool _warnedFailure;
        private bool _warnedReplacedPending;
        private bool _warnedWorkerShutdown;

        public PointCloudEncodePipeline(
            string threadName,
            int completedCapacity,
            int workerStopWaitMs,
            Func<TRequest, TResult> encode,
            Func<TResult, bool> isSuccess,
            Func<TResult, string> failureMessage,
            Func<string, string> formatFailureWarning,
            Action<TResult> publishCompleted,
            Action<string> logWarning,
            string replacedPendingWarning,
            string queueFailureMessagePrefix,
            Func<int, string> droppedCompletedWarning,
            string workerShutdownWarning,
            int failureWarningIntervalFrames)
        {
            _pipeline = new BackgroundEncodePipeline<TRequest, TResult>(
                threadName,
                completedCapacity,
                workerStopWaitMs,
                encode);

            _isSuccess = isSuccess ?? throw new ArgumentNullException(nameof(isSuccess));
            _failureMessage = failureMessage ?? throw new ArgumentNullException(nameof(failureMessage));
            _formatFailureWarning = formatFailureWarning ?? throw new ArgumentNullException(nameof(formatFailureWarning));
            _publishCompleted = publishCompleted ?? throw new ArgumentNullException(nameof(publishCompleted));
            _logWarning = logWarning ?? throw new ArgumentNullException(nameof(logWarning));
            _replacedPendingWarning = replacedPendingWarning;
            _queueFailureMessagePrefix = queueFailureMessagePrefix;
            _droppedCompletedWarning = droppedCompletedWarning ?? throw new ArgumentNullException(nameof(droppedCompletedWarning));
            _workerShutdownWarning = workerShutdownWarning;
            _failureWarningIntervalFrames = failureWarningIntervalFrames;
        }

        public void Queue(TRequest request, bool logQosDrops, Action onPendingDrop)
        {
            var queued = _pipeline.Enqueue(request, out var replacedPending, out var startError);
            if (replacedPending)
            {
                if (logQosDrops && !_warnedReplacedPending)
                {
                    _logWarning(_replacedPendingWarning);
                    _warnedReplacedPending = true;
                }

                onPendingDrop?.Invoke();
            }

            if (queued)
                return;

            LogFailure(_queueFailureMessagePrefix + (string.IsNullOrWhiteSpace(startError) ? "unknown error" : startError));
        }

        public void Drain(bool logQosDrops, Action<int> onDroppedCompleted, Action onResultsProcessed)
        {
            _pipeline.Drain(_drainedResults, out var droppedCompletedResults);
            if (droppedCompletedResults > 0 && logQosDrops)
                _logWarning(_droppedCompletedWarning(droppedCompletedResults));

            if (droppedCompletedResults > 0)
                onDroppedCompleted?.Invoke(droppedCompletedResults);

            if (_drainedResults.Count == 0)
                return;

            foreach (var result in _drainedResults)
            {
                if (!_isSuccess(result))
                {
                    LogFailure(_failureMessage(result));
                    continue;
                }

                _warnedFailure = false;
                _failureCount = 0;
                _warnedReplacedPending = false;
                _publishCompleted(result);
            }

            onResultsProcessed?.Invoke();
        }

        public void Stop(bool clearCompleted)
        {
            _drainedResults.Clear();
            if (_pipeline.Stop(clearCompleted, out var waitedForWorker))
            {
                if (waitedForWorker)
                    _warnedWorkerShutdown = false;
                return;
            }

            if (_warnedWorkerShutdown)
                return;

            _logWarning(_workerShutdownWarning);
            _warnedWorkerShutdown = true;
        }

        private void LogFailure(string message)
        {
            _failureCount++;
            if (_warnedFailure && _failureCount % _failureWarningIntervalFrames != 0)
                return;

            _warnedFailure = true;
            _logWarning(_formatFailureWarning(message));
        }
    }
}
