// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core/Runtime
// Purpose: Retain per-step Stop progress across retryable lifecycle failures.

using System;
using System.Runtime.ExceptionServices;

namespace Unity.FoxgloveSDK.Core
{
    internal enum RuntimeStopCleanupStep
    {
        ReplaySuppressionWarnings = 0,
        ReplaySnapshot = 1,
        ReplaySceneSnapshot = 2,
        ReplayPanelHistory = 3,
        ReplayOrchestrator = 4,
        Recording = 5,
        Session = 6
    }

    /// <summary>
    /// Tracks Stop cleanup independently. A failure leaves only its own step
    /// retryable; already completed steps are not replayed on the next attempt.
    /// </summary>
    internal sealed class RuntimeStopCleanupState
    {
        private const int StepCount = 7;
        private readonly bool[] _completed = new bool[StepCount];

        internal RuntimeStopCleanupState()
        {
            SetAll(true);
        }

        internal bool IsComplete
        {
            get
            {
                for (var i = 0; i < _completed.Length; i++)
                {
                    if (!_completed[i])
                        return false;
                }

                return true;
            }
        }

        internal bool IsCompleted(RuntimeStopCleanupStep step)
            => _completed[(int)step];

        /// <summary>
        /// Required ownership boundaries are safe to start again only after
        /// replay forwarders, recording ownership, and the retired session have
        /// all been detached. Replay-history housekeeping is best effort and
        /// must not poison a new session epoch by itself.
        /// </summary>
        internal bool IsReadyForStart
            => IsCompleted(RuntimeStopCleanupStep.ReplayOrchestrator)
               && IsCompleted(RuntimeStopCleanupStep.Recording)
               && IsCompleted(RuntimeStopCleanupStep.Session);

        /// <summary>Whether the resource-owning Stop steps are complete.</summary>
        internal bool IsResourceCleanupComplete => IsReadyForStart;

        internal void Reset()
        {
            SetAll(false);
        }

        internal void MarkComplete(RuntimeStopCleanupStep step)
        {
            _completed[(int)step] = true;
        }

        internal void TryCleanup(
            RuntimeStopCleanupStep step,
            Action cleanup,
            ref ExceptionDispatchInfo firstFailure)
        {
            if (IsCompleted(step))
                return;

            try
            {
                cleanup?.Invoke();
                MarkComplete(step);
            }
            catch (Exception exception)
            {
                firstFailure ??= ExceptionDispatchInfo.Capture(exception);
            }
        }

        private void SetAll(bool value)
        {
            for (var i = 0; i < _completed.Length; i++)
                _completed[i] = value;
        }
    }
}
